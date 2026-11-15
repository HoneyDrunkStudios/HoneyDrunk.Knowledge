using HoneyDrunk.Knowledge.Abstractions;
using System.Globalization;
using System.Text;

namespace HoneyDrunk.Knowledge;

/// <summary>
/// Splits plain text or Markdown into passages of at most <see cref="KnowledgeLimits.MaxChunkTextLength"/>
/// characters. The result depends on the text alone.
/// </summary>
internal static class TextChunker
{
    private const string ParagraphSeparator = "\n\n";

    // The parameter name the caller sees; the public ingest method names its text "document".
    private static string DocumentParameter => "document";

    /// <summary>Cleans <paramref name="text"/> and packs its paragraphs into passages.</summary>
    /// <param name="text">The document text.</param>
    /// <returns>One to 1,024 passages, in order.</returns>
    /// <exception cref="ArgumentException">
    /// Nothing is left after cleaning, or the text needs more than <see cref="KnowledgeLimits.MaxChunksPerVersion"/> passages.
    /// The message never holds any of the text.
    /// </exception>
    internal static IReadOnlyList<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text, DocumentParameter);

        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var paragraph in Paragraphs(Clean(text)))
        {
            if (paragraph.Length > KnowledgeLimits.MaxChunkTextLength)
            {
                Flush(chunks, current);
                var pieces = Cut(paragraph);
                for (var i = 0; i < pieces.Count - 1; i++)
                {
                    Add(chunks, pieces[i]);
                }

                current.Append(pieces[^1]);
            }
            else if (current.Length == 0)
            {
                current.Append(paragraph);
            }
            else if (current.Length + ParagraphSeparator.Length + paragraph.Length <= KnowledgeLimits.MaxChunkTextLength)
            {
                current.Append(ParagraphSeparator).Append(paragraph);
            }
            else
            {
                Flush(chunks, current);
                current.Append(paragraph);
            }
        }

        Flush(chunks, current);

        if (chunks.Count == 0)
        {
            throw new ArgumentException("The document has no text after control and format characters are removed.", DocumentParameter);
        }

        return chunks;
    }

    private static void Flush(List<string> chunks, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        Add(chunks, current.ToString());
        current.Clear();
    }

    private static void Add(List<string> chunks, string chunk)
    {
        if (chunks.Count >= KnowledgeLimits.MaxChunksPerVersion)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The document needs more than {KnowledgeLimits.MaxChunksPerVersion} chunks, which is the limit."),
                DocumentParameter);
        }

        chunks.Add(chunk);
    }

    private static string Clean(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                builder.Append('\n');
            }
            else if (c == '\t')
            {
                builder.Append(' ');
            }
            else if (c == '\n')
            {
                builder.Append(c);
            }
            else
            {
                var category = char.GetUnicodeCategory(c);
                if (category != UnicodeCategory.Control && category != UnicodeCategory.Format)
                {
                    builder.Append(c);
                }
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<string> Paragraphs(string cleaned)
    {
        var lines = new List<string>();
        foreach (var raw in cleaned.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length > 0)
            {
                lines.Add(line);
            }
            else if (lines.Count > 0)
            {
                yield return string.Join('\n', lines);
                lines.Clear();
            }
        }

        if (lines.Count > 0)
        {
            yield return string.Join('\n', lines);
        }
    }

    private static List<string> Cut(string paragraph)
    {
        const int max = KnowledgeLimits.MaxChunkTextLength;
        var pieces = new List<string>();
        var rest = paragraph;

        while (rest.Length > max)
        {
            var at = LastWhiteSpaceCut(rest);
            if (at > 0)
            {
                pieces.Add(rest[..at].TrimEnd());
                rest = rest[at..].TrimStart();
            }
            else
            {
                var length = char.IsHighSurrogate(rest[max - 1]) ? max - 1 : max;
                pieces.Add(rest[..length]);
                rest = rest[length..];
            }
        }

        pieces.Add(rest);
        return pieces;
    }

    // The last white space at index 1 to 1,200 that leaves some text before it; 0 when there is none.
    private static int LastWhiteSpaceCut(string rest)
    {
        for (var i = KnowledgeLimits.MaxChunkTextLength; i >= 1; i--)
        {
            if (char.IsWhiteSpace(rest[i]) && rest.AsSpan(0, i).TrimEnd().Length > 0)
            {
                return i;
            }
        }

        return 0;
    }
}
