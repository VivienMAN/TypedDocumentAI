using System.Diagnostics;

namespace TypedDocumentAI;

/// <summary>Routes to independent providers and owns type-to-schema conversion and response validation.</summary>
public sealed class DocumentClient : IDocumentClient
{
    /// <summary>The activity source name for content-free operation tracing.</summary>
    public const string ActivitySourceName = "TypedDocumentAI";
    private static readonly ActivitySource Activities = new(ActivitySourceName);
    private readonly Dictionary<string, IDocumentProvider> _providers;
    private readonly IDocumentSchema _schema;
    private readonly string? _defaultProviderName;

    /// <summary>Creates a client with explicit dependencies. Duplicate provider names are rejected.</summary>
    public DocumentClient(IEnumerable<IDocumentProvider> providers, IDocumentSchema schema, DocumentAIOptions options)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(options);
        _schema = schema;
        _providers = new Dictionary<string, IDocumentProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            DocumentValidation.Identifier(provider.Name, nameof(providers));
            if (!_providers.TryAdd(provider.Name, provider))
            {
                throw new InvalidOperationException("Provider names must be unique, ignoring case.");
            }
        }
        if (_providers.Count == 0)
        {
            throw new InvalidOperationException("Register at least one document provider.");
        }
        if (options.DefaultProviderName is not null)
        {
            DocumentValidation.Identifier(options.DefaultProviderName, nameof(options.DefaultProviderName));
            if (!_providers.ContainsKey(options.DefaultProviderName))
            {
                throw new InvalidOperationException("The configured default document provider is not registered.");
            }
            _defaultProviderName = options.DefaultProviderName;
        }
        else if (_providers.Count == 1)
        {
            _defaultProviderName = _providers.Keys.Single();
        }
        Providers = Array.AsReadOnly(_providers.Values.Select(provider => new DocumentProviderInfo(
            provider.Name, provider is IOcrProvider, provider is IStructuredDocumentProvider, provider.Capabilities)).ToArray());
    }

    /// <inheritdoc />
    public IReadOnlyList<DocumentProviderInfo> Providers { get; }

    /// <inheritdoc />
    public async Task<ExtractionResult<T>> ExtractAsync<T>(DocumentInput document, ExtractionOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new ExtractionOptions();
        DocumentValidation.Request(options);
        var provider = GetProvider(options.ProviderName);
        if (provider is not IStructuredDocumentProvider extractor)
        {
            throw new NotSupportedException("The selected provider does not implement structured extraction.");
        }
        using var activity = Activities.StartActivity("document.extract");
        activity?.SetTag("document.provider", provider.Name);
        var start = Stopwatch.GetTimestamp();
        try
        {
            var schema = _schema.Create<T>();
            var result = await extractor.ExtractJsonAsync(document, schema, options, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var value = _schema.Deserialize<T>(result.Data, schema);
            return new ExtractionResult<T>(value, result.Metadata with { Duration = Stopwatch.GetElapsedTime(start) });
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<OcrResult> ReadAsync(DocumentInput document, DocumentRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new DocumentRequestOptions();
        DocumentValidation.Request(options);
        var provider = GetProvider(options.ProviderName);
        if (provider is not IOcrProvider reader)
        {
            throw new NotSupportedException("The selected provider does not implement OCR or transcription.");
        }
        using var activity = Activities.StartActivity("document.read");
        activity?.SetTag("document.provider", provider.Name);
        var start = Stopwatch.GetTimestamp();
        try
        {
            var result = await reader.ReadAsync(document, options, cancellationToken).ConfigureAwait(false);
            return result with { Metadata = result.Metadata with { Duration = Stopwatch.GetElapsedTime(start) } };
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
    }

    private IDocumentProvider GetProvider(string? requestedName)
    {
        var name = requestedName ?? _defaultProviderName;
        if (name is null)
        {
            throw new InvalidOperationException("Select a provider explicitly or configure DefaultProviderName.");
        }
        return _providers.TryGetValue(name, out var provider)
            ? provider
            : throw new InvalidOperationException("The requested document provider is not registered.");
    }
}
