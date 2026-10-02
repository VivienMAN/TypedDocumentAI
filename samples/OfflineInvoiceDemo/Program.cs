using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TypedDocumentAI;

// This provider returns a fixed synthetic fixture. It performs no OCR or AI inference.
// The real client still creates the schema, validates the JSON and deserializes Invoice.
var services = new ServiceCollection();
services.AddDocumentAI().AddProvider<SyntheticInvoiceProvider>();
await using var container = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
var client = container.GetRequiredService<IDocumentClient>();
var document = DocumentInput.FromBytes(
    Encoding.UTF8.GetBytes("Invoice INV-001 | 2026-10-01 | Total 125.50"),
    "synthetic-invoice.txt", "text/plain");
var result = await client.ExtractAsync<Invoice>(document);
if (result.Value.Number != "INV-001" || result.Value.Total != 125.50m ||
    result.Value.Date != new DateOnly(2026, 10, 1))
{
    throw new InvalidOperationException("The synthetic fixture did not produce the expected typed invoice.");
}
Console.WriteLine("OFFLINE DEMO: fixed synthetic response, no OCR, network or paid API call.");
Console.WriteLine(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true }));

public sealed class Invoice
{
    [Description("Invoice number exactly as printed, or null when absent")]
    public string? Number { get; init; }
    [Description("Printed final invoice total, or null when absent")]
    public decimal? Total { get; init; }
    [Description("Invoice date in YYYY-MM-DD format, or null when absent")]
    public DateOnly? Date { get; init; }
}

public sealed class SyntheticInvoiceProvider : IStructuredDocumentProvider
{
    public string Name => "synthetic";
    public DocumentCapabilities Capabilities => DocumentCapabilities.None;

    public Task<JsonExtractionResult> ExtractJsonAsync(DocumentInput document, JsonElement schema,
        ExtractionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        DocumentValidation.Request(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (document.ContentType != "text/plain" || options.ProviderOptions is not null ||
            !string.IsNullOrWhiteSpace(options.Instructions))
        {
            throw new NotSupportedException("This demo accepts only synthetic plain text without provider settings or instructions.");
        }
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw new DocumentSchemaException("This demo expects an object schema.");
        }
        using var response = JsonDocument.Parse("""{"Number":"INV-001","Total":125.50,"Date":"2026-10-01"}""");
        return Task.FromResult(new JsonExtractionResult(response.RootElement.Clone(),
            new ProcessingMetadata(Name, "fixed-fixture")));
    }
}
