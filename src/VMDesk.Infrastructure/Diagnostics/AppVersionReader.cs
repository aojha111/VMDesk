using System.Reflection;

namespace VMDesk.Infrastructure.Diagnostics;

/// <summary>
/// Reads the running application version. Kept in Infrastructure so diagnostics do not
/// depend on the Application layer (spec §56).
/// </summary>
internal static class AppVersionReader
{
    public static string Read()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersionReader).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "1.0.0";
    }
}