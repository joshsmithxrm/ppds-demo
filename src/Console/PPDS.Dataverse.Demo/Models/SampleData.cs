using Microsoft.Xrm.Sdk;

namespace PPDS.Dataverse.Demo.Models;

/// <summary>
/// Sample data for demonstrating data migration scenarios.
/// Uses a consistent naming prefix (PPDS-) for easy identification and cleanup.
/// </summary>
public static class SampleData
{
    /// <summary>
    /// Prefix used for all sample records to enable easy identification and cleanup.
    /// </summary>
    public const string Prefix = "PPDS-";

    /// <summary>
    /// Gets sample account entities WITHOUT parent/child relationships.
    /// Use GetAccountParentUpdates() to set parent relationships after initial creation.
    /// </summary>
    /// <remarks>
    /// Structure (after parent updates applied):
    /// - Contoso Ltd (parent)
    ///   - Contoso East (child)
    ///   - Contoso West (child)
    /// - Fabrikam Inc (standalone)
    /// - Adventure Works (standalone)
    /// - Northwind Traders (standalone)
    /// </remarks>
    public static List<Entity> GetAccounts()
    {
        // Use deterministic GUIDs based on names for idempotent seeding
        var contosoId = CreateDeterministicGuid("PPDS-Contoso Ltd");
        var contosoEastId = CreateDeterministicGuid("PPDS-Contoso East");
        var contosoWestId = CreateDeterministicGuid("PPDS-Contoso West");
        var fabrikamId = CreateDeterministicGuid("PPDS-Fabrikam Inc");
        var adventureWorksId = CreateDeterministicGuid("PPDS-Adventure Works");
        var northwindId = CreateDeterministicGuid("PPDS-Northwind Traders");

        // First pass: create all accounts WITHOUT parent references
        // Parent relationships will be set in a second pass via GetAccountParentUpdates()
        return new List<Entity>
        {
            // Parent account (accountcategorycode: 1=Preferred Customer, 2=Standard)
            CreateAccount(contosoId, "PPDS-Contoso Ltd", null, new OptionSetValue(1), "Technology", "555-0100", "https://contoso.example.com"),

            // Child accounts (parent will be set in second pass)
            CreateAccount(contosoEastId, "PPDS-Contoso East", null, new OptionSetValue(2), "Technology", "555-0101", null),
            CreateAccount(contosoWestId, "PPDS-Contoso West", null, new OptionSetValue(2), "Technology", "555-0102", null),

            // Standalone accounts
            CreateAccount(fabrikamId, "PPDS-Fabrikam Inc", null, new OptionSetValue(1), "Manufacturing", "555-0200", "https://fabrikam.example.com"),
            CreateAccount(adventureWorksId, "PPDS-Adventure Works", null, new OptionSetValue(1), "Retail", "555-0300", "https://adventureworks.example.com"),
            CreateAccount(northwindId, "PPDS-Northwind Traders", null, new OptionSetValue(2), "Wholesale", "555-0400", null),
        };
    }

    /// <summary>
    /// Gets account updates to set parent relationships.
    /// Should be applied after accounts are created.
    /// </summary>
    public static List<Entity> GetAccountParentUpdates()
    {
        var contosoId = CreateDeterministicGuid("PPDS-Contoso Ltd");
        var contosoEastId = CreateDeterministicGuid("PPDS-Contoso East");
        var contosoWestId = CreateDeterministicGuid("PPDS-Contoso West");

        return new List<Entity>
        {
            new Entity("account", contosoEastId)
            {
                ["parentaccountid"] = new EntityReference("account", contosoId)
            },
            new Entity("account", contosoWestId)
            {
                ["parentaccountid"] = new EntityReference("account", contosoId)
            },
        };
    }

