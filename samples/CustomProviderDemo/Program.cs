using System.Text;
using Microsoft.Extensions.DependencyInjection;
using TypedDocumentAI;

// This offline sample proves registration of a new provider without modifying the core.
// It decodes plain text only; it is NOT an OCR engine for images or PDFs.
var services = new ServiceCollection();
services.AddDocumentAI().AddProvider<PlainTextProvider>();
await using var container = services.BuildServiceProvider();
var client = container.GetRequiredService<IDocumentClient>();
var document = DocumentInput.FromBytes(Encoding.UTF8.GetBytes("Local provider registration works."), "demo.txt", "text/plain");
var result = await client.ReadAsync(document);
Console.WriteLine($"{result.Metadata.ProviderName}: {result.Text}");

public sealed class PlainTextProvider : IOcrProvider
{
    public string Name => "plaintext";
    public DocumentCapabilities Capabilities => DocumentCapabilities.None;

    public Task<OcrResult> ReadAsync(DocumentInput document, DocumentRequestOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        DocumentValidation.Request(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (document.ContentType != "text/plain")
        {
            throw new NotSupportedException("The sample adapter accepts plain text only, not scanned documents.");
        }
        if (options.ProviderOptions is not null || !string.IsNullOrWhiteSpace(options.Instructions))
        {
            throw new NotSupportedException("The sample adapter does not accept provider options or instructions.");
        }
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(document.Content.Span);
        }
        catch (DecoderFallbackException)
        {
            throw new DocumentResponseException("The plain-text document is not valid UTF-8.");
        }
        return Task.FromResult(new OcrResult(text, Array.Empty<OcrPage>(), new ProcessingMetadata(Name, "utf8-decoder")));
    }
}
