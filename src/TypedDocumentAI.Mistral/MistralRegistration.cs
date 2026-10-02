using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TypedDocumentAI.Http;

namespace TypedDocumentAI.Mistral;

/// <summary>Registration extensions for the Mistral provider package.</summary>
public static class MistralRegistration
{
    /// <summary>Adds the default "mistral" provider, optionally customizing its factory-managed HTTP pipeline.</summary>
    public static DocumentAIBuilder AddMistral(this DocumentAIBuilder builder, Action<MistralOptions> configure,
        Action<IHttpClientBuilder>? configureHttp = null) => builder.AddMistral("mistral", configure, configureHttp);

    /// <summary>Adds a named Mistral configuration, allowing multiple accounts or model defaults without modifying the core.</summary>
    public static DocumentAIBuilder AddMistral(this DocumentAIBuilder builder, string name,
        Action<MistralOptions> configure, Action<IHttpClientBuilder>? configureHttp = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        DocumentValidation.Identifier(name, nameof(name));
        builder.Services.AddOptions<MistralOptions>(name).Configure(configure)
            .Validate(options => options.GetValidationErrors().Count == 0, "Invalid Mistral provider options.")
            .ValidateOnStart();
        var http = builder.Services.AddDocumentHttpClient(MistralDocumentProvider.GetHttpClientName(name));
        configureHttp?.Invoke(http);
        builder.AddProvider(services => new MistralDocumentProvider(name,
            services.GetRequiredService<IHttpClientFactory>(),
            services.GetRequiredService<IOptionsMonitor<MistralOptions>>().Get(name)));
        return builder;
    }
}
