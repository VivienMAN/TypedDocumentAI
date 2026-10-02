namespace TypedDocumentAI.Http;

/// <summary>Shared transport settings for HTTP-based document providers.</summary>
public abstract class HttpProviderOptions
{
    /// <summary>A server-side provider API key. Load it from secrets, never from a public client application.</summary>
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>A trusted HTTPS API root, including its version path. Changing it redirects document data and credentials.</summary>
    public Uri BaseAddress { get; set; } = new("https://localhost/");
    /// <summary>The default upstream model. Adapters supply their own default.</summary>
    public string Model { get; set; } = string.Empty;
    /// <summary>Maximum document bytes before base64 encoding. Default: 20 MiB.</summary>
    public int MaxDocumentBytes { get; set; } = DocumentInput.DefaultMaxBytes;
    /// <summary>Maximum uncompressed HTTP response bytes. Default: 16 MiB.</summary>
    public int MaxResponseBytes { get; set; } = 16 * 1024 * 1024;
    /// <summary>Deadline for the HTTP operation, including response-body reads and retry delays.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(2);
    /// <summary>Additional POST attempts. Disabled by default because replay can duplicate processing and charges.</summary>
    public int MaxRetryAttempts { get; set; }
    /// <summary>Initial backoff, with exponential growth and jitter when Retry-After is absent.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    /// <summary>Maximum retry delay. A larger server Retry-After stops retries rather than being shortened.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Validates configuration without returning secret values in the diagnostics.</summary>
    public virtual IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey) || ApiKey.Length > 4096 || ApiKey.Any(char.IsControl))
        {
            errors.Add("ApiKey must be a non-empty, single-line secret.");
        }
        if (BaseAddress is null || !BaseAddress.IsAbsoluteUri || BaseAddress.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(BaseAddress.UserInfo) || !string.IsNullOrEmpty(BaseAddress.Query) ||
            !string.IsNullOrEmpty(BaseAddress.Fragment))
        {
            errors.Add("BaseAddress must be an absolute HTTPS URI without credentials, query or fragment.");
        }
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 200 || Model.Any(char.IsControl))
        {
            errors.Add("Model must be a non-empty, single-line identifier.");
        }
        if (MaxDocumentBytes <= 0 || MaxDocumentBytes > 100 * 1024 * 1024)
        {
            errors.Add("MaxDocumentBytes must be between 1 byte and 100 MiB.");
        }
        if (MaxResponseBytes <= 0 || MaxResponseBytes > 100 * 1024 * 1024)
        {
            errors.Add("MaxResponseBytes must be between 1 byte and 100 MiB.");
        }
        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromDays(1))
        {
            errors.Add("RequestTimeout must be positive and no greater than one day.");
        }
        if (MaxRetryAttempts is < 0 or > 5 || RetryBaseDelay < TimeSpan.Zero ||
            MaxRetryDelay < RetryBaseDelay || MaxRetryDelay > TimeSpan.FromHours(1))
        {
            errors.Add("Retries require 0-5 attempts and non-negative ordered delays up to one hour.");
        }
        return errors.AsReadOnly();
    }
}
