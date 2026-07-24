using UglyToad.PdfPig;

namespace AiDocMngmnt.Worker;

public static class TextExtractor
{
    /// <summary>
    /// Extracts plain text from a downloaded document, or returns null when
    /// the content type is not supported (yet).
    /// </summary>
    public static async Task<string?> ExtractAsync(Stream content, string contentType, CancellationToken ct)
    {
        if (contentType.StartsWith("text/") || contentType is "application/json" or "application/xml")
        {
            using var reader = new StreamReader(content);
            return await reader.ReadToEndAsync(ct);
        }

        if (contentType == "application/pdf")
        {
            // PdfPig needs a seekable stream; buffer the blob into memory first.
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            buffer.Position = 0;

            using var pdf = PdfDocument.Open(buffer);
            var pages = pdf.GetPages().Select(p => p.Text);
            return string.Join("\n\n", pages);
        }

        // Images (OCR via vision models) and Office formats are future work.
        return null;
    }
}
