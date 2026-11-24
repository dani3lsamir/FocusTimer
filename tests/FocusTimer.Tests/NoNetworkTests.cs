using System.Text.RegularExpressions;

namespace FocusTimer.Tests;

/// <summary>Garantía de privacidad: el código de la app no puede contener APIs de red ni paquetes NuGet.</summary>
public class NoNetworkTests
{
    private static readonly Regex Forbidden = new(
        @"System\.Net\b|HttpClient|WebClient|WebRequest|Socket|TcpClient|UdpClient|TcpListener|\bDns\.|WebSocket|NetworkInformation",
        RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FocusTimer.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró FocusTimer.slnx");
    }

    private static IEnumerable<string> SourceFiles(params string[] patterns)
    {
        var src = Path.Combine(RepoRoot(), "src");
        return patterns.SelectMany(p => Directory.EnumerateFiles(src, p, SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    [Fact]
    public void Source_contains_no_network_apis()
    {
        var files = SourceFiles("*.cs").ToList();
        Assert.True(files.Count > 5, "No se encontraron archivos fuente");
        var hits = files
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(x => Forbidden.IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(RepoRoot(), x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.True(hits.Count == 0, "APIs de red encontradas:\n" + string.Join("\n", hits));
    }

    [Fact]
    public void App_projects_have_no_package_references()
    {
        var hits = SourceFiles("*.csproj", "*.props", "*.targets")
            .Where(f => File.ReadAllText(f).Contains("PackageReference", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(hits);
    }

    [Theory]
    [InlineData("using System.Net.Http;")]
    [InlineData("var c = new HttpClient();")]
    [InlineData("new System.Net.Sockets.Socket(a, b, c)")]
    [InlineData("WebRequest.Create(u)")]
    [InlineData("new WebClient()")]
    public void Detector_catches_known_patterns(string code) => Assert.Matches(Forbidden, code);
}
