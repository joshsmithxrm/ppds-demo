using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PPDS.Dataverse.DependencyInjection;
using PPDS.Dataverse.Pooling;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Base class for commands that need Dataverse connectivity.
/// </summary>
public abstract class CommandBase
{
    /// <summary>
    /// Creates and configures the host with Dataverse connection pool.
    /// </summary>
    public static IHost CreateHost(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddDataverseConnectionPool(context.Configuration);
            })
            .Build();
    }

    /// <summary>
    /// Validates the connection pool is enabled and returns it.
    /// </summary>
    public static IDataverseConnectionPool? GetConnectionPool(IHost host)
    {
        var pool = host.Services.GetRequiredService<IDataverseConnectionPool>();

        if (!pool.IsEnabled)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Connection pool is not enabled.");
            Console.WriteLine();
            Console.ResetColor();
            Console.WriteLine("Configure using .NET User Secrets:");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  dotnet user-secrets set \"Dataverse:Connections:0:Name\" \"Primary\"");
            Console.WriteLine("  dotnet user-secrets set \"Dataverse:Connections:0:ConnectionString\" \"AuthType=ClientSecret;Url=...\"");
            Console.ResetColor();
            Console.WriteLine();
            return null;
        }

        return pool;
    }

    /// <summary>
    /// Writes a success message in green.
    /// </summary>
    public static void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Writes an error message in red.
    /// </summary>
    public static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Writes an info message in cyan.
    /// </summary>
    public static void WriteInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
