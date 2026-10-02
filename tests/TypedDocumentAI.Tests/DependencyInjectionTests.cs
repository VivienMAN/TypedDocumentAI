using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TypedDocumentAI.Mistral;
using TypedDocumentAI.OpenAI;

namespace TypedDocumentAI.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task Both_adapters_register_and_route_independently()
    {
        using var mistral = new StubHandler(_ => TestData.Mistral());
        using var openai = new StubHandler(_ => TestData.OpenAi());
        var services = new ServiceCollection();
        services.AddDocumentAI(options => options.DefaultProviderName = "mistral")
            .AddMistral(options => options.ApiKey = "test-mistral", http => http.ConfigurePrimaryHttpMessageHandler(() => mistral))
            .AddOpenAI(options => options.ApiKey = "test-openai", http => http.ConfigurePrimaryHttpMessageHandler(() => openai));
        await using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var client = container.GetRequiredService<IDocumentClient>();
        await client.ExtractAsync<Invoice>(TestData.Pdf());
        await client.ExtractAsync<Invoice>(TestData.Pdf(), new ExtractionOptions { ProviderName = "openai" });
        Assert.Single(mistral.Requests);
        Assert.Single(openai.Requests);
        Assert.Equal("Bearer test-mistral", mistral.Requests[0].Authorization);
        Assert.Equal("Bearer test-openai", openai.Requests[0].Authorization);
    }

    [Fact]
    public async Task Multiple_named_configurations_of_the_same_adapter_are_supported()
    {
        using var handlerA = new StubHandler(_ => TestData.Mistral());
        using var handlerB = new StubHandler(_ => TestData.Mistral());
        var services = new ServiceCollection();
        services.AddDocumentAI()
            .AddMistral("account-a", options => { options.ApiKey = "key-a"; options.Model = "model-a"; },
                http => http.ConfigurePrimaryHttpMessageHandler(() => handlerA))
            .AddMistral("account-b", options => { options.ApiKey = "key-b"; options.Model = "model-b"; },
                http => http.ConfigurePrimaryHttpMessageHandler(() => handlerB));
        await using var container = services.BuildServiceProvider();
        var client = container.GetRequiredService<IDocumentClient>();
        await client.ReadAsync(TestData.Pdf(), new DocumentRequestOptions { ProviderName = "account-a" });
        await client.ReadAsync(TestData.Pdf(), new DocumentRequestOptions { ProviderName = "account-b" });
        Assert.Equal("model-a", Assert.Single(handlerA.Requests).Body.GetProperty("model").GetString());
        Assert.Equal("model-b", Assert.Single(handlerB.Requests).Body.GetProperty("model").GetString());
    }

    [Fact]
    public void Missing_api_keys_fail_when_services_are_resolved()
    {
        var services = new ServiceCollection();
        services.AddDocumentAI().AddMistral(_ => { });
        using var container = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => container.GetRequiredService<IDocumentClient>());
    }

    [Fact]
    public void Invalid_token_configuration_is_validated()
    {
        var services = new ServiceCollection();
        services.AddDocumentAI().AddOpenAI(options => { options.ApiKey = "test"; options.MaxOutputTokens = 0; });
        using var container = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => container.GetRequiredService<IDocumentClient>());
    }

    [Fact]
    public void Repeated_core_registration_does_not_duplicate_the_client()
    {
        var services = new ServiceCollection();
        services.AddDocumentAI();
        services.AddDocumentAI().AddMistral(options => options.ApiKey = "test");
        using var container = services.BuildServiceProvider();
        Assert.Single(container.GetServices<IDocumentClient>());
    }

    [Fact]
    public void Provider_registrations_reject_invalid_names()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddDocumentAI().AddMistral("bad name", options => options.ApiKey = "test"));
    }
}
