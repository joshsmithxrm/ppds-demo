using System.CommandLine;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using PPDS.Dataverse.Demo.Infrastructure;
using PPDS.Dataverse.Pooling;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Creates geographic reference data schema (ppds_state, ppds_city, ppds_zipcode) for volume testing.
/// </summary>
public static class CreateGeoSchemaCommand
{
    private const string PublisherPrefix = "ppds";

    public static Command Create()
    {
        var command = new Command("create-geo-schema", "Create geographic reference data tables for volume testing");

        var deleteFirstOption = new Option<bool>(
            "--delete-first",
            "Delete existing tables before creating (WARNING: destroys data)");

        // Use standardized options from GlobalOptionsExtensions
        var envOption = GlobalOptionsExtensions.CreateEnvironmentOption();
        var verboseOption = GlobalOptionsExtensions.CreateVerboseOption();
        var debugOption = GlobalOptionsExtensions.CreateDebugOption();

        command.AddOption(deleteFirstOption);
        command.AddOption(envOption);
        command.AddOption(verboseOption);
        command.AddOption(debugOption);

        command.SetHandler(async (bool deleteFirst, string? environment, bool verbose, bool debug) =>
        {
            var options = new GlobalOptions
            {
                Environment = environment,
                Verbose = verbose,
                Debug = debug
            };
            Environment.ExitCode = await ExecuteAsync(deleteFirst, options);
        }, deleteFirstOption, envOption, verboseOption, debugOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(bool deleteFirst, GlobalOptions options)
    {
        ConsoleWriter.Header("Create Geographic Schema for Volume Testing");

        using var host = HostFactory.CreateHostForMigration(options);
        var pool = HostFactory.GetConnectionPool(host, options.Environment);

        if (pool == null)
        {
            ConsoleWriter.Error("Connection pool not configured. See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md");
            return 1;
        }

        Console.WriteLine($"  Environment: {options.Environment ?? "Dev (default)"}");
        if (options.Debug)
            Console.WriteLine("  Logging: Debug");
        else if (options.Verbose)
            Console.WriteLine("  Logging: Verbose");
        Console.WriteLine();

        try
        {
            await using var client = await pool.GetClientAsync();

            if (deleteFirst)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  WARNING: --delete-first specified. Existing tables will be deleted!");
                Console.ResetColor();
                Console.WriteLine();

                await DeleteTableIfExistsAsync(client, "ppds_zipcode");
                await DeleteTableIfExistsAsync(client, "ppds_city");
                await DeleteTableIfExistsAsync(client, "ppds_state");
            }

            // Create tables in dependency order
            Console.WriteLine("  Creating tables...");
            Console.WriteLine();

            // 1. State (no dependencies)
            await CreateStateTableAsync(client);

            // 2. City (depends on State)
            await CreateCityTableAsync(client);

            // 3. ZipCode (depends on State, references City via N:N or lookup)
            await CreateZipCodeTableAsync(client);

            Console.WriteLine();
            Console.WriteLine("+==============================================================+");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("|              Schema Creation Complete                         |");
            Console.ResetColor();
            Console.WriteLine("+==============================================================+");
            Console.WriteLine();
            Console.WriteLine("  Tables created:");
            Console.WriteLine("    - ppds_state (State/Province)");
            Console.WriteLine("    - ppds_city (City with State lookup)");
            Console.WriteLine("    - ppds_zipcode (ZIP Code with State lookup)");
            Console.WriteLine();
            Console.WriteLine("  Next steps:");
            Console.WriteLine("    dotnet run -- load-geo-data     # Load geographic data");
            Console.WriteLine("    dotnet run -- clean-geo-data    # Clean up data");

            return 0;
        }
        catch (Exception ex)
        {
            ConsoleWriter.Exception(ex, options.Debug);
            return 1;
        }
    }

    private static async Task DeleteTableIfExistsAsync(IPooledClient client, string logicalName)
    {
        try
        {
            Console.Write($"    Deleting {logicalName}... ");
            var request = new DeleteEntityRequest { LogicalName = logicalName };
            await client.ExecuteAsync(request);
            ConsoleWriter.Success("Deleted");
        }
        catch (Exception ex) when (ex.Message.Contains("Could not find") || ex.Message.Contains("does not exist"))
        {
            Console.WriteLine("(not found, skipping)");
        }
    }

    private static async Task<bool> TableExistsAsync(IPooledClient client, string logicalName)
    {
        try
        {
            var request = new RetrieveEntityRequest
            {
                LogicalName = logicalName,
                EntityFilters = EntityFilters.Entity
            };
            await client.ExecuteAsync(request);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task CreateStateTableAsync(IPooledClient client)
    {
        const string logicalName = "ppds_state";

        Console.Write($"    Creating {logicalName}... ");

        if (await TableExistsAsync(client, logicalName))
        {
            Console.WriteLine("(already exists)");
            return;
        }

        var entity = new EntityMetadata
        {
            SchemaName = "ppds_State",
            LogicalName = logicalName,
            DisplayName = new Label("State", 1033),
            DisplayCollectionName = new Label("States", 1033),
            Description = new Label("US States and territories for geographic reference data", 1033),
            OwnershipType = OwnershipTypes.UserOwned,
            IsActivity = false
        };

        var primaryAttribute = new StringAttributeMetadata
        {
            SchemaName = "ppds_Name",
            LogicalName = "ppds_name",
            DisplayName = new Label("Name", 1033),
            Description = new Label("State name (e.g., California)", 1033),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
            MaxLength = 100
        };

        var request = new CreateEntityRequest
        {
            Entity = entity,
            PrimaryAttribute = primaryAttribute
        };

        await client.ExecuteAsync(request);

        // Add additional attributes
        await CreateStringAttributeAsync(client, logicalName, "ppds_abbreviation", "Abbreviation",
            "Two-letter state abbreviation (e.g., CA)", 2, AttributeRequiredLevel.ApplicationRequired);

        // Create alternate key on abbreviation for upsert support
        await CreateAlternateKeyAsync(client, logicalName, "ppds_ak_abbreviation", "Abbreviation",
            ["ppds_abbreviation"]);

        ConsoleWriter.Success("Created");
    }

    private static async Task CreateCityTableAsync(IPooledClient client)
    {
        const string logicalName = "ppds_city";

        Console.Write($"    Creating {logicalName}... ");

        if (await TableExistsAsync(client, logicalName))
        {
            Console.WriteLine("(already exists)");
            return;
        }

        var entity = new EntityMetadata
        {
            SchemaName = "ppds_City",
            LogicalName = logicalName,
            DisplayName = new Label("City", 1033),
            DisplayCollectionName = new Label("Cities", 1033),
            Description = new Label("US Cities for geographic reference data", 1033),
            OwnershipType = OwnershipTypes.UserOwned,
            IsActivity = false
        };

        var primaryAttribute = new StringAttributeMetadata
        {
            SchemaName = "ppds_Name",
            LogicalName = "ppds_name",
            DisplayName = new Label("Name", 1033),
            Description = new Label("City name", 1033),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
            MaxLength = 200
        };

        var request = new CreateEntityRequest
        {
            Entity = entity,
            PrimaryAttribute = primaryAttribute
        };

        await client.ExecuteAsync(request);

        // Add State lookup
        await CreateLookupAttributeAsync(client, logicalName, "ppds_stateid", "State",
            "ppds_state", AttributeRequiredLevel.ApplicationRequired);

        // Create composite alternate key on (name, stateid) for upsert support
        // This allows cities with the same name in different states
        await CreateAlternateKeyAsync(client, logicalName, "ppds_ak_name_state", "City-State",
            ["ppds_name", "ppds_stateid"]);

        ConsoleWriter.Success("Created");
    }

    private static async Task CreateZipCodeTableAsync(IPooledClient client)
    {
        const string logicalName = "ppds_zipcode";

        Console.Write($"    Creating {logicalName}... ");

        if (await TableExistsAsync(client, logicalName))
        {
            Console.WriteLine("(already exists)");
            return;
        }

        var entity = new EntityMetadata
        {
            SchemaName = "ppds_ZipCode",
            LogicalName = logicalName,
            DisplayName = new Label("ZIP Code", 1033),
            DisplayCollectionName = new Label("ZIP Codes", 1033),
            Description = new Label("US ZIP Codes for geographic reference data and volume testing", 1033),
            OwnershipType = OwnershipTypes.UserOwned,
            IsActivity = false
        };

        var primaryAttribute = new StringAttributeMetadata
        {
            SchemaName = "ppds_Code",
            LogicalName = "ppds_code",
            DisplayName = new Label("ZIP Code", 1033),
            Description = new Label("5-digit ZIP code", 1033),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
            MaxLength = 10
        };

        var request = new CreateEntityRequest
        {
            Entity = entity,
            PrimaryAttribute = primaryAttribute
        };

        await client.ExecuteAsync(request);

        // State lookup
        await CreateLookupAttributeAsync(client, logicalName, "ppds_stateid", "State",
            "ppds_state", AttributeRequiredLevel.ApplicationRequired);

        // City lookup
        await CreateLookupAttributeAsync(client, logicalName, "ppds_cityid", "City",
            "ppds_city", AttributeRequiredLevel.ApplicationRequired);

        // County
        await CreateStringAttributeAsync(client, logicalName, "ppds_county", "County",
            "County name", 100, AttributeRequiredLevel.None);

        // Coordinates
        await CreateDecimalAttributeAsync(client, logicalName, "ppds_latitude", "Latitude",
            "Latitude coordinate", AttributeRequiredLevel.None);

        await CreateDecimalAttributeAsync(client, logicalName, "ppds_longitude", "Longitude",
            "Longitude coordinate", AttributeRequiredLevel.None);

        // Create alternate key on ZIP code for upsert support
        await CreateAlternateKeyAsync(client, logicalName, "ppds_ak_code", "ZIP Code",
            ["ppds_code"]);

        ConsoleWriter.Success("Created");
    }

    private static async Task CreateStringAttributeAsync(IPooledClient client, string entityLogicalName,
        string attributeLogicalName, string displayName, string description, int maxLength,
        AttributeRequiredLevel requiredLevel)
    {
        var parts = attributeLogicalName.Split('_');
        var schemaName = parts.Length == 2
            ? parts[0] + "_" + char.ToUpper(parts[1][0]) + parts[1].Substring(1)
            : attributeLogicalName;

        var attribute = new StringAttributeMetadata
        {
            SchemaName = schemaName,
            LogicalName = attributeLogicalName,
            DisplayName = new Label(displayName, 1033),
            Description = new Label(description, 1033),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(requiredLevel),
            MaxLength = maxLength
        };

        var request = new CreateAttributeRequest
        {
            EntityName = entityLogicalName,
            Attribute = attribute
        };

        await client.ExecuteAsync(request);
    }

    private static async Task CreateIntegerAttributeAsync(IPooledClient client, string entityLogicalName,
        string attributeLogicalName, string displayName, string description, AttributeRequiredLevel requiredLevel)
    {
        var parts = attributeLogicalName.Split('_');
        var schemaName = parts.Length == 2
            ? parts[0] + "_" + char.ToUpper(parts[1][0]) + parts[1].Substring(1)
            : attributeLogicalName;

        var attribute = new IntegerAttributeMetadata
        {
            SchemaName = schemaName,
            LogicalName = attributeLogicalName,
            DisplayName = new Label(displayName, 1033),
            Description = new Label(description, 1033),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(requiredLevel),
            MinValue = 0,
            MaxValue = int.MaxValue
        };

        var request = new CreateAttributeRequest
        {
            EntityName = entityLogicalName,
            Attribute = attribute
        };

        await client.ExecuteAsync(request);
    }

    private static async Task CreateDecimalAttributeAsync(IPooledClient client, string entityLogicalName,
        string attributeLogicalName, string displayName, string description, AttributeRequiredLevel requiredLevel)
    {
        var parts = attributeLogicalName.Split('_');
        var schemaName = parts.Length == 2
            ? parts[0] + "_" + char.ToUpper(parts[1][0]) + parts[1].Substring(1)
            : attributeLogicalName;

        var attribute = new DecimalAttributeMetadata
        {
            SchemaName = schemaName,
            LogicalName = attributeLogicalName,
            DisplayName = new Label(displayName, 1033),
            Description = new Label(description, 1033),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(requiredLevel),
            Precision = 6,
            MinValue = -180,
            MaxValue = 180
        };

        var request = new CreateAttributeRequest
        {
            EntityName = entityLogicalName,
            Attribute = attribute
        };

        await client.ExecuteAsync(request);
    }

    private static async Task CreateLookupAttributeAsync(IPooledClient client, string entityLogicalName,
        string attributeLogicalName, string displayName, string referencedEntity, AttributeRequiredLevel requiredLevel)
    {
        var parts = attributeLogicalName.Split('_');
        var schemaName = parts.Length == 2
            ? parts[0] + "_" + char.ToUpper(parts[1][0]) + parts[1].Substring(1)
            : attributeLogicalName;

        // For lookups, we need to create a 1:N relationship
        var request = new CreateOneToManyRequest
        {
            Lookup = new LookupAttributeMetadata
            {
                SchemaName = schemaName,
                LogicalName = attributeLogicalName,
                DisplayName = new Label(displayName, 1033),
                RequiredLevel = new AttributeRequiredLevelManagedProperty(requiredLevel)
            },
            OneToManyRelationship = new OneToManyRelationshipMetadata
            {
                SchemaName = $"{PublisherPrefix}_{referencedEntity}_{entityLogicalName}",
                ReferencedEntity = referencedEntity,
                ReferencingEntity = entityLogicalName,
                CascadeConfiguration = new CascadeConfiguration
                {
                    Assign = CascadeType.NoCascade,
                    Delete = CascadeType.RemoveLink,
                    Merge = CascadeType.NoCascade,
                    Reparent = CascadeType.NoCascade,
                    Share = CascadeType.NoCascade,
                    Unshare = CascadeType.NoCascade
                }
            }
        };

        await client.ExecuteAsync(request);
    }

    private static async Task CreateAlternateKeyAsync(IPooledClient client, string entityLogicalName,
        string keyName, string displayName, params string[] attributeLogicalNames)
    {
        var keyMetadata = new EntityKeyMetadata
        {
            SchemaName = keyName,
            DisplayName = new Label(displayName, 1033),
            KeyAttributes = attributeLogicalNames
        };

        var request = new CreateEntityKeyRequest
        {
            EntityName = entityLogicalName,
            EntityKey = keyMetadata
        };

        await client.ExecuteAsync(request);
        // Note: Alternate keys activate asynchronously. They will be ready by the time
        // load-geo-data runs (typically within seconds on empty tables).
    }
}
