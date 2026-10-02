using Microsoft.Extensions.DependencyInjection;

namespace TypedDocumentAI.Http;

/// <summary>HTTP registration defaults shared by provider packages.</summary>
public static class DocumentHttpRegistration
{
    /// <summary>Adds a factory-managed client with no cookies or redirects and no header values in HTTP logs.</summary>
    public static IHttpClientBuilder AddDocumentHttpClient(this IServiceCollection services, string clientName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        return services.AddHttpClient(clientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .RedactLoggedHeaders(static _ => true);
    }
}
