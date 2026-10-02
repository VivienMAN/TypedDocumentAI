namespace TypedDocumentAI;

/// <summary>Routing configuration. No provider keys or provider-specific model settings belong here.</summary>
public sealed class DocumentAIOptions
{
    /// <summary>Default routing name. Optional when exactly one provider is registered.</summary>
    public string? DefaultProviderName { get; set; }
}
