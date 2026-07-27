using System.Text;

namespace AiDocMngmnt.Worker;

public static class TextChunker
{
    /// <summary>
    /// Splits text into overlapping chunks, preferring paragraph boundaries.
    /// Small chunks embed precisely; the overlap keeps context that would
    /// otherwise be cut in half at a boundary.
    /// </summary>
    public static List<string> Chunk(string text, int maxChars = 1500, int overlapChars = 200)
    {
        var paragraphs = text
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            // A single paragraph longer than maxChars is split hard.
            .SelectMany(p => p.Length <= maxChars ? [p] : SplitHard(p, maxChars));

        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (current.Length > 0 && current.Length + paragraph.Length + 2 > maxChars)
            {
                chunks.Add(current.ToString());

                // Carry the tail of the previous chunk over as overlap.
                var previous = current.ToString();
                current.Clear();
                current.Append(previous[Math.Max(0, previous.Length - overlapChars)..]);
                current.Append("\n\n");
            }

            if (current.Length > 0 && !current.ToString().EndsWith("\n\n"))
            {
                current.Append("\n\n");
            }

            current.Append(paragraph);
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString());
        }

        return chunks;
    }

    private static IEnumerable<string> SplitHard(string text, int maxChars)
    {
        for (var i = 0; i < text.Length; i += maxChars)
        {
            yield return text.Substring(i, Math.Min(maxChars, text.Length - i));
        }
    }
}
