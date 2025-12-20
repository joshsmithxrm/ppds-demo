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

// Default behavior: show help if no command specified
rootCommand.SetHandler(() =>
{
    Console.WriteLine("PPDS.Dataverse Demo");
    Console.WriteLine("===================");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  whoami  Test connectivity with WhoAmI request");
    Console.WriteLine("  seed    Create sample accounts and contacts");
    Console.WriteLine("  clean   Remove sample data from Dataverse");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run -- whoami");
    Console.WriteLine("  dotnet run -- seed");
    Console.WriteLine("  dotnet run -- clean");
    Console.WriteLine();
    Console.WriteLine("Configuration:");
    Console.WriteLine("  Connection is configured via .NET User Secrets.");
    Console.WriteLine("  See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md for setup.");
    Console.WriteLine();
});

return await rootCommand.InvokeAsync(args);
