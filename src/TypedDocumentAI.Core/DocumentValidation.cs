namespace TypedDocumentAI;

/// <summary>Shared validation for provider authors. Invalid settings are rejected before paid API requests.</summary>
public static class DocumentValidation
{
    /// <summary>Validates a routing name or a JSON schema name.</summary>
    public static void Identifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
        {
            throw new ArgumentException("Identifiers must contain 1-64 ASCII letters, digits, underscores or hyphens.", parameterName);
        }
    }

    /// <summary>Validates common call options, including instruction length and the optional provider name.</summary>
    public static void Request(DocumentRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ProviderName is not null)
        {
            Identifier(options.ProviderName, nameof(options.ProviderName));
        }
        if (options.Instructions is { Length: > 16000 })
        {
            throw new ArgumentException("Extraction instructions exceed 16000 characters.", nameof(options));
        }
        if (options is ExtractionOptions extraction)
        {
            Identifier(extraction.SchemaName, nameof(extraction.SchemaName));
        }
    }
}
