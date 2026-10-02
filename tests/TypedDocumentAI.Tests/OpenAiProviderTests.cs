using TypedDocumentAI.Mistral;
using TypedDocumentAI.OpenAI;

namespace TypedDocumentAI.Tests;

public sealed class OpenAiProviderTests
{
    [Fact]
    public async Task Typed_extraction_uses_responses_and_disables_stored_responses()
    {
        using var handler = new StubHandler(_ => TestData.OpenAi());
        var result = await TestData.Client(TestData.OpenAiProvider(handler)).ExtractAsync<Invoice>(TestData.Pdf());
        Assert.Equal("INV-001", result.Value.Number);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/responses", request.Uri.AbsolutePath);
        Assert.False(request.Body.GetProperty("store").GetBoolean());
        Assert.Equal("json_schema", request.Body.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        Assert.True(request.Body.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        var document = request.Body.GetProperty("input")[0].GetProperty("content")[1];
        Assert.Equal("input_file", document.GetProperty("type").GetString());
        Assert.StartsWith("data:application/pdf;base64,", document.GetProperty("file_data").GetString());
        Assert.Equal(123L, result.Metadata.InputTokens);
        Assert.Equal(45L, result.Metadata.OutputTokens);
        Assert.Null(result.Metadata.PagesProcessed);
    }

    [Fact]
    public async Task Images_use_input_image_not_input_file()
    {
        using var handler = new StubHandler(_ => TestData.OpenAi("Visible text"));
        await TestData.Client(TestData.OpenAiProvider(handler)).ReadAsync(TestData.Image());
        var part = Assert.Single(handler.Requests).Body.GetProperty("input")[0].GetProperty("content")[1];
        Assert.Equal("input_image", part.GetProperty("type").GetString());
        Assert.StartsWith("data:image/png;base64,", part.GetProperty("image_url").GetString());
        Assert.Equal("high", part.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Raw_transcription_does_not_invent_page_metadata()
    {
        using var handler = new StubHandler(_ => TestData.OpenAi("# Invoice\nTotal: 125.50"));
        var client = TestData.Client(TestData.OpenAiProvider(handler));
        var result = await client.ReadAsync(TestData.Pdf());
        Assert.Contains("125.50", result.Text);
        Assert.Empty(result.Pages);
        Assert.Null(result.Metadata.PagesProcessed);
        Assert.Equal(DocumentCapabilities.None, Assert.Single(client.Providers).Capabilities);
        Assert.False(Assert.Single(handler.Requests).Body.TryGetProperty("text", out _));
    }

    [Fact]
    public async Task Per_request_model_and_token_limit_are_supported()
    {
        using var handler = new StubHandler(_ => TestData.OpenAi());
        await TestData.Client(TestData.OpenAiProvider(handler)).ExtractAsync<Invoice>(TestData.Pdf(), new ExtractionOptions
        {
            ProviderOptions = new OpenAiRequestOptions { Model = "configured-test-model", MaxOutputTokens = 2048 }
        });
        var request = Assert.Single(handler.Requests);
        Assert.Equal("configured-test-model", request.Body.GetProperty("model").GetString());
        Assert.Equal(2048, request.Body.GetProperty("max_output_tokens").GetInt32());
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("in_progress")]
    [InlineData("queued")]
    public async Task Partial_or_pending_output_is_not_accepted(string status)
    {
        using var handler = new StubHandler(_ => TestData.OpenAi(status: status));
        await Assert.ThrowsAsync<DocumentIncompleteException>(() => TestData.Client(TestData.OpenAiProvider(handler))
            .ExtractAsync<Invoice>(TestData.Pdf()));
    }

    [Fact]
    public async Task Refusals_are_typed_and_do_not_expose_the_refusal_text()
    {
        using var handler = new StubHandler(_ => TestData.OpenAi("SECRET REFUSAL", type: "refusal"));
        var exception = await Assert.ThrowsAsync<DocumentRefusalException>(() => TestData.Client(TestData.OpenAiProvider(handler))
            .ExtractAsync<Invoice>(TestData.Pdf()));
        Assert.DoesNotContain("SECRET", exception.ToString());
    }

    [Fact]
    public async Task Empty_output_array_is_an_error()
    {
        using var handler = new StubHandler(_ => TestData.Json("""{"status":"completed","model":"test","output":[]}"""));
        await Assert.ThrowsAsync<DocumentResponseException>(() => TestData.Client(TestData.OpenAiProvider(handler)).ReadAsync(TestData.Pdf()));
    }

    [Fact]
    public async Task Markdown_fences_are_not_silently_repaired_into_json()
    {
        using var handler = new StubHandler(_ => TestData.OpenAi("```json\n{}\n```"));
        await Assert.ThrowsAsync<DocumentResponseException>(() => TestData.Client(TestData.OpenAiProvider(handler))
            .ExtractAsync<Invoice>(TestData.Pdf()));
    }

    [Fact]
    public async Task Reasoning_items_are_skipped_and_output_text_is_read_from_messages()
    {
        using var handler = new StubHandler(_ => TestData.Json("""
            {"status":"completed","model":"test","output":[
              {"type":"reasoning","summary":[]},
              {"type":"message","role":"assistant","content":[{"type":"output_text","text":"actual document text"}]}
            ]}
            """));
        var result = await TestData.Client(TestData.OpenAiProvider(handler)).ReadAsync(TestData.Pdf());
        Assert.Equal("actual document text", result.Text);
    }

    [Fact]
    public async Task Another_providers_settings_fail_before_network()
    {
        using var handler = new StubHandler(_ => TestData.OpenAi());
        await Assert.ThrowsAsync<ArgumentException>(() => TestData.Client(TestData.OpenAiProvider(handler)).ReadAsync(TestData.Pdf(),
            new DocumentRequestOptions { ProviderOptions = new MistralRequestOptions { Pages = new[] { 0 } } }));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Invalid_token_limits_fail_before_network()
    {
        using var handler = new StubHandler(_ => TestData.OpenAi());
        await Assert.ThrowsAsync<ArgumentException>(() => TestData.Client(TestData.OpenAiProvider(handler)).ReadAsync(TestData.Pdf(),
            new DocumentRequestOptions { ProviderOptions = new OpenAiRequestOptions { MaxOutputTokens = 0 } }));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_failed_generation_is_not_a_successful_ocr_response()
    {
        using var handler = new StubHandler(_ => TestData.OpenAi(status: "failed"));
        await Assert.ThrowsAsync<DocumentResponseException>(() => TestData.Client(TestData.OpenAiProvider(handler)).ReadAsync(TestData.Pdf()));
    }
}
