using Rowles.LeanLucene.Cli;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Index;

[Trait("Category", "Index")]
[Trait("Category", "Validation")]
public sealed class IndexCheckerCliTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexCheckerCliTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "IndexCheckerCli: Check Valid Index Returns Zero")]
    public void IndexCheckerCli_CheckValidIndex_ReturnsZero()
    {
        var path = CreateIndex("cli_valid");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["check", path], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Healthy", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Check Corrupt Index Returns One")]
    public void IndexCheckerCli_CheckCorruptIndex_ReturnsOne()
    {
        var path = CreateIndex("cli_corrupt");
        File.Delete(Directory.GetFiles(path, "*.dic").Single());
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["check", path], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains(IndexCheckIssueCodes.RequiredFileMissing, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Json Writes Stable Shape")]
    public void IndexCheckerCli_CheckJson_WritesStableJson()
    {
        var path = CreateIndex("cli_json");
        File.Delete(Directory.GetFiles(path, "*.dic").Single());
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["check", path, "--json"], output, error);

        Assert.Equal(1, exitCode);
        var json = output.ToString();
        Assert.Contains("\"isHealthy\":false", json);
        Assert.Contains("\"issues\"", json);
        Assert.Contains(IndexCheckIssueCodes.RequiredFileMissing, json);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Invalid Arguments Return Two")]
    public void IndexCheckerCli_InvalidArguments_ReturnsTwo()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["check", "--unknown"], output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("Unknown option", error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Output Writes Report File")]
    public void IndexCheckerCli_Output_WritesReportFile()
    {
        var path = CreateIndex("cli_output");
        var outputPath = Path.Combine(_fixture.Path, "cli-output-report.txt");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["check", path, "--output", outputPath, "--summary-only"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Wrote check result", output.ToString());
        Assert.Contains("Healthy", File.ReadAllText(outputPath));
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Interactive Command Requires Terminal When Redirected")]
    public void IndexCheckerCli_Interactive_WhenRedirected_ReturnsTwo()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["interactive"], output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("Interactive mode requires a terminal", error.ToString());
    }

    private string CreateIndex(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        var dir = new MMapDirectory(path);
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello world"));
        writer.AddDocument(doc);
        writer.Commit();
        return path;
    }
}
