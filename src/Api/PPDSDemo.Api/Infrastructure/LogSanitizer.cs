namespace PPDSDemo.Api.Infrastructure;

/// <summary>
/// Provides sanitization for values before logging to prevent log injection attacks.
/// </summary>
public static class LogSanitizer
{
    private const int DefaultMaxLength = 200;
    private const int ShortMaxLength = 50;

    /// <summary>
    /// Sanitizes a string value for safe logging.
    /// </summary>
    /// <param name="value">The value to sanitize.</param>
    /// <param name="maxLength">Maximum length before truncation (default: 200).</param>
    /// <returns>A sanitized string safe for logging.</returns>
    /// <remarks>
    /// This method:
    /// - Escapes newlines and carriage returns to prevent log injection
    /// - Truncates long values to prevent log flooding
    /// - Returns "[null]" for null values for clarity
    /// - Returns "[empty]" for empty/whitespace values
    /// </remarks>
    public static string Sanitize(string? value, int maxLength = DefaultMaxLength)
    {
        if (value is null)
            return "[null]";

        if (string.IsNullOrWhiteSpace(value))
            return "[empty]";

        // Escape newlines and carriage returns to prevent log injection
        var sanitized = value
            .Replace("\r\n", "\\r\\n")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");

        // Truncate if too long to prevent log flooding
        if (sanitized.Length > maxLength)
        {
            return string.Concat(sanitized.AsSpan(0, maxLength), "...[truncated]");
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes a string with a shorter maximum length (50 chars).
    /// Use for values like action names, filter parameters, etc.
    /// </summary>
    public static string SanitizeShort(string? value) => Sanitize(value, ShortMaxLength);

    /// <summary>
    /// Returns a safe representation for logging without exposing the full value.
    /// Use for potentially large payloads like request bodies.
    /// </summary>
    /// <param name="value">The value to describe.</param>
    /// <returns>A description of the value suitable for logging.</returns>
    public static string DescribePayload(string? value)
    {
        if (value is null)
            return "[null payload]";

        if (string.IsNullOrWhiteSpace(value))
            return "[empty payload]";

        return $"[payload: {value.Length} chars]";
    }
}
