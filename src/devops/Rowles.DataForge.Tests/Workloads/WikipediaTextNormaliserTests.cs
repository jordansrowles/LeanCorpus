using Rowles.DataForge.Workloads;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class WikipediaTextNormaliserTests
{
    [Fact]
    public void Normalises_newlines_comments_and_reference_elements()
    {
        var input = "== Heading ==\r\nFirst <!-- hidden --> second <REF name=\"note\">citation</REF> third <ref name=\"x\" />";

        Assert.Equal("Heading\nFirst second third", WikipediaTextNormaliserV1.Normalise(input));
    }

    [Fact]
    public void Removes_nested_templates_and_tables_with_bounded_delimiters()
    {
        var input = "Before {{outer|{{inner|text}}}} after\n{| class=\"wikitable\"\n| row\n{| nested |}\n|}\nEnd";

        Assert.Equal("Before after\n\nEnd", WikipediaTextNormaliserV1.Normalise(input));
    }

    [Fact]
    public void Converts_internal_and_external_links_and_emphasis()
    {
        var input = "[[Page]] [[Page|display]] [[File:photo.jpg]] [[Category:Hidden|shown]] [https://example.test label] [https://example.test] '''bold''' ''italic''";

        Assert.Equal("Page display shown label bold italic", WikipediaTextNormaliserV1.Normalise(input));
    }

    [Fact]
    public void Strips_html_syntax_then_decodes_entities_and_preserves_unicode()
    {
        var input = "<p>Zażółć 東京 مرحبا &amp; &#x1F30D;</p><li>second</li>";

        Assert.Equal("Zażółć 東京 مرحبا & 🌍\n\nsecond", WikipediaTextNormaliserV1.Normalise(input));
    }

    [Fact]
    public void Collapses_horizontal_space_and_three_or_more_newlines()
    {
        var input = "\talpha  beta\r\n\r\n\r\ngamma\u00a0delta\u0001epsilon";

        Assert.Equal("alpha beta\n\ngamma delta epsilon", WikipediaTextNormaliserV1.Normalise(input));
    }

    [Theory]
    [InlineData("<!-- unclosed", "Unclosed Wikipedia comment block.")]
    [InlineData("{{ unclosed", "Unclosed Wikipedia template block.")]
    [InlineData("{| unclosed", "Unclosed Wikipedia table block.")]
    public void Fails_on_unclosed_bounded_markup(string input, string message)
    {
        var exception = Assert.Throws<InvalidDataException>(() => WikipediaTextNormaliserV1.Normalise(input));
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void Fails_on_nested_references_and_template_depth_overflow()
    {
        Assert.Throws<InvalidDataException>(() => WikipediaTextNormaliserV1.Normalise("<ref>a<ref>b</ref></ref>"));
        var tooDeep = string.Concat(Enumerable.Repeat("{{", 65)) + string.Concat(Enumerable.Repeat("}}", 65));
        Assert.Throws<InvalidDataException>(() => WikipediaTextNormaliserV1.Normalise(tooDeep));
    }

    [Fact]
    public void Normalisation_is_deterministic_and_idempotent_for_normalised_visible_text()
    {
        var samples = new[]
        {
            "Plain text with spaces",
            "== A heading ==\nA paragraph with ''markup'' and &amp; entity",
            "第一段落\n\nمرحبا بالعالم",
            "<p>one</p><p>two</p>"
        };

        foreach (var sample in samples)
        {
            var normalised = WikipediaTextNormaliserV1.Normalise(sample);
            Assert.Equal(normalised, WikipediaTextNormaliserV1.Normalise(sample));
            Assert.Equal(normalised, WikipediaTextNormaliserV1.Normalise(normalised));
            Assert.DoesNotContain('\r', normalised);
            Assert.Equal(normalised, new System.Text.UTF8Encoding(false, true).GetString(System.Text.Encoding.UTF8.GetBytes(normalised)));
        }
    }
}
