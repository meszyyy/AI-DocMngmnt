using Microsoft.Extensions.AI;

namespace AiDocMngmnt.Worker;

// The shape we ask the model to fill in. Microsoft.Extensions.AI turns this
// into a JSON schema and validates the model's response against it.
public record DocumentAnalysis(string Summary, string[] Tags);

public class DocumentAnalyzer(IChatClient chatClient)
{
    // GitHub Models allows ~8K input tokens per request; keep a safe margin.
    private const int MaxInputChars = 16_000;

    public async Task<DocumentAnalysis> AnalyzeAsync(string fileName, string text, CancellationToken ct)
    {
        if (text.Length > MaxInputChars)
        {
            text = text[..MaxInputChars];
        }

        var prompt = $"""
            Analyze the document below and produce:
            - summary: 2-3 sentences, written in the same language as the document
            - tags: 3-7 short, lowercase keywords in the document's language

            File name: {fileName}
            Document text (may be truncated):
            ---
            {text}
            """;

        // Typed structured output: the response is parsed into DocumentAnalysis.
        var response = await chatClient.GetResponseAsync<DocumentAnalysis>(prompt, cancellationToken: ct);
        return response.Result;
    }
}
