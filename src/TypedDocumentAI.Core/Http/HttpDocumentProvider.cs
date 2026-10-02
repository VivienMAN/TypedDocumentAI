using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TypedDocumentAI.Http;

/// <summary>Optional reusable HTTP foundation. Local OCR providers can implement the operation interfaces directly.</summary>
public abstract class HttpDocumentProvider : IDocumentProvider
{
    private static readonly ProductInfoHeaderValue Product = new("TypedDocumentAI",
        typeof(HttpDocumentProvider).Assembly.GetName().Version!.ToString(3));
    private readonly IHttpClientFactory _clientFactory;
    private readonly string _httpClientName;
    private readonly string _apiKey;
    private readonly Uri _baseAddress;
    private readonly int _maxDocumentBytes;
    private readonly int _maxResponseBytes;
    private readonly TimeSpan _requestTimeout;
    private readonly int _maxRetryAttempts;
    private readonly TimeSpan _retryBaseDelay;
    private readonly TimeSpan _maxRetryDelay;

    /// <summary>Captures a validated configuration snapshot. Does not retain a factory-created HttpClient.</summary>
    protected HttpDocumentProvider(string name, string httpClientName, IHttpClientFactory clientFactory, HttpProviderOptions options)
    {
        DocumentValidation.Identifier(name, nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(httpClientName);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(options);
        var errors = options.GetValidationErrors();
        if (errors.Count != 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(options));
        }
        Name = name;
        _httpClientName = httpClientName;
        _clientFactory = clientFactory;
        _apiKey = options.ApiKey;
        var builder = new UriBuilder(options.BaseAddress);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }
        _baseAddress = builder.Uri;
        DefaultModel = options.Model;
        _maxDocumentBytes = options.MaxDocumentBytes;
        _maxResponseBytes = options.MaxResponseBytes;
        _requestTimeout = options.RequestTimeout;
        _maxRetryAttempts = options.MaxRetryAttempts;
        _retryBaseDelay = options.RetryBaseDelay;
        _maxRetryDelay = options.MaxRetryDelay;
    }

    /// <inheritdoc />
    public string Name { get; }
    /// <inheritdoc />
    public abstract DocumentCapabilities Capabilities { get; }
    /// <summary>The captured default model, unaffected by subsequent mutation of options objects.</summary>
    protected string DefaultModel { get; }

    /// <summary>Validates document size, media type, call settings and cancellation before contacting the provider.</summary>
    protected void ValidateInput(DocumentInput document, DocumentRequestOptions options,
        IReadOnlySet<string> supportedMediaTypes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(supportedMediaTypes);
        cancellationToken.ThrowIfCancellationRequested();
        DocumentValidation.Request(options);
        if (document.Length > _maxDocumentBytes)
        {
            throw new DocumentLimitException("The document exceeds the provider input limit.", _maxDocumentBytes);
        }
        if (!supportedMediaTypes.Contains(document.ContentType))
        {
            throw new NotSupportedException("This document media type is not supported by the selected adapter.");
        }
    }

    /// <summary>Resolves and validates an optional per-call model override.</summary>
    protected string ResolveModel(ProviderRequestOptions? options)
    {
        var model = options?.Model ?? DefaultModel;
        if (string.IsNullOrWhiteSpace(model) || model.Length > 200 || model.Any(char.IsControl))
        {
            throw new ArgumentException("The model override is invalid.", nameof(options));
        }
        return model;
    }

    /// <summary>Encodes the document as an inline data URI. Does not create an upstream Files API resource.</summary>
    protected static string ToDataUri(DocumentInput document) =>
        $"data:{document.ContentType};base64,{Convert.ToBase64String(document.Content.Span)}";

    /// <summary>Sends one JSON operation, optionally retrying explicitly enabled transient failures.</summary>
    protected async Task<ProviderHttpResult> SendJsonAsync(string relativePath, JsonObject payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith('/') ||
            relativePath.Contains("..", StringComparison.Ordinal) || !Uri.IsWellFormedUriString(relativePath, UriKind.Relative))
        {
            throw new ArgumentException("Use a relative API path without traversal.", nameof(relativePath));
        }
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        var token = timeout.Token;
        var start = Stopwatch.GetTimestamp();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var client = _clientFactory.CreateClient(_httpClientName);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseAddress, relativePath));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.UserAgent.Add(Product);
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                }
                catch (HttpRequestException) when (attempt < _maxRetryAttempts)
                {
                    await Task.Delay(Backoff(attempt), token).ConfigureAwait(false);
                    continue;
                }
                using (response)
                {
                    var requestId = GetRequestId(response);
                    if (!response.IsSuccessStatusCode)
                    {
                        var delay = RetryDelay(response, attempt);
                        if (attempt < _maxRetryAttempts && IsTransient(response.StatusCode) && delay is { } retryDelay)
                        {
                            response.Dispose();
                            await Task.Delay(retryDelay, token).ConfigureAwait(false);
                            continue;
                        }
                        throw new DocumentProviderException(Name, response.StatusCode, requestId);
                    }
                    var body = await ReadBodyAsync(response, token).ConfigureAwait(false);
                    try
                    {
                        using var json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 64 });
                        if (json.RootElement.ValueKind != JsonValueKind.Object)
                        {
                            throw new DocumentResponseException("The provider response must be a JSON object.");
                        }
                        return new ProviderHttpResult(json.RootElement.Clone(), requestId, Stopwatch.GetElapsedTime(start));
                    }
                    catch (JsonException)
                    {
                        throw new DocumentResponseException("The provider returned invalid JSON.");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new DocumentTimeoutException(Name, _requestTimeout);
        }
        catch (HttpRequestException)
        {
            throw new DocumentProviderException(Name, null);
        }
        catch (IOException)
        {
            throw new DocumentProviderException(Name, null);
        }
    }

    private async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is { } length && length > _maxResponseBytes)
        {
            throw new DocumentLimitException("The HTTP response exceeds the configured limit.", _maxResponseBytes);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var rented = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                var count = (int)Math.Min(rented.Length, (long)_maxResponseBytes - buffer.Length + 1);
                var read = await stream.ReadAsync(rented.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return buffer.ToArray();
                }
                if (buffer.Length + read > _maxResponseBytes)
                {
                    throw new DocumentLimitException("The HTTP response exceeds the configured limit.", _maxResponseBytes);
                }
                buffer.Write(rented, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    private TimeSpan Backoff(int attempt)
    {
        var milliseconds = _retryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt) * (0.5 + Random.Shared.NextDouble());
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, _maxRetryDelay.TotalMilliseconds));
    }

    private TimeSpan? RetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
        if (delay is null)
        {
            return Backoff(attempt);
        }
        if (delay.Value > _maxRetryDelay)
        {
            return null;
        }
        return delay.Value < TimeSpan.Zero ? TimeSpan.Zero : delay.Value;
    }

    private static bool IsTransient(HttpStatusCode status) => status is
        HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static string? GetRequestId(HttpResponseMessage response)
    {
        foreach (var name in new[] { "x-request-id", "request-id" })
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                var value = values.FirstOrDefault();
                if (value is { Length: > 0 and <= 128 } && !value.Any(char.IsControl))
                {
                    return value;
                }
            }
        }
        return null;
    }

    /// <summary>Reads a mandatory property with an exact JSON kind.</summary>
    protected static JsonElement Require(JsonElement parent, string property, JsonValueKind kind)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value) || value.ValueKind != kind)
        {
            throw new DocumentResponseException("The provider response is missing a required field or contains an invalid field type.");
        }
        return value;
    }

    /// <summary>Reads an optional string and rejects incorrectly typed values.</summary>
    protected static string? OptionalString(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new DocumentResponseException("The provider returned an incorrectly typed string field.");
        }
        return value.GetString();
    }

    /// <summary>Reads an optional, non-negative integer measurement.</summary>
    protected static long? OptionalCount(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var count) || count < 0)
        {
            throw new DocumentResponseException("The provider returned an invalid usage measurement.");
        }
        return count;
    }

    /// <summary>Parses a JSON annotation without exposing its contents in exceptions.</summary>
    protected static JsonElement ParseAnnotation(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 64 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new DocumentResponseException("The extracted annotation must be a JSON object.");
            }
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new DocumentResponseException("The extracted annotation is not valid JSON.");
        }
    }

    /// <summary>An owned JSON response and safe request metadata.</summary>
    protected sealed record ProviderHttpResult(JsonElement Body, string? RequestId, TimeSpan Duration);
}
