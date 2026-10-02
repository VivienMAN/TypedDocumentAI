using TypedDocumentAI.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace TypedDocumentAI.Tests;

public sealed class TransportTests
{
    [Fact]
    public async Task User_agent_reports_the_actual_library_version()
    {
        using var handler = new StubHandler(_ => TestData.Mistral());
        await TestData.Client(TestData.MistralProvider(handler)).ReadAsync(TestData.Pdf());
        var version = typeof(HttpDocumentProvider).Assembly.GetName().Version!.ToString(3);
        Assert.Equal("TypedDocumentAI/" + version, Assert.Single(handler.Requests).UserAgent);
    }

    [Fact]
    public async Task Retries_are_disabled_by_default()
    {
        using var handler = new StubHandler(_ => TestData.Json("{}", HttpStatusCode.TooManyRequests));
        await Assert.ThrowsAsync<DocumentProviderException>(() => TestData.Client(TestData.MistralProvider(handler)).ReadAsync(TestData.Pdf()));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task Opt_in_retries_cover_selected_transient_statuses(int status)
    {
        var attempts = 0;
        using var handler = new StubHandler(_ => ++attempts == 1
            ? TestData.Json("{}", (HttpStatusCode)status) : TestData.Mistral());
        await TestData.Client(TestData.MistralProvider(handler, options => options.MaxRetryAttempts = 1)).ReadAsync(TestData.Pdf());
        Assert.Equal(2, attempts);
        Assert.Equal(handler.Requests[0].Body.GetRawText(), handler.Requests[1].Body.GetRawText());
    }

    [Theory]
    [InlineData(301)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    public async Task Other_statuses_are_not_retried(int status)
    {
        using var handler = new StubHandler(_ => TestData.Json("{}", (HttpStatusCode)status));
        await Assert.ThrowsAsync<DocumentProviderException>(() => TestData.Client(
            TestData.MistralProvider(handler, options => options.MaxRetryAttempts = 2)).ReadAsync(TestData.Pdf()));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Retry_budget_is_finite()
    {
        using var handler = new StubHandler(_ => TestData.Json("{}", HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<DocumentProviderException>(() => TestData.Client(
            TestData.MistralProvider(handler, options => options.MaxRetryAttempts = 2)).ReadAsync(TestData.Pdf()));
        Assert.Collection(handler.Requests, _ => { }, _ => { }, _ => { });
    }

    [Fact]
    public async Task Retry_after_is_not_shortened_when_it_exceeds_the_configured_limit()
    {
        using var handler = new StubHandler(_ =>
        {
            var response = TestData.Json("{}", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
            return response;
        });
        await Assert.ThrowsAsync<DocumentProviderException>(() => TestData.Client(
            TestData.MistralProvider(handler, options => options.MaxRetryAttempts = 2)).ReadAsync(TestData.Pdf()));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Retry_after_zero_allows_an_immediate_retry()
    {
        var attempts = 0;
        using var handler = new StubHandler(_ =>
        {
            if (++attempts > 1)
            {
                return TestData.Mistral();
            }
            var response = TestData.Json("{}", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return response;
        });
        await TestData.Client(TestData.MistralProvider(handler, options => options.MaxRetryAttempts = 1)).ReadAsync(TestData.Pdf());
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Transport_failures_are_retried_only_when_explicitly_enabled()
    {
        var attempts = 0;
        using var handler = new StubHandler(_ => ++attempts == 1 ? throw new HttpRequestException("network") : TestData.Mistral());
        await TestData.Client(TestData.MistralProvider(handler, options => options.MaxRetryAttempts = 1)).ReadAsync(TestData.Pdf());
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Non_success_bodies_are_not_embedded_in_errors()
    {
        using var handler = new StubHandler(_ =>
        {
            var response = TestData.Json("{\"private\":\"SECRET CONTENT\"}", HttpStatusCode.BadRequest);
            response.Headers.Add("x-request-id", "req-test-123");
            return response;
        });
        var exception = await Assert.ThrowsAsync<DocumentProviderException>(() => TestData.Client(TestData.MistralProvider(handler))
            .ReadAsync(TestData.Pdf()));
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("req-test-123", exception.RequestId);
        Assert.DoesNotContain("SECRET", exception.ToString());
        Assert.DoesNotContain("unit-test-key", exception.ToString());
    }

    [Fact]
    public async Task Caller_cancellation_is_not_converted_into_a_timeout()
    {
        using var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return TestData.Mistral();
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TestData.Client(TestData.MistralProvider(handler))
            .ReadAsync(TestData.Pdf(), cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Provider_deadline_has_its_own_exception_type()
    {
        using var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return TestData.Mistral();
        });
        await Assert.ThrowsAsync<DocumentTimeoutException>(() => TestData.Client(TestData.MistralProvider(handler,
            options => options.RequestTimeout = TimeSpan.FromMilliseconds(50))).ReadAsync(TestData.Pdf()));
    }

    [Fact]
    public async Task Response_size_is_bounded_with_content_length()
    {
        using var handler = new StubHandler(_ => TestData.Json(new string('x', 100)));
        await Assert.ThrowsAsync<DocumentLimitException>(() => TestData.Client(TestData.MistralProvider(handler,
            options => options.MaxResponseBytes = 10)).ReadAsync(TestData.Pdf()));
    }

    [Fact]
    public async Task Response_size_is_bounded_without_content_length()
    {
        using var stream = new NonSeekableStream(Encoding.UTF8.GetBytes(new string('x', 100)));
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
        await Assert.ThrowsAsync<DocumentLimitException>(() => TestData.Client(TestData.MistralProvider(handler,
            options => options.MaxResponseBytes = 10)).ReadAsync(TestData.Pdf()));
        Assert.Equal(11, stream.BytesRead);
        Assert.True(stream.WasDisposed);
    }

    [Fact]
    public async Task A_timeout_also_covers_the_response_body()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BlockingStream())
        });
        await Assert.ThrowsAsync<DocumentTimeoutException>(() => TestData.Client(TestData.MistralProvider(handler,
            options => options.RequestTimeout = TimeSpan.FromMilliseconds(50))).ReadAsync(TestData.Pdf()));
    }

    [Fact]
    public async Task Invalid_json_is_not_retried_or_exposed()
    {
        using var handler = new StubHandler(_ => TestData.Json("SECRET not-json"));
        var exception = await Assert.ThrowsAsync<DocumentResponseException>(() => TestData.Client(TestData.MistralProvider(handler,
            options => options.MaxRetryAttempts = 2)).ReadAsync(TestData.Pdf()));
        Assert.DoesNotContain("SECRET", exception.ToString());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Provider_size_limits_are_checked_before_network()
    {
        using var handler = new StubHandler(_ => TestData.Mistral());
        await Assert.ThrowsAsync<DocumentLimitException>(() => TestData.Client(TestData.MistralProvider(handler,
            options => options.MaxDocumentBytes = 1)).ReadAsync(TestData.Pdf()));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Unsupported_media_types_are_checked_before_network()
    {
        using var handler = new StubHandler(_ => TestData.Mistral());
        var input = DocumentInput.FromBytes(new byte[] { 1 }, "a.gif", "image/gif");
        await Assert.ThrowsAsync<NotSupportedException>(() => TestData.Client(TestData.MistralProvider(handler)).ReadAsync(input));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_configured_api_root_is_normalized_without_losing_its_path()
    {
        using var handler = new StubHandler(_ => TestData.Mistral());
        await TestData.Client(TestData.MistralProvider(handler,
            options => options.BaseAddress = new Uri("https://trusted.example/proxy/v1"))).ReadAsync(TestData.Pdf());
        Assert.Equal("/proxy/v1/ocr", Assert.Single(handler.Requests).Uri.AbsolutePath);
    }

    [Theory]
    [InlineData("http://api.example/v1/")]
    [InlineData("https://user:secret@api.example/v1/")]
    [InlineData("https://api.example/v1/?key=secret")]
    [InlineData("https://api.example/v1/#fragment")]
    public void Unsafe_api_roots_are_rejected(string address)
    {
        using var handler = new StubHandler(_ => TestData.Mistral());
        Assert.Throws<ArgumentException>(() => TestData.MistralProvider(handler, options => options.BaseAddress = new Uri(address)));
    }

    private sealed class BlockingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
