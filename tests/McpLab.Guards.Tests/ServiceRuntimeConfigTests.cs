using System.Xml.Linq;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class ServiceRuntimeConfigTests {
    private const string GcOptionName = "System.GC.HighMemoryPercent";
    private const string GcOptionValue = "20";
    private const int ExpectedServiceCount = 10;

    private static readonly string[] IgnoredDirectories = ["bin", "obj", ".local", "node_modules", ".git"];

    [Test]
    public void ServiceProjects_McpServerSet_HasExpectedCount() {
        Assert.That(FindServiceProjects(), Has.Count.EqualTo(ExpectedServiceCount));
    }

    [Test]
    public void ServiceProjects_McpServerSet_ExcludesSharedAndAdminProjects() {
        IEnumerable<string?> names = FindServiceProjects().Select(path => Path.GetFileName(path));

        using (Assert.EnterMultipleScope()) {
            Assert.That(names, Has.None.EqualTo("McpLab.Shared.csproj"));
            Assert.That(names, Has.None.EqualTo("McpLab.Admin.csproj"));
        }
    }

    [TestCaseSource(nameof(ServiceProjectCases))]
    public void ServiceProject_RuntimeConfig_SetsHighMemoryPercent(string projectPath) {
        XDocument document = XDocument.Load(projectPath);

        XElement[] options = document.Descendants("RuntimeHostConfigurationOption")
            .Where(element => (string?)element.Attribute("Include") == GcOptionName)
            .ToArray();

        using (Assert.EnterMultipleScope()) {
            Assert.That(options, Has.Length.EqualTo(1), projectPath);
            Assert.That(options.Select(option => (string?)option.Attribute("Value")), Has.All.EqualTo(GcOptionValue), projectPath);
        }
    }

    private static IEnumerable<TestCaseData> ServiceProjectCases() {
        foreach (string projectPath in FindServiceProjects()) {
            yield return new TestCaseData(projectPath).SetArgDisplayNames(Path.GetFileName(projectPath));
        }
    }

    private static List<string> FindServiceProjects() {
        string root = FindRepositoryRoot();

        return Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsInIgnoredDirectory(root, path))
            .Where(IsMcpServerProject)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsMcpServerProject(string projectPath) {
        XDocument document = XDocument.Load(projectPath);

        return (string?)document.Root?.Attribute("Sdk") == "Microsoft.NET.Sdk.Web"
            && document.Descendants("PackageReference")
                .Any(element => (string?)element.Attribute("Include") == "ModelContextProtocol.AspNetCore");
    }

    private static bool IsInIgnoredDirectory(string root, string path) {
        string[] segments = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments[..^1].Any(segment => IgnoredDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null) {
            if (File.Exists(Path.Combine(directory.FullName, "McpLab.slnx"))) {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("McpLab.slnx was not found above the test output directory.");
    }
}
