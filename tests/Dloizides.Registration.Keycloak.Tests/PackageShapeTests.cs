using Shouldly;

namespace Dloizides.Registration.Keycloak.Tests;

public sealed class PackageShapeTests
{
    [DenylistFact]
    public void PackageFiles_WhenScanned_NameNoDeniedWord()
    {
        var root = PackageRoot();
        var names = PackageDenylist.LoadForThisRun();

        var hits = PackageDenylist.FilesNaming(root, names);

        PackageDenylist.ScannableFiles(root).Count.ShouldBeGreaterThan(8);
        hits.ShouldBeEmpty();
    }

    [Fact]
    public void FilesNaming_WithPlantedNameInsideIdentifierOrFilename_FindsBoth()
    {
        var root = Directory.CreateTempSubdirectory("denylist-probe").FullName;
        File.WriteAllText(Path.Combine(root, "One.cs"), "class WidgetqScreen { }");
        File.WriteAllText(Path.Combine(root, "WidgetqTwo.cs"), "class Clean { }");

        var hits = PackageDenylist.FilesNaming(root, ["widgetq"]);

        hits.Count.ShouldBe(2);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void SkipReason_WhenNoListAndNotRequired_Skips()
    {
        var empty = Directory.CreateTempSubdirectory("denylist-skip").FullName;

        var reason = PackageDenylist.SkipReason(_ => null, empty);

        reason.ShouldNotBeNull();
        Directory.Delete(empty);
    }

    [Fact]
    public void SkipReason_WhenNoListAndRequired_RunsSoItFails()
    {
        var empty = Directory.CreateTempSubdirectory("denylist-required").FullName;

        var reason = PackageDenylist.SkipReason(
            variable => string.Equals(variable, PackageDenylist.RequiredVariable, StringComparison.Ordinal) ? "1" : null,
            empty);

        reason.ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => PackageDenylist.Load(_ => null, empty));
        Directory.Delete(empty);
    }

    [Fact]
    public void PublishScript_WhenRead_RunsDenylistTestAsRequired()
    {
        var script = File.ReadAllText(Path.Combine(PackageRoot(), "publish.ps1"));

        var requires = script.Contains($"$env:{PackageDenylist.RequiredVariable} = '1'", StringComparison.Ordinal);
        var runs = script.Contains(nameof(PackageFiles_WhenScanned_NameNoDeniedWord), StringComparison.Ordinal);

        requires.ShouldBeTrue();
        runs.ShouldBeTrue();
        script.ShouldContain("Passed:");
    }

    [Fact]
    public void CiWorkflow_WhenRead_FailsOnZeroTestsAndReadsOnly()
    {
        var workflow = File.ReadAllText(Path.Combine(PackageRoot(), ".github", "workflows", "ci.yml"));

        var failsOnZero = workflow.Contains("grep -Eq 'Passed:", StringComparison.Ordinal);
        var readsOnly = workflow.Contains("contents: read", StringComparison.Ordinal);

        failsOnZero.ShouldBeTrue();
        readsOnly.ShouldBeTrue();
    }

    private static string PackageRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dloizides.Registration.Keycloak.slnx")))
            directory = directory.Parent;
        directory.ShouldNotBeNull();
        return directory.FullName;
    }
}
