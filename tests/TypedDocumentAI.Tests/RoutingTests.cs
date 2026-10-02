using System.Text.Json;

namespace TypedDocumentAI.Tests;

public sealed class RoutingTests
{
    [Fact]
    public async Task A_single_provider_is_selected_without_a_default()
    {
        var provider = new TextOnlyProvider("local");
        var result = await TestData.Client(provider).ReadAsync(TestData.Pdf());
        Assert.Equal("local", result.Metadata.ProviderName);
    }

    [Fact]
    public async Task Multiple_providers_require_an_explicit_choice()
    {
        var client = TestData.Client(new TextOnlyProvider("one"), new TextOnlyProvider("two"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ReadAsync(TestData.Pdf()));
        var result = await client.ReadAsync(TestData.Pdf(), new DocumentRequestOptions { ProviderName = "TWO" });
        Assert.Equal("two", result.Metadata.ProviderName);
    }

    [Fact]
    public async Task The_configured_default_is_used()
    {
        var client = new DocumentClient(new[] { new TextOnlyProvider("one"), new TextOnlyProvider("two") },
            new SystemTextJsonDocumentSchema(), new DocumentAIOptions { DefaultProviderName = "two" });
        Assert.Equal("two", (await client.ReadAsync(TestData.Pdf())).Metadata.ProviderName);
    }

    [Fact]
    public async Task Unknown_names_fail_without_contacting_another_provider()
    {
        var provider = new TextOnlyProvider("one");
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestData.Client(provider).ReadAsync(TestData.Pdf(),
            new DocumentRequestOptions { ProviderName = "missing" }));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public void Duplicate_case_insensitive_names_are_rejected() =>
        Assert.Throws<InvalidOperationException>(() => TestData.Client(new TextOnlyProvider("A"), new TextOnlyProvider("a")));

    [Fact]
    public void An_empty_provider_set_is_rejected() => Assert.Throws<InvalidOperationException>(() => TestData.Client());

    [Fact]
    public void An_unregistered_default_is_rejected() => Assert.Throws<InvalidOperationException>(() =>
        new DocumentClient(new[] { new TextOnlyProvider("a") }, new SystemTextJsonDocumentSchema(),
            new DocumentAIOptions { DefaultProviderName = "missing" }));

    [Fact]
    public async Task Ocr_only_plugins_do_not_need_a_structured_extraction_stub()
    {
        var provider = new TextOnlyProvider("local");
        var client = TestData.Client(provider);
        Assert.False(Assert.Single(client.Providers).SupportsStructuredExtraction);
        await Assert.ThrowsAsync<NotSupportedException>(() => client.ExtractAsync<Invoice>(TestData.Pdf()));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Provider_failures_do_not_trigger_cross_provider_fallback()
    {
        using var handler = new StubHandler(_ => TestData.Json("{}", System.Net.HttpStatusCode.ServiceUnavailable));
        var other = new TextOnlyProvider("other");
        var client = TestData.Client(TestData.MistralProvider(handler), other);
        await Assert.ThrowsAsync<DocumentProviderException>(() => client.ReadAsync(TestData.Pdf(),
            new DocumentRequestOptions { ProviderName = "mistral" }));
        Assert.Equal(0, other.Calls);
    }

    [Fact]
    public async Task Cancellation_is_checked_before_a_plugin_is_called()
    {
        var provider = new TextOnlyProvider("local");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TestData.Client(provider).ReadAsync(TestData.Pdf(),
            cancellationToken: cancellation.Token));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Invalid_schemas_fail_before_a_paid_request()
    {
        using var handler = new StubHandler(_ => TestData.Mistral());
        await Assert.ThrowsAsync<DocumentSchemaException>(() => TestData.Client(TestData.MistralProvider(handler))
            .ExtractAsync<SchemaTests.Recursive>(TestData.Pdf()));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Third_party_structured_results_are_validated_by_the_core()
    {
        var client = TestData.Client(new MalformedStructuredProvider());
        await Assert.ThrowsAsync<DocumentResponseException>(() => client.ExtractAsync<Invoice>(TestData.Pdf()));
    }

    private sealed class TextOnlyProvider(string name) : IOcrProvider
    {
        public string Name => name;
        public DocumentCapabilities Capabilities => DocumentCapabilities.None;
        internal int Calls { get; private set; }
        public Task<OcrResult> ReadAsync(DocumentInput document, DocumentRequestOptions options, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new OcrResult("synthetic plugin output", Array.Empty<OcrPage>(), new ProcessingMetadata(Name, "local")));
        }
    }

    private sealed class MalformedStructuredProvider : IStructuredDocumentProvider
    {
        public string Name => "custom";
        public DocumentCapabilities Capabilities => DocumentCapabilities.None;
        public Task<JsonExtractionResult> ExtractJsonAsync(DocumentInput document, JsonElement schema,
            ExtractionOptions options, CancellationToken cancellationToken = default)
        {
            using var json = JsonDocument.Parse("{}");
            return Task.FromResult(new JsonExtractionResult(json.RootElement.Clone(), new ProcessingMetadata(Name, "test")));
        }
    }
}
