# Operational behavior

## Input ownership and memory

`FromBytes` makes a defensive copy. `FromStreamAsync` reads from the current stream position and leaves the caller's stream open, including on failure. It works with readable non-seekable streams and consumes at most one byte beyond its local limit to detect overflow. `FromFileAsync` owns and disposes its file stream. File extension inference is deliberately restricted; explicit MIME input is available, but the adapters still enforce their own allowlists.

These are buffered inputs. The default 20 MiB input ceiling is applied both by the helper and independently by each provider. Raising the limit requires changing both where appropriate. A document also creates base64 and JSON allocations; managed allocations are not guaranteed to be zeroed when released. The process-wide memory limit depends on caller concurrency. Use a bounded queue or semaphore at the application level rather than launching every document at once.

## HTTP settings

`HttpProviderOptions` controls the trusted API root, default model, input/response limits, deadline and retries. `MaxResponseBytes` defaults to 16 MiB and is checked while streaming even without a Content-Length header. The two-minute request deadline covers network operations, response-body reads and retry delays. Synchronous encoding/schema work is not an interruptible CPU-time budget, and document loading has the caller's cancellation token rather than a provider HTTP deadline.

The factory-managed client has no competing `HttpClient.Timeout`. Its default handler disables redirects and cookies and limits pooled connection lifetime. An application can customize the named builder:

```csharp
services.AddDocumentAI().AddMistral(
    options => options.ApiKey = apiKey,
    http => http.SetHandlerLifetime(TimeSpan.FromMinutes(5)));
```

Replacing the primary handler can change the security defaults. TLS/certificate validation should never be disabled for production. BaseAddress is trusted operator configuration, not user input; changing it redirects both the key and document.

## Retries and billing

Default: zero additional attempts. If explicitly enabled, only selected transient HTTP statuses (408, 429, 500, 502, 503, 504) and send-level network exceptions are replayed. The budget allows at most five additional attempts. Requests are rebuilt from the same buffered payload. Invalid JSON, refusal, incomplete output and body-read failures are not automatically replayed.

A Retry-After delay larger than MaxRetryDelay causes retries to stop, rather than violating the server's suggested delay. Otherwise it is respected; absent Retry-After uses exponential backoff with jitter. The operation deadline still bounds the retry loop. No idempotency or exactly-once billing guarantee is provided by this code.

Do not layer another generic POST retry handler on top without considering the multiplied attempt budget. Cross-provider fallback is not built in because it changes data recipients, cost, model behavior and service terms.

## Provider defaults and overrides

Mistral defaults to `mistral-ocr-latest`; OpenAI defaults to `gpt-4.1-mini` with 8,192 maximum output tokens. These are configurable defaults, not a claim that they are optimal or the latest models. Pin an upstream model version when reproducibility is required and available. Model aliases and upstream behavior can change independently of a NuGet release.

Each named registration captures configuration when resolved. Updating a configuration source after construction does not rotate a captured key; rebuild the relevant application container/process. Do not register both providers just to use one if the second account is not configured.

## Multiple configurations of one provider

```csharp
services.AddDocumentAI()
    .AddMistral("mistral-account-a", o => o.ApiKey = accountAKey)
    .AddMistral("mistral-account-b", o => o.ApiKey = accountBKey);
```

Choose a name per call or configure DefaultProviderName. This is not a full tenant-authorization boundary: your application must decide which caller is allowed to use which provider registration.
