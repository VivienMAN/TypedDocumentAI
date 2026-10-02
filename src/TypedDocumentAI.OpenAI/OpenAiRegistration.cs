using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TypedDocumentAI.Http;

namespace TypedDocumentAI.OpenAI;

/// <summary>Registration extensions for the OpenAI provider package.</summary>
public static class OpenAiRegistration
{
    /// <summary>Adds the default "openai" provider, optionally customizing its factory-managed HTTP pipeline.</summary>
    public static DocumentAIBuilder AddOpenAI(this DocumentAIBuilder builder, Action<OpenAiOptions> configure,
        Action<IHttpClientBuilder>? configureHttp = null) => builder.AddOpenAI("openai", configure, configureHttp);

    /// <summary>Adds a named OpenAI configuration for an independent account or model default.</summary>
    public static DocumentAIBuilder AddOpenAI(this DocumentAIBuilder builder, string name,
        Action<OpenAiOptions> configure, Action<IHttpClientBuilder>? configureHttp = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        DocumentValidation.Identifier(name, nameof(name));
        builder.Services.AddOptions<OpenAiOptions>(name).Configure(configure)
            .Validate(options => options.GetValidationErrors().Count == 0, "Invalid OpenAI provider options.")
            .ValidateOnStart();
        var http = builder.Services.AddDocumentHttpClient(OpenAiDocumentProvider.GetHttpClientName(name));
        configureHttp?.Invoke(http);
        builder.AddProvider(services => new OpenAiDocumentProvider(name,
            services.GetRequiredService<IHttpClientFactory>(),
            services.GetRequiredService<IOptionsMonitor<OpenAiOptions>>().Get(name)));
        return builder;
    }
}
