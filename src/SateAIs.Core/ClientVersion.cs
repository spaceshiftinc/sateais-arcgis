using System.Reflection;

namespace SateAIs.Core;

/// <summary>The version this client reports to the API. Comes from Directory.Build.props.</summary>
public static class ClientVersion
{
    public static readonly string Current = Read();

    private static string Read()
    {
        var assembly = typeof(ClientVersion).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        // Build metadata ("+sha") is not part of the version the API wants.
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
