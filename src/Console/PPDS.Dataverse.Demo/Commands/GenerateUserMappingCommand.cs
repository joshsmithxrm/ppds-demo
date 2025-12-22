using System.CommandLine;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Generates a user mapping file for cross-environment migration.
/// Matches users by Azure AD Object ID since systemuserid differs across environments.
/// </summary>
public static class GenerateUserMappingCommand
{
    private static readonly string OutputPath = Path.Combine(AppContext.BaseDirectory, "user-mapping.xml");

    public static Command Create()
    {
        var command = new Command("generate-user-mapping", "Generate user mapping file for cross-environment migration");

        var outputOption = new Option<string>(
            "--output",
            () => OutputPath,
            "Output path for the user mapping XML file");

        var analyzeOnlyOption = new Option<bool>(
            "--analyze",
            "Analyze user differences without generating mapping file");

        command.AddOption(outputOption);
        command.AddOption(analyzeOnlyOption);

        command.SetHandler(async (string output, bool analyzeOnly) =>
        {
            Environment.ExitCode = await ExecuteAsync(output, analyzeOnly);
        }, outputOption, analyzeOnlyOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(string outputPath, bool analyzeOnly)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       Generate User Mapping: Dev → QA                        ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        using var host = CommandBase.CreateHost([]);
        var config = host.Services.GetRequiredService<IConfiguration>();

        var (devConnectionString, devName) = CommandBase.ResolveEnvironment(config, "Dev");
        var (qaConnectionString, qaName) = CommandBase.ResolveEnvironment(config, "QA");

        if (string.IsNullOrEmpty(devConnectionString))
        {
            CommandBase.WriteError("Dev connection not found. Configure Environments:Dev:ConnectionString in user-secrets.");
            return 1;
        }

        if (string.IsNullOrEmpty(qaConnectionString))
        {
            CommandBase.WriteError("QA connection not found. Configure Environments:QA:ConnectionString in user-secrets.");
            return 1;
        }

        try
        {
            // Connect to both environments
            Console.WriteLine("  Connecting to environments...");
            using var devClient = new ServiceClient(devConnectionString);
            using var qaClient = new ServiceClient(qaConnectionString);

            if (!devClient.IsReady || !qaClient.IsReady)
            {
                CommandBase.WriteError($"Connection failed: Dev={devClient.IsReady}, QA={qaClient.IsReady}");
                return 1;
            }

            Console.WriteLine($"    {devName}: Connected");
            Console.WriteLine($"    {qaName}: Connected");
            Console.WriteLine();

            // Query users from both environments
            Console.WriteLine("  Querying users...");
            var devUsers = await QueryUsersAsync(devClient);
            var qaUsers = await QueryUsersAsync(qaClient);

            Console.WriteLine($"    {devName}: {devUsers.Count} users");
            Console.WriteLine($"    {qaName}: {qaUsers.Count} users");
            Console.WriteLine();

            // Build lookup by AAD Object ID
            var qaUsersByAadId = qaUsers
                .Where(u => u.AadObjectId.HasValue)
                .ToDictionary(u => u.AadObjectId!.Value, u => u);

            // Build lookup by domain name as fallback
            var qaUsersByDomain = qaUsers
                .Where(u => !string.IsNullOrEmpty(u.DomainName))
                .GroupBy(u => u.DomainName!.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First());

            // Match users
            var mappings = new List<UserMappingInfo>();
            var unmapped = new List<UserInfo>();

            foreach (var devUser in devUsers)
            {
                UserInfo? qaUser = null;

                // Try AAD Object ID first
                if (devUser.AadObjectId.HasValue &&
                    qaUsersByAadId.TryGetValue(devUser.AadObjectId.Value, out qaUser))
                {
                    mappings.Add(new UserMappingInfo
                    {
                        Source = devUser,
                        Target = qaUser,
                        MatchedBy = "AadObjectId"
                    });
                }
                // Fallback to domain name
                else if (!string.IsNullOrEmpty(devUser.DomainName) &&
                         qaUsersByDomain.TryGetValue(devUser.DomainName.ToLowerInvariant(), out qaUser))
                {
                    mappings.Add(new UserMappingInfo
                    {
                        Source = devUser,
                        Target = qaUser,
                        MatchedBy = "DomainName"
                    });
                }
                else
                {
                    unmapped.Add(devUser);
                }
            }

            // Report results
            Console.WriteLine("  Mapping Results:");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"    Matched: {mappings.Count}");
            Console.ResetColor();

            var byAad = mappings.Count(m => m.MatchedBy == "AadObjectId");
            var byDomain = mappings.Count(m => m.MatchedBy == "DomainName");
            Console.WriteLine($"      By AAD Object ID: {byAad}");
            Console.WriteLine($"      By Domain Name: {byDomain}");

            if (unmapped.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"    Unmapped: {unmapped.Count}");
                Console.ResetColor();
            }
            Console.WriteLine();

            // Show sample mappings
            Console.WriteLine("  Sample Mappings (first 5):");
            foreach (var mapping in mappings.Take(5))
            {
                Console.WriteLine($"    {mapping.Source.FullName}");
                Console.WriteLine($"      Dev: {mapping.Source.SystemUserId}");
                Console.WriteLine($"      QA:  {mapping.Target.SystemUserId} (matched by {mapping.MatchedBy})");
            }
            Console.WriteLine();

            // Show unmapped users
            if (unmapped.Count > 0)
            {
                Console.WriteLine("  Unmapped Users (first 10):");
                foreach (var user in unmapped.Take(10))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"    {user.FullName} ({user.DomainName ?? "no domain"})");
                    Console.ResetColor();
                }
                Console.WriteLine();
            }

            if (analyzeOnly)
            {
                Console.WriteLine("  [ANALYZE ONLY] No mapping file generated.");
                return 0;
            }

            // Generate mapping file
            Console.WriteLine($"  Generating mapping file: {outputPath}");
            GenerateMappingFile(outputPath, mappings);
            CommandBase.WriteSuccess($"  Generated {mappings.Count} mappings");
            Console.WriteLine();

            Console.WriteLine("  Usage:");
            Console.WriteLine($"    ppds-migrate import --data <file> --user-mapping \"{outputPath}\"");

            return 0;
        }
        catch (Exception ex)
        {
            CommandBase.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<List<UserInfo>> QueryUsersAsync(ServiceClient client)
    {
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet(
                "systemuserid",
                "fullname",
                "domainname",
                "internalemailaddress",
                "azureactivedirectoryobjectid",
                "isdisabled",
                "accessmode"
            ),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    // Exclude disabled users
                    new ConditionExpression("isdisabled", ConditionOperator.Equal, false)
                }
            }
        };

        var results = await client.RetrieveMultipleAsync(query);
        return results.Entities.Select(e => new UserInfo
        {
            SystemUserId = e.Id,
            FullName = e.GetAttributeValue<string>("fullname") ?? "(no name)",
            DomainName = e.GetAttributeValue<string>("domainname"),
            Email = e.GetAttributeValue<string>("internalemailaddress"),
            AadObjectId = e.GetAttributeValue<Guid?>("azureactivedirectoryobjectid"),
            AccessMode = e.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("accessmode")?.Value ?? 0
        }).ToList();
    }

    private static void GenerateMappingFile(string path, List<UserMappingInfo> mappings)
    {
        var doc = new XDocument(
            new XElement("mappings",
                new XAttribute("useCurrentUserAsDefault", "true"),
                mappings.Select(m => new XElement("mapping",
                    new XAttribute("sourceId", m.Source.SystemUserId),
                    new XAttribute("sourceName", m.Source.FullName),
                    new XAttribute("targetId", m.Target.SystemUserId),
                    new XAttribute("targetName", m.Target.FullName)
                ))
            )
        );

        doc.Save(path);
    }

    private class UserInfo
    {
        public Guid SystemUserId { get; set; }
        public string FullName { get; set; } = "";
        public string? DomainName { get; set; }
        public string? Email { get; set; }
        public Guid? AadObjectId { get; set; }
        public int AccessMode { get; set; }
    }

    private class UserMappingInfo
    {
        public UserInfo Source { get; set; } = null!;
        public UserInfo Target { get; set; } = null!;
        public string MatchedBy { get; set; } = "";
    }
}
