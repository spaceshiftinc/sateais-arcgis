using System.Text.Json;

namespace SateAIs.Core.Credentials;

/// <summary>
/// Where the API key lives. The credential store entry is the one the Python toolbox writes,
/// so signing in once serves both. Resolution order matches the other SateAIs clients.
/// </summary>
public static class ApiKeys
{
    public const string Service = "SateAIs";
    public const string Account = "api_key";
    public const string EnvironmentVariable = "SATEAIS_API_KEY";

    public static string CredentialsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sateais", "credentials");

    /// <summary>Credential store, then the environment variable, then the file shared with the CLI.</summary>
    public static string? Resolve() => Stored() ?? FromEnvironment() ?? ReadCredentialsFile(CredentialsFile);

    public static string? Stored() => OperatingSystem.IsWindows() ? WindowsCredentials.Read(Service, Account) : null;

    public static void Store(string apiKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The credential store is only available on Windows.");
        }
        WindowsCredentials.Write(Service, Account, apiKey);
    }

    public static void Clear()
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsCredentials.Delete(Service, Account);
        }
    }

    /// <summary>Where the key in use came from, for the settings screen.</summary>
    public static string DescribeSource()
    {
        if (Stored() is not null)
        {
            return "the operating system credential store";
        }
        if (FromEnvironment() is not null)
        {
            return $"the {EnvironmentVariable} environment variable";
        }
        if (ReadCredentialsFile(CredentialsFile) is not null)
        {
            return $"{CredentialsFile} (shared with the sateais CLI)";
        }
        return "nowhere - no key is configured";
    }

    /// <summary>A key for display, e.g. <c>sk_live_****abcd</c>.</summary>
    public static string Mask(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return "(not set)";
        }
        return apiKey.Length <= 12 ? "***" : $"{apiKey[..8]}****{apiKey[^4..]}";
    }

    internal static string? FromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>The CLI's JSON file, or a legacy file that is just the key.</summary>
    internal static string? ReadCredentialsFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        string text;
        try
        {
            text = File.ReadAllText(path).Trim();
        }
        catch (IOException)
        {
            return null;
        }
        if (text.Length == 0)
        {
            return null;
        }
        if (!text.StartsWith('{'))
        {
            return text;
        }
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("api_key", out var key)
                && key.ValueKind == JsonValueKind.String
                && key.GetString() is { Length: > 0 } value
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
