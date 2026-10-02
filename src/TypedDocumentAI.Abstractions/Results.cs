using System.Text.Json;

namespace TypedDocumentAI;

/// <summary>A validated typed extraction and the metadata actually reported by its provider.</summary>
public sealed record ExtractionResult<T>(T Value, ProcessingMetadata Metadata) where T : class;

/// <summary>Provider output before the common schema validation step. The element must own its lifetime.</summary>
public sealed record JsonExtractionResult(JsonElement Data, ProcessingMetadata Metadata);

/// <summary>Recognized or transcribed text. Empty Pages means the adapter does not supply page-level output.</summary>
public sealed record OcrResult(string Text, IReadOnlyList<OcrPage> Pages, ProcessingMetadata Metadata);

/// <summary>Provider-reported OCR text for one page. Index is the provider's zero-based page index.</summary>
public sealed record OcrPage(int Index, string Text, string? Header = null, string? Footer = null);

/// <summary>Optional measurements. Null means unavailable, never an estimated zero.</summary>
public sealed record ProcessingMetadata(
    string ProviderName,
    string Model,
    string? RequestId = null,
    TimeSpan? Duration = null,
    int? PagesProcessed = null,
    long? InputTokens = null,
    long? OutputTokens = null);
