using System.IO;
using Xunit;

namespace VMDesk.Application.Tests;

/// <summary>
/// WPF rejects DynamicResource on Style.BasedOn at runtime with
/// "A 'DynamicResourceExtension' cannot be set on the 'BasedOn' property".
/// The failure surfaces only when the window is constructed, so a source
/// scan guards every XAML file in the app.
/// </summary>
public class XamlResourceContractTests
{
    private static string AppXamlDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VMDesk.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "VMDesk.App");
    }

    [Fact]
    public void No_style_uses_dynamic_resource_for_based_on()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(AppXamlDirectory(), "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (lines[i].Contains("BasedOn=\"{DynamicResource"))
                    offenders.Add($"{file}:{i + 1}: {lines[i].Trim()}");
        }

        Assert.True(offenders.Count == 0,
            "Style.BasedOn must use StaticResource (DynamicResource is illegal there). Found:\n" +
            string.Join("\n", offenders));
    }
}
