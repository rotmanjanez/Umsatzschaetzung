namespace Umsatzschaetzung.Tests.Reports;

// The page is written from the code. A change to the model fails here until the page follows:
// UMSATZ_WRITE_DOCS=1 dotnet test --filter TemplateDocTests
public class TemplateDocTests
{
    static readonly string Page = Path.Combine(TestData.Repo, "web/docs/pages/vorlagen.md");
    static readonly string SchemaFile = Path.Combine(TestData.Repo, "web/docs/pages/vorlagen-schema.json");
    static bool Write => Environment.GetEnvironmentVariable("UMSATZ_WRITE_DOCS") == "1";

    [Fact]
    public void PageDescribesTheModel()
    {
        var page = File.ReadAllText(Page).ReplaceLineEndings("\n");
        var from = page.IndexOf(TemplateDoc.Begin, StringComparison.Ordinal);
        var to = page.IndexOf(TemplateDoc.End, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "Marken fehlen in vorlagen.md");
        var expected = page[..(from + TemplateDoc.Begin.Length)] + "\n\n" + TemplateDoc.Model() + "\n" + page[to..];
        if (Write) File.WriteAllText(Page, expected);
        Assert.Equal(expected, page);
    }

    [Fact]
    public void SchemaDescribesTheModel()
    {
        var expected = TemplateDoc.Schema();
        if (Write) File.WriteAllText(SchemaFile, expected);
        Assert.Equal(expected, File.Exists(SchemaFile) ? File.ReadAllText(SchemaFile).ReplaceLineEndings("\n") : "");
    }
}
