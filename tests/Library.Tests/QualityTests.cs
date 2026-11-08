using FluentAssertions;
using Library.Tools;
using Xunit;
namespace Library.Tests;
public sealed class QualityTests
{
    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "artifacts"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, relative));
    }
    [Fact]
    public void RefactoredMethodHasLowerComplexityAndHigherMi()
    {
        var before = CodeQuality.Measure(Source("before/LegacyUploadController.cs"), "LegacyUploadController").Methods[0];
        var after = CodeQuality.Measure(Source("src/Library.Api/UploadController.cs"), "UploadController").Methods[0];
        after.Complexity.Should().BeLessThan(before.Complexity);
        after.Mi.Should().BeGreaterThan(before.Mi);
        string shared = "class C(int value) { public int F() => value; public int G() => value; }";
        CodeQuality.Measure(shared, "C").Lcom1.Should().Be(0);
    }
    [Fact]
    public void DocumentedControllerPassesProjectAudit() =>
        CodeQuality.Audit(Source("src/Library.Api/UploadController.cs")).Should().BeEmpty();
    [Fact]
    public void LegacyHasPathAndRoleFindings()
    {
        var findings = CodeQuality.Audit(Source("before/LegacyUploadController.cs"));
        findings.Select(x => x.Rule).Should().Contain(new[] { "AUD-04", "AUD-05" });
    }
    [Fact]
    public void IfAndLogicalAndGiveComplexityThree()
    {
        string source = "class C { public int F(bool a,bool b) { if(a && b) return 1; return 0; } }";
        CodeQuality.Measure(source, "C").Methods[0].Complexity.Should().Be(3);
    }
}
