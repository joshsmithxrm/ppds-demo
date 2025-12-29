namespace PPDS.Dataverse.Demo.Infrastructure;

/// <summary>
/// Centralized console output utilities.
/// Provides consistent formatting for success, error, info, and progress messages.
/// </summary>
public static class ConsoleWriter
{
    /// <summary>
    /// Writes a success message in green.
    /// </summary>
    public static void Success(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Writes an error message in red.
    /// </summary>
    public static void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Writes an info message in cyan.
    /// </summary>
    public static void Info(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Writes a warning message in yellow.
    /// </summary>
    public static void Warning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Writes a dim/debug message in dark gray.
    /// </summary>
    public static void Debug(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Writes a header box with the given title.
    /// </summary>
    public static void Header(string title)
    {
        Console.WriteLine("+==============================================================+");
        Console.WriteLine($"|{title.PadLeft((62 + title.Length) / 2).PadRight(62)}|");
        Console.WriteLine("+==============================================================+");
        Console.WriteLine();
    }

    /// <summary>
    /// Writes a section header.
    /// </summary>
    public static void Section(string title)
    {
        Console.WriteLine("+-----------------------------------------------------------------+");
        Console.WriteLine($"| {title,-63}|");
        Console.WriteLine("+-----------------------------------------------------------------+");
    }

    /// <summary>
    /// Writes a result banner (success or failure).
    /// </summary>
    public static void ResultBanner(string message, bool success)
    {
        Console.WriteLine("+==============================================================+");
        Console.ForegroundColor = success ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"|{message.PadLeft((62 + message.Length) / 2).PadRight(62)}|");
        Console.ResetColor();
        Console.WriteLine("+==============================================================+");
    }

    /// <summary>
    /// Writes a pass/fail indicator.
    /// </summary>
    public static void PassFail(bool passed, string? suffix = null)
    {
        Console.ForegroundColor = passed ? ConsoleColor.Green : ConsoleColor.Red;
        Console.Write(passed ? "[PASS]" : "[FAIL]");
        Console.ResetColor();
        if (suffix != null)
        {
            Console.Write(suffix);
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Writes exception details. Shows stack trace only in debug mode.
    /// </summary>
    public static void Exception(Exception ex, bool debug = false)
    {
        Error($"Error: {ex.Message}");
        if (debug && ex.StackTrace != null)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
        }
    }

    /// <summary>
    /// Writes a labeled value.
    /// </summary>
    public static void Labeled(string label, object? value, int indent = 2)
    {
        var padding = new string(' ', indent);
        Console.WriteLine($"{padding}{label}: {value}");
    }

    /// <summary>
    /// Writes connection pool setup instructions.
    /// </summary>
    public static void ConnectionSetupInstructions(string? environment = null)
    {
        var env = environment ?? "Dev";
        Console.WriteLine();
        Console.WriteLine("Configure using .NET User Secrets:");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  cd src/Console/PPDS.Dataverse.Demo");
        Console.WriteLine($"  dotnet user-secrets set \"Dataverse:Environments:{env}:Url\" \"https://YOUR-ORG.crm.dynamics.com\"");
        Console.WriteLine($"  dotnet user-secrets set \"Dataverse:Environments:{env}:Connections:0:ClientId\" \"your-client-id\"");
        Console.WriteLine($"  dotnet user-secrets set \"Dataverse:Environments:{env}:Connections:0:ClientSecret\" \"your-client-secret\"");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md for details.");
        Console.WriteLine();
    }
}
