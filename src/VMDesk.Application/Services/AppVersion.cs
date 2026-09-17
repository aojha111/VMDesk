namespace VMDesk.Application.Services;

/// <summary>Central app version, resolved from the entry assembly (spec §52).</summary>
public static class AppVersion
{
    public static string Value { get; } =
        typeof(AppVersion).Assembly.GetCustomAttributes(false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0]
        ?? "1.0.0";
}
