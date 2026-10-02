namespace TypedDocumentAI;

/// <summary>Common per-call settings. Values are not persisted or logged by the library.</summary>
public record DocumentRequestOptions
{
    /// <summary>Overrides the configured default provider for this call.</summary>
    public string? ProviderName { get; init; }
    /// <summary>Additional extraction instructions. Not every OCR engine supports this; unsupported values are rejected.</summary>
    public string? Instructions { get; init; }
    /// <summary>Strongly typed adapter-specific settings. Passing another adapter's settings is an error.</summary>
    public ProviderRequestOptions? ProviderOptions { get; init; }
}

/// <summary>Per-call settings for structured extraction.</summary>
public sealed record ExtractionOptions : DocumentRequestOptions
{
    /// <summary>The schema identifier, restricted to 1-64 ASCII letters, digits, underscores or hyphens.</summary>
    public string SchemaName { get; init; } = "document";
}

/// <summary>Base class for typed adapter settings; third-party packages can introduce their own derived records.</summary>
public abstract record ProviderRequestOptions
{
    /// <summary>Optionally overrides the provider's configured model.</summary>
    public string? Model { get; init; }
}
