using System.Text.Json;

namespace TypedDocumentAI;

/// <summary>The common entry point. Routing is explicit; there is no automatic cross-provider fallback.</summary>
public interface IDocumentClient
{
    /// <summary>Describes the providers registered in this client.</summary>
    IReadOnlyList<DocumentProviderInfo> Providers { get; }

    /// <summary>Extracts a typed object, validates the response shape and deserializes it.</summary>
    Task<ExtractionResult<T>> ExtractAsync<T>(DocumentInput document, ExtractionOptions? options = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>Reads document text. Page metadata is present only when actually supplied by the provider.</summary>
    Task<OcrResult> ReadAsync(DocumentInput document, DocumentRequestOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Minimal provider identity. A provider implements only the operation interfaces it supports.</summary>
public interface IDocumentProvider
{
    /// <summary>A stable, case-insensitive routing name such as "mistral".</summary>
    string Name { get; }
    /// <summary>Additional capabilities of this adapter, not of the upstream platform in general.</summary>
    DocumentCapabilities Capabilities { get; }
}

/// <summary>Optional contract for document text recognition or transcription.</summary>
public interface IOcrProvider : IDocumentProvider
{
    /// <summary>Reads document text without inventing unavailable positional metadata.</summary>
    Task<OcrResult> ReadAsync(DocumentInput document, DocumentRequestOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>Optional contract for native structured extraction. An OCR-only provider need not implement it.</summary>
public interface IStructuredDocumentProvider : IDocumentProvider
{
    /// <summary>Extracts JSON using a normalized schema. The caller validates returned JSON before using it.</summary>
    Task<JsonExtractionResult> ExtractJsonAsync(DocumentInput document, JsonElement schema,
        ExtractionOptions options, CancellationToken cancellationToken = default);
}

/// <summary>Creates matching schemas and typed results. Replace to support an additional serialization strategy.</summary>
public interface IDocumentSchema
{
    /// <summary>Builds a portable, strict object schema for a C# document type.</summary>
    JsonElement Create<T>() where T : class;
    /// <summary>Validates the JSON shape against the supplied schema and deserializes it.</summary>
    T Deserialize<T>(JsonElement data, JsonElement schema) where T : class;
}

/// <summary>Features that may differ between adapters. This is not a claim of identical model accuracy.</summary>
[Flags]
public enum DocumentCapabilities
{
    /// <summary>No additional capability is advertised.</summary>
    None = 0,
    /// <summary>The adapter returns page-indexed OCR output.</summary>
    PageText = 1,
    /// <summary>The adapter can select input pages server-side.</summary>
    PageSelection = 2,
    /// <summary>The adapter can return extracted page headers and footers.</summary>
    HeadersAndFooters = 4
}

/// <summary>A provider's implemented operation contracts and optional capabilities.</summary>
public sealed record DocumentProviderInfo(
    string Name, bool SupportsOcr, bool SupportsStructuredExtraction, DocumentCapabilities Capabilities);
