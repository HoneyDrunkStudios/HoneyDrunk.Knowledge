using Xunit;

namespace HoneyDrunk.Knowledge.Tests;

/// <summary>The chunking rules: paragraphs packed up to 1,200 characters, cleaned, bounded and deterministic.</summary>
public sealed class TextChunkerTests
{
    /// <summary>Short paragraphs pack into one chunk with blank lines between.</summary>
    [Fact]
    public void Short_paragraphs_pack_into_one_chunk_with_blank_lines_between()
    {
        var chunks = TextChunker.Split("First line\nsecond line\n\n\n\nSecond paragraph\n\nThird");

        Assert.Equal(new[] { "First line\nsecond line\n\nSecond paragraph\n\nThird" }, chunks);
    }

    /// <summary>A chunk never exceeds 1200 characters.</summary>
    [Fact]
    public void A_chunk_never_exceeds_1200_characters()
    {
        var text = string.Join("\n\n", Enumerable.Range(0, 60).Select(i => new string((char)('a' + (i % 26)), 500)))
            + "\n\n" + string.Join(' ', Enumerable.Repeat("word", 1500));

        var chunks = TextChunker.Split(text);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.InRange(c.Length, 1, 1200));
    }

    /// <summary>A long paragraph is cut at the last space before 1200.</summary>
    [Fact]
    public void A_long_paragraph_is_cut_at_the_last_space_before_1200()
    {
        var text = new string('a', 1190) + " " + new string('b', 50);

        var chunks = TextChunker.Split(text);

        Assert.Equal(new[] { new string('a', 1190), new string('b', 50) }, chunks);
    }

    /// <summary>A space at exactly 1200 is a valid cut.</summary>
    [Fact]
    public void A_space_at_exactly_1200_is_a_valid_cut()
    {
        var text = new string('a', 1200) + " " + new string('b', 10);

        var chunks = TextChunker.Split(text);

        Assert.Equal(new[] { new string('a', 1200), new string('b', 10) }, chunks);
    }

    /// <summary>A paragraph with no space is cut at 1200 outside a surrogate pair.</summary>
    [Fact]
    public void A_paragraph_with_no_space_is_cut_at_1200_outside_a_surrogate_pair()
    {
        var plain = TextChunker.Split(new string('x', 2500));
        Assert.Equal(new[] { 1200, 1200, 100 }, plain.Select(c => c.Length).ToArray());

        // Position 1199 holds the high half of a pair, so the cut moves before it.
        var pair = char.ConvertFromUtf32(0x1F600);
        var text = new string('x', 1199) + pair + new string('y', 20);

        var chunks = TextChunker.Split(text);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(new string('x', 1199), chunks[0]);
        Assert.Equal(pair + new string('y', 20), chunks[1]);
        Assert.All(chunks, c => Assert.False(char.IsLowSurrogate(c[0]) || char.IsHighSurrogate(c[^1])));
    }

    /// <summary>Control and format characters are removed and tabs become spaces.</summary>
    [Fact]
    public void Control_and_format_characters_are_removed_and_tabs_become_spaces()
    {
        var chunks = TextChunker.Split("a\u0000b\u202Ec\u200Bd\te\r\nf\rg  \n\nh\u0007");

        Assert.Equal(new[] { "abcd e\nf\ng\n\nh" }, chunks);
    }

    /// <summary>The same text always gives the same chunks.</summary>
    [Fact]
    public void The_same_text_always_gives_the_same_chunks()
    {
        var text = string.Join("\n\n", Enumerable.Range(0, 40).Select(i => string.Join(' ', Enumerable.Repeat("w" + i, 200))));

        Assert.Equal(TextChunker.Split(text), TextChunker.Split(text));
    }

    /// <summary>Text that cleans to nothing is refused.</summary>
    /// <param name="text">The text to split.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   \n\n\t\r\n  ")]
    [InlineData("\u0000\u200B\u202E")]
    public void Text_that_cleans_to_nothing_is_refused(string text)
    {
        var error = Assert.Throws<ArgumentException>(() => TextChunker.Split(text));

        Assert.Equal("document", error.ParamName);
    }

    /// <summary>More than 1024 chunks is refused naming the limit not the text.</summary>
    [Fact]
    public void More_than_1024_chunks_is_refused_naming_the_limit_not_the_text()
    {
        var text = string.Join("\n\n", Enumerable.Range(0, 1025).Select(_ => "SENTINEL " + new string('z', 700)));

        var error = Assert.Throws<ArgumentException>(() => TextChunker.Split(text));

        Assert.Equal("document", error.ParamName);
        Assert.Contains("1024", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", error.Message, StringComparison.Ordinal);

        var exact = string.Join("\n\n", Enumerable.Range(0, 1024).Select(_ => new string('z', 700)));
        Assert.Equal(1024, TextChunker.Split(exact).Count);
    }

    /// <summary>Markdown headings and lists are kept as text.</summary>
    [Fact]
    public void Markdown_headings_and_lists_are_kept_as_text()
    {
        const string text = "# Title\n\n- one\n- two\n  - nested\n\n```\ncode\n```";

        var chunks = TextChunker.Split(text);

        Assert.Equal(new[] { text }, chunks);
    }
}
