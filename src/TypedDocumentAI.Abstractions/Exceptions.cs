using System.Net;

namespace TypedDocumentAI;

/// <summary>Base exception for package-specific failures. Messages never embed document bodies.</summary>
public class DocumentException : Exception
{
    /// <summary>Creates a package exception with a safe diagnostic message.</summary>
    public DocumentException(string message) : base(message) { }
}

/// <summary>The local input, provider input or response byte limit was exceeded.</summary>
public sealed class DocumentLimitException : DocumentException
{
    /// <summary>Creates a size-limit failure.</summary>
    public DocumentLimitException(string message, long limitBytes) : base(message) => LimitBytes = limitBytes;
    /// <summary>The configured byte limit that was exceeded.</summary>
    public long LimitBytes { get; }
}

/// <summary>A .NET type or schema uses an unsupported shape.</summary>
public sealed class DocumentSchemaException : DocumentException
{
    /// <summary>Creates a schema failure.</summary>
    public DocumentSchemaException(string message) : base(message) { }
}

/// <summary>A response is malformed, missing required data or incompatible with the requested type.</summary>
public class DocumentResponseException : DocumentException
{
    /// <summary>Creates a response-contract failure, without retaining sensitive upstream content.</summary>
    public DocumentResponseException(string message) : base(message) { }
}

/// <summary>The provider returned an HTTP error or a transport failure.</summary>
public sealed class DocumentProviderException : DocumentException
{
    /// <summary>Creates a provider failure with safe HTTP metadata.</summary>
    public DocumentProviderException(string providerName, HttpStatusCode? statusCode, string? requestId = null)
        : base(statusCode is null
            ? "The document provider could not be reached."
            : $"The document provider returned HTTP {(int)statusCode.Value}.")
    {
        ProviderName = providerName;
        StatusCode = statusCode;
        RequestId = requestId;
    }
    /// <summary>The configured provider name.</summary>
    public string ProviderName { get; }
    /// <summary>The HTTP status, or null when no HTTP response was received.</summary>
    public HttpStatusCode? StatusCode { get; }
    /// <summary>The provider request ID, when available.</summary>
    public string? RequestId { get; }
}

/// <summary>The library's provider request deadline elapsed. Caller cancellation remains OperationCanceledException.</summary>
public sealed class DocumentTimeoutException : DocumentException
{
    /// <summary>Creates a provider timeout failure.</summary>
    public DocumentTimeoutException(string providerName, TimeSpan timeout)
        : base("The document provider request exceeded its configured timeout.")
    {
        ProviderName = providerName;
        Timeout = timeout;
    }
    /// <summary>The configured provider name.</summary>
    public string ProviderName { get; }
    /// <summary>The deadline applied to the request, including retry delays.</summary>
    public TimeSpan Timeout { get; }
}

/// <summary>The model refused to process the input. Refusal text is deliberately not retained.</summary>
public sealed class DocumentRefusalException : DocumentResponseException
{
    /// <summary>Creates a refusal failure.</summary>
    public DocumentRefusalException() : base("The provider refused the document request.") { }
}

/// <summary>The provider did not finish its response. Partial extraction is never reported as success.</summary>
public sealed class DocumentIncompleteException : DocumentResponseException
{
    /// <summary>Creates an incomplete-response failure.</summary>
    public DocumentIncompleteException() : base("The provider returned an incomplete response.") { }
}
