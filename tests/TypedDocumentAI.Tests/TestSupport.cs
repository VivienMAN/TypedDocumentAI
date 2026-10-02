using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TypedDocumentAI.Mistral;
using TypedDocumentAI.OpenAI;

namespace TypedDocumentAI.Tests;

public sealed class Invoice
{
    [Description("Invoice number exactly as printed")]
    public string? Number { get; init; }
    public decimal? Total { get; init; }
    public DateOnly? Date { get; init; }
}

internal static class TestData
{
    internal const string Annotation = """{"Number":"INV-001","Total":125.50,"Date":"2026-10-01"}""";

    internal static DocumentInput Pdf() => DocumentInput.FromBytes(
        Encoding.UTF8.GetBytes("%PDF-1.7 synthetic bytes for offline HTTP contract tests"), "invoice.pdf", "application/pdf");
    internal static DocumentInput Image() => DocumentInput.FromBytes(new byte[] { 137, 80, 78, 71 }, "invoice.png", "image/png");

    internal static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    internal static HttpResponseMessage Mistral(string? annotation = Annotation)
    {
        var body = new JsonObject
        {
            ["model"] = "mistral-test-model",
            ["pages"] = new JsonArray
            {
                new JsonObject { ["index"] = 0, ["markdown"] = "# Invoice\nTotal: 125.50", ["header"] = "Header", ["footer"] = "Footer" }
            },
            ["document_annotation"] = annotation,
            ["usage_info"] = new JsonObject { ["pages_processed"] = 1 }
        };
        return Json(body.ToJsonString());
    }

    internal static HttpResponseMessage OpenAi(string text = Annotation, string status = "completed", string type = "output_text")
    {
        var part = new JsonObject { ["type"] = type, [type == "refusal" ? "refusal" : "text"] = text };
        var body = new JsonObject
        {
            ["id"] = "resp-test",
            ["status"] = status,
            ["model"] = "openai-test-model",
            ["output"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "message", ["role"] = "assistant", ["status"] = "completed",
                    ["content"] = new JsonArray { part }
                }
            },
            ["usage"] = new JsonObject { ["input_tokens"] = 123, ["output_tokens"] = 45 }
        };
        return Json(body.ToJsonString());
    }

    internal static MistralDocumentProvider MistralProvider(StubHandler handler, Action<MistralOptions>? configure = null,
        string name = "mistral")
    {
        var options = new MistralOptions { ApiKey = "unit-test-key", RetryBaseDelay = TimeSpan.Zero };
        configure?.Invoke(options);
        return new MistralDocumentProvider(name, new StubFactory(handler), options);
    }

    internal static OpenAiDocumentProvider OpenAiProvider(StubHandler handler, Action<OpenAiOptions>? configure = null,
        string name = "openai")
    {
        var options = new OpenAiOptions { ApiKey = "unit-test-key", RetryBaseDelay = TimeSpan.Zero };
        configure?.Invoke(options);
        return new OpenAiDocumentProvider(name, new StubFactory(handler), options);
    }

    internal static DocumentClient Client(params IDocumentProvider[] providers) =>
        new(providers, new SystemTextJsonDocumentSchema(), new DocumentAIOptions());
}

internal sealed record RequestSnapshot(HttpMethod Method, Uri Uri, JsonElement Body, string? Authorization, string UserAgent);

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<RequestSnapshot, CancellationToken, Task<HttpResponseMessage>> _respond;
    private readonly ConcurrentQueue<RequestSnapshot> _requests = new();

    internal StubHandler(Func<RequestSnapshot, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;
    internal StubHandler(Func<RequestSnapshot, HttpResponseMessage> respond) => _respond = (request, _) => Task.FromResult(respond(request));
    internal RequestSnapshot[] Requests => _requests.ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        using var document = JsonDocument.Parse(bytes);
        var snapshot = new RequestSnapshot(request.Method, request.RequestUri!, document.RootElement.Clone(),
            request.Headers.Authorization?.ToString(), request.Headers.UserAgent.ToString());
        _requests.Enqueue(snapshot);
        return await _respond(snapshot, cancellationToken);
    }
}

internal sealed class StubFactory(StubHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
}

internal sealed class NonSeekableStream(byte[] bytes) : Stream
{
    private readonly MemoryStream _inner = new(bytes);
    internal bool WasDisposed { get; private set; }
    internal long BytesRead { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        BytesRead += read;
        return read;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken);
        BytesRead += read;
        return read;
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            WasDisposed = true;
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
