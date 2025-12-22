using System.CommandLine;
using PPDS.Dataverse.Demo.Commands;

// PPDS.Dataverse Demo CLI
// Demonstrates connection pooling, bulk operations, and data migration workflows.

var rootCommand = new RootCommand("PPDS.Dataverse Demo - Connection pool and data migration demos")
{
    Name = "ppds-dataverse-demo"
};

// Add subcommands
rootCommand.AddCommand(WhoAmICommand.Create());
rootCommand.AddCommand(SeedCommand.Create());
rootCommand.AddCommand(CleanCommand.Create());
rootCommand.AddCommand(TestMigrationCommand.Create());
rootCommand.AddCommand(MigrationFeaturesCommand.Create());
rootCommand.AddCommand(CrossEnvMigrationCommand.Create());
rootCommand.AddCommand(GenerateUserMappingCommand.Create());
rootCommand.AddCommand(CreateGeoSchemaCommand.Create());
rootCommand.AddCommand(LoadGeoDataCommand.Create());
rootCommand.AddCommand(CleanGeoDataCommand.Create());

// Default behavior: show help if no command specified
rootCommand.SetHandler(() =>
{
    Console.WriteLine("PPDS.Dataverse Demo");
    Console.WriteLine("===================");
    Console.WriteLine();
    Console.WriteLine("Sample Data Commands:");
    Console.WriteLine("  whoami              Test connectivity with WhoAmI request");
    Console.WriteLine("  seed                Create sample accounts and contacts");
    Console.WriteLine("  clean [--env QA]    Remove sample data from Dataverse");
    Console.WriteLine();
    Console.WriteLine("Geographic Data Commands (Volume Testing):");
    Console.WriteLine("  create-geo-schema   Create geographic tables (state, city, zipcode)");
    Console.WriteLine("  load-geo-data       Download and load 42K US ZIP codes");
    Console.WriteLine("  clean-geo-data      Bulk delete geographic data");
    Console.WriteLine();
    Console.WriteLine("Migration Commands:");
    Console.WriteLine("  test-migration      End-to-end test of ppds-migrate CLI");
    Console.WriteLine("  demo-features       Demo migration features (M2M, filtering, etc.)");
    Console.WriteLine("  migrate-to-qa       Export from Dev and import to QA");
    Console.WriteLine("  generate-user-mapping  Generate user mapping for cross-env migration");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run -- whoami");
    Console.WriteLine("  dotnet run -- seed");
    Console.WriteLine("  dotnet run -- clean --env QA");
    Console.WriteLine("  dotnet run -- create-geo-schema");
    Console.WriteLine("  dotnet run -- load-geo-data --limit 1000");
    Console.WriteLine("  dotnet run -- migrate-to-qa --dry-run");
    Console.WriteLine();
    Console.WriteLine("Configuration:");
    Console.WriteLine("  Connections configured via .NET User Secrets.");
    Console.WriteLine("  Pool: Dataverse:Connections:0:ConnectionString");
    Console.WriteLine("  Envs: Environments:Dev:ConnectionString, Environments:QA:ConnectionString");
    Console.WriteLine();
});

return await rootCommand.InvokeAsync(args);
