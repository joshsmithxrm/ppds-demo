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
    Console.WriteLine("Commands:");
    Console.WriteLine("  whoami          Test connectivity with WhoAmI request");
    Console.WriteLine("  seed            Create sample accounts and contacts");
    Console.WriteLine("  clean           Remove sample data from Dataverse");
    Console.WriteLine("  test-migration  End-to-end test of ppds-migrate CLI");
    Console.WriteLine("  demo-features   Demo new migration features (M2M, filtering, etc.)");
    Console.WriteLine("  migrate-to-qa   Export from Dev and import to QA");
    Console.WriteLine("  generate-user-mapping  Generate user mapping file for cross-env migration");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run -- whoami");
    Console.WriteLine("  dotnet run -- seed");
    Console.WriteLine("  dotnet run -- clean");
    Console.WriteLine("  dotnet run -- test-migration");
    Console.WriteLine("  dotnet run -- demo-features --feature all");
    Console.WriteLine("  dotnet run -- migrate-to-qa --dry-run");
    Console.WriteLine("  dotnet run -- generate-user-mapping --analyze");
    Console.WriteLine();
    Console.WriteLine("Configuration:");
    Console.WriteLine("  Connection is configured via .NET User Secrets.");
    Console.WriteLine("  See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md for setup.");
    Console.WriteLine();
});

return await rootCommand.InvokeAsync(args);
