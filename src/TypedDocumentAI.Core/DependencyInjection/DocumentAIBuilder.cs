using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace TypedDocumentAI;

/// <summary>Registration builder shared by independent provider packages.</summary>
public sealed class DocumentAIBuilder
{
    /// <summary>Wraps a service collection. Usually created by AddDocumentAI.</summary>
    public DocumentAIBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }
    /// <summary>The application service collection, available to third-party provider extensions.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Registers an additional singleton provider implementation with constructor injection.</summary>
    public DocumentAIBuilder AddProvider<TProvider>() where TProvider : class, IDocumentProvider
    {
        Services.AddSingleton<IDocumentProvider, TProvider>();
        return this;
    }

    /// <summary>Registers an additional singleton provider using an explicit factory.</summary>
    public DocumentAIBuilder AddProvider(Func<IServiceProvider, IDocumentProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Services.AddSingleton<IDocumentProvider>(factory);
        return this;
    }
}

/// <summary>Registers the provider-neutral document client.</summary>
public static class DocumentAIServiceCollectionExtensions
{
    /// <summary>Adds routing and schema services. Provider packages are registered separately.</summary>
    public static DocumentAIBuilder AddDocumentAI(
        this IServiceCollection services, Action<DocumentAIOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<DocumentAIOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }
        services.TryAddSingleton<IDocumentSchema, SystemTextJsonDocumentSchema>();
        services.TryAddSingleton<IDocumentClient>(provider => new DocumentClient(
            provider.GetServices<IDocumentProvider>(),
            provider.GetRequiredService<IDocumentSchema>(),
            provider.GetRequiredService<IOptions<DocumentAIOptions>>().Value));
        return new DocumentAIBuilder(services);
    }
}
