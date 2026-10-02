using TypedDocumentAI.Http;

namespace TypedDocumentAI.Mistral;

/// <summary>Configuration for the Mistral OCR REST adapter.</summary>
public sealed class MistralOptions : HttpProviderOptions
{
    /// <summary>Creates defaults for the public Mistral v1 API. An API key is still required.</summary>
    public MistralOptions()
    {
        BaseAddress = new Uri("https://api.mistral.ai/v1/");
        Model = "mistral-ocr-latest";
    }
}

/// <summary>Mistral-specific per-call settings. These do not appear in the provider-neutral core.</summary>
public sealed record MistralRequestOptions : ProviderRequestOptions
{
    /// <summary>Optional, distinct zero-based page indices. Actual model page limits remain upstream constraints.</summary>
    public IReadOnlyList<int>? Pages { get; init; }
    /// <summary>Separates page headers in ReadAsync. Not accepted for typed extraction.</summary>
    public bool ExtractHeaders { get; init; }
    /// <summary>Separates page footers in ReadAsync. Not accepted for typed extraction.</summary>
    public bool ExtractFooters { get; init; }
}