    /// <summary>
    /// Gets sample contact entities linked to accounts.
    /// </summary>
    public static List<Entity> GetContacts()
    {
        // Account IDs (must match GetAccounts)
        var contosoId = CreateDeterministicGuid("PPDS-Contoso Ltd");
        var contosoEastId = CreateDeterministicGuid("PPDS-Contoso East");
        var contosoWestId = CreateDeterministicGuid("PPDS-Contoso West");
        var fabrikamId = CreateDeterministicGuid("PPDS-Fabrikam Inc");
        var adventureWorksId = CreateDeterministicGuid("PPDS-Adventure Works");
        var northwindId = CreateDeterministicGuid("PPDS-Northwind Traders");

        return new List<Entity>
        {
            // Contoso Ltd contacts
            CreateContact("PPDS-John Smith", "John", "Smith", "CEO", contosoId, "john.smith@contoso.example.com", "555-0110"),
            CreateContact("PPDS-Jane Doe", "Jane", "Doe", "CTO", contosoId, "jane.doe@contoso.example.com", "555-0111"),
            CreateContact("PPDS-Bob Johnson", "Bob", "Johnson", "CFO", contosoId, "bob.johnson@contoso.example.com", "555-0112"),

            // Contoso East contacts
            CreateContact("PPDS-Alice Brown", "Alice", "Brown", "Regional Manager", contosoEastId, "alice.brown@contoso.example.com", "555-0120"),
            CreateContact("PPDS-Charlie Wilson", "Charlie", "Wilson", "Sales Lead", contosoEastId, "charlie.wilson@contoso.example.com", "555-0121"),

            // Contoso West contacts
            CreateContact("PPDS-Diana Miller", "Diana", "Miller", "Regional Manager", contosoWestId, "diana.miller@contoso.example.com", "555-0130"),
            CreateContact("PPDS-Edward Davis", "Edward", "Davis", "Operations Lead", contosoWestId, "edward.davis@contoso.example.com", "555-0131"),

            // Fabrikam contacts
            CreateContact("PPDS-Frank Garcia", "Frank", "Garcia", "President", fabrikamId, "frank.garcia@fabrikam.example.com", "555-0210"),
            CreateContact("PPDS-Grace Martinez", "Grace", "Martinez", "VP Sales", fabrikamId, "grace.martinez@fabrikam.example.com", "555-0211"),
            CreateContact("PPDS-Henry Rodriguez", "Henry", "Rodriguez", "VP Engineering", fabrikamId, "henry.rodriguez@fabrikam.example.com", "555-0212"),

            // Adventure Works contacts
            CreateContact("PPDS-Ivy Lee", "Ivy", "Lee", "Owner", adventureWorksId, "ivy.lee@adventureworks.example.com", "555-0310"),
            CreateContact("PPDS-Jack Taylor", "Jack", "Taylor", "Store Manager", adventureWorksId, "jack.taylor@adventureworks.example.com", "555-0311"),

            // Northwind Traders contacts
            CreateContact("PPDS-Karen White", "Karen", "White", "Managing Director", northwindId, "karen.white@northwind.example.com", "555-0410"),
            CreateContact("PPDS-Leo Harris", "Leo", "Harris", "Procurement Manager", northwindId, "leo.harris@northwind.example.com", "555-0411"),
            CreateContact("PPDS-Maria Clark", "Maria", "Clark", "Logistics Coordinator", northwindId, "maria.clark@northwind.example.com", "555-0412"),
        };
    }

    /// <summary>
    /// Gets account IDs for use in queries and cleanup.
    /// </summary>
    public static List<Guid> GetAccountIds()
    {
        return new List<Guid>
        {
            CreateDeterministicGuid("PPDS-Contoso Ltd"),
            CreateDeterministicGuid("PPDS-Contoso East"),
            CreateDeterministicGuid("PPDS-Contoso West"),
            CreateDeterministicGuid("PPDS-Fabrikam Inc"),
            CreateDeterministicGuid("PPDS-Adventure Works"),
            CreateDeterministicGuid("PPDS-Northwind Traders"),
        };
    }

    /// <summary>
    /// Gets contact IDs for use in queries and cleanup.
    /// </summary>
    public static List<Guid> GetContactIds()
    {
        var contacts = new[]
        {
            "PPDS-John Smith", "PPDS-Jane Doe", "PPDS-Bob Johnson",
            "PPDS-Alice Brown", "PPDS-Charlie Wilson",
            "PPDS-Diana Miller", "PPDS-Edward Davis",
            "PPDS-Frank Garcia", "PPDS-Grace Martinez", "PPDS-Henry Rodriguez",
            "PPDS-Ivy Lee", "PPDS-Jack Taylor",
            "PPDS-Karen White", "PPDS-Leo Harris", "PPDS-Maria Clark"
        };

        return contacts.Select(CreateDeterministicGuid).ToList();
    }

    private static Entity CreateAccount(
        Guid id,
        string name,
        Guid? parentAccountId,
        OptionSetValue? accountCategoryCode,
        string? industry,
        string? phone,
        string? website)
    {
        var account = new Entity("account", id)
        {
            ["accountid"] = id, // Required for UpsertMultiple to use this ID
            ["name"] = name,
            ["telephone1"] = phone,
        };

        if (parentAccountId.HasValue)
        {
            account["parentaccountid"] = new EntityReference("account", parentAccountId.Value);
        }

        if (accountCategoryCode != null)
        {
            account["accountcategorycode"] = accountCategoryCode;
        }

        if (!string.IsNullOrEmpty(industry))
        {
            account["description"] = $"Industry: {industry}";
        }

        if (!string.IsNullOrEmpty(website))
        {
            account["websiteurl"] = website;
        }

        return account;
    }

    private static Entity CreateContact(
        string fullName,
        string firstName,
        string lastName,
        string jobTitle,
        Guid parentCustomerId,
        string email,
        string phone)
    {
        var id = CreateDeterministicGuid(fullName);

        return new Entity("contact", id)
        {
            ["contactid"] = id, // Required for UpsertMultiple to use this ID
            ["firstname"] = firstName,
            ["lastname"] = lastName,
            ["fullname"] = fullName.Replace(Prefix, ""), // Remove prefix for display name
            ["jobtitle"] = jobTitle,
            ["parentcustomerid"] = new EntityReference("account", parentCustomerId),
            ["emailaddress1"] = email,
            ["telephone1"] = phone,
        };
    }

    /// <summary>
    /// Creates a deterministic GUID from a string for idempotent seeding.
    /// Same input always produces same GUID.
    /// </summary>
    private static Guid CreateDeterministicGuid(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }
}
