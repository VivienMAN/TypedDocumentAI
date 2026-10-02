using System.Text.Json;
using TypedDocumentAI.Mistral;
using TypedDocumentAI.OpenAI;

namespace TypedDocumentAI.Tests;

public sealed class MistralProviderTests
{
    [Fact]
    public async Task Typed_extraction_uses_inline_pdf_and_strict_annotation_schema()
    {
        using var handler = new StubHandler(_ => TestData.Mistral());
        var result = await TestData.Client(TestData.MistralProvider(handler)).ExtractAsync<Invoice>(TestData.Pdf());
        Assert.Equal("INV-001", result.Value.Number);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v1/ocr", request.Uri.AbsolutePath);
        Assert.Equal("Bearer unit-test-key", request.Authorization);
        var document = request.Body.GetProperty("document");
        Assert.Equal("document_url", document.GetProperty("type").GetString());
        Assert.StartsWith("data:application/pdf;base64,", document.GetProperty("document_url").GetString());
        var schema = request.Body.GetProperty("document_annotation_format").GetProperty("json_schema");
        Assert.True(schema.GetProperty("strict").GetBoolean());
        Assert.Equal("object", schema.GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal(1, result.Metadata.PagesProcessed);
        Assert.Null(result.Metadata.InputTokens);
    }

    [Fact]
    public async Task Images_use_the_image_chunk()
    {
        using var handler = new StubHandler(_ => TestData.Mistral());
        await TestData.Client(TestData.MistralProvider(handler)).ReadAsync(TestData.Image());
        var document = Assert.Single(handler.Requests).Body.GetProperty("document");
        Assert.Equal("image_url", document.GetProperty("type").GetString());
        Assert.StartsWith("data:image/png;base64,", document.GetProperty("image_url").GetString());
    }

    [Fact]
    public async Task Raw_ocr_retains_real_pages_and_separated_headers()
    {
        using var handler = new StubHandler(_ => TestData.Mistral());
        var result = await TestData.Client(TestData.MistralProvider(handler)).ReadAsync(TestData.Pdf(),
            new DocumentRequestOptions { ProviderOptions = new MistralRequestOptions { ExtractHeaders = true, ExtractFooters = true } });
        var page = Assert.Single(result.Pages);
        Assert.Equal(0, page.Index);
        Assert.Equal("Header", page.Header);
        Assert.Equal("Footer", page.Footer);
        Assert.Contains("Header", result.Text);
        Assert.Contains("125.50", result.Text);
        Assert.False(Assert.Single(handler.Requests).Body.TryGetProperty("document_annotation_format", out _));
    }

    [Fact]
    public async Task Selected_pages_and_model_override_are_sent_without_changing_global_defaults()
    {
        using var handler = new StubHandler(_ => TestData.Mistral());
        var client = TestData.Client(TestData.MistralProvider(handler));
        await client.ExtractAsync<Invoice>(TestData.Pdf(), new ExtractionOptions
        {
            SchemaName = "invoice_v2",
            Instructions = "Extract the customer invoice only.",
            ProviderOptions = new MistralRequestOptions { Model = "pinned-test-model", Pages = new[] { 0, 2 } }
        });
        await client.ExtractAsync<Invoice>(TestData.Pdf());
        var requests = handler.Requests;
        Assert.Equal("pinned-test-model", requests[0].Body.GetProperty("model").GetString());
        Assert.Equal(new[] { 0, 2 }, requests[0].Body.GetProperty("pages").EnumerateArray().Select(page => page.GetInt32()));
        Assert.Contains("customer invoice", requests[0].Body.GetProperty("document_annotation_prompt").GetString());
        Assert.Equal("mistral-ocr-latest", requests[1].Body.GetProperty("model").GetString());
        Assert.False(requests[1].Body.TryGetProperty("pages", out _));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, 1)]
    public async Task Invalid_page_selections_fail_before_network(int first, int second)
    {
        using var handler = new StubHandler(_ => TestData.Mistral());
        await Assert.ThrowsAsync<ArgumentException>(() => TestData.Client(TestData.MistralProvider(handler)).ReadAsync(TestData.Pdf(),
            new DocumentRequestOptions { ProviderOptions = new MistralRequestOptions { Pages = new[] { first, second } } }));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Another_providers_options_are_not_silently_ignored()
    {
        using var handler = new StubHandler(_ => TestData.Mistral());
        await Assert.ThrowsAsync<ArgumentException>(() => TestData.Client(TestData.MistralProvider(handler)).ReadAsync(TestData.Pdf(),
            new DocumentRequestOptions { ProviderOptions = new OpenAiRequestOptions() }));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Raw_ocr_rejects_unsupported_instructions()
    {
        using var handler = new StubHandler(_ => TestData.Mistral());
        await Assert.ThrowsAsync<NotSupportedException>(() => TestData.Client(TestData.MistralProvider(handler)).ReadAsync(TestData.Pdf(),
            new DocumentRequestOptions { Instructions = "Summarize this document." }));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Missing_annotation_is_an_error()
    {
        using var handler = new StubHandler(_ => TestData.Mistral(null));
        await Assert.ThrowsAsync<DocumentResponseException>(() => TestData.Client(TestData.MistralProvider(handler))
            .ExtractAsync<Invoice>(TestData.Pdf()));
    }

    [Fact]
    public async Task Object_annotations_are_supported_as_well_as_json_strings()
    {
        using var handler = new StubHandler(_ => TestData.Json("""
            {"model":"mistral-test","document_annotation":{"Number":"A","Total":1,"Date":null},"pages":[]}
            """));
        var result = await TestData.Client(TestData.MistralProvider(handler)).ExtractAsync<Invoice>(TestData.Pdf());
        Assert.Equal("A", result.Value.Number);
    }

    [Fact]
    public async Task Duplicate_page_indices_are_rejected()
    {
        using var handler = new StubHandler(_ => TestData.Json("""
            {"model":"mistral-test","pages":[{"index":0,"markdown":"a"},{"index":0,"markdown":"b"}]}
            """));
        await Assert.ThrowsAsync<DocumentResponseException>(() => TestData.Client(TestData.MistralProvider(handler)).ReadAsync(TestData.Pdf()));
    }

    [Fact]
    public async Task Unknown_top_level_response_fields_are_forward_compatible()
    {
        using var handler = new StubHandler(_ => TestData.Json("""
            {"model":"mistral-test","pages":[{"index":0,"markdown":"text","new_field":42}],"future_capability":{}}
            """));
        var result = await TestData.Client(TestData.MistralProvider(handler)).ReadAsync(TestData.Pdf());
        Assert.Equal("text", result.Text);
        Assert.Null(result.Metadata.PagesProcessed);
    }
}
