# Migration from TypedMistral.DocumentAI V1

V2 is a new multi-provider source repository and package family, not a binary-compatible update to the previous single-package prototype. The first public four-package release starts at `1.0.0`. V2 describes the architecture generation, not the NuGet major version; it is not a binary-compatible update of the prototype. See [validation](VALIDATION.md) for executed checks.

| V1 concept | V2 replacement |
| --- | --- |
| `TypedMistral.DocumentAI` | `TypedDocumentAI.Mistral`, with Core and Abstractions dependencies |
| Mistral-specific extraction service | Inject provider-neutral `IDocumentClient` |
| `AddTypedMistralDocumentAI(...)` | `AddDocumentAI(...).AddMistral(...)` |
| `ExtractAsync<T>(stream, filename, contentType)` | Build `DocumentInput`, then `ExtractAsync<T>(document, options, token)` |
| Direct `T` result | `ExtractionResult<T>.Value`, plus Metadata |
| Upload, OCR, remote file deletion | Inline data URI, with no Files API resource created |
| Mistral-only call options | Common options plus `MistralRequestOptions` |

## Typical migration

```csharp
services.AddDocumentAI().AddMistral(options => options.ApiKey = mistralApiKey);

var input = await DocumentInput.FromStreamAsync(
    stream, "invoice.pdf", "application/pdf", cancellationToken: cancellationToken);

var result = await client.ExtractAsync<Invoice>(input, cancellationToken: cancellationToken);
Invoice invoice = result.Value;
```

The caller still owns `stream`; it is consumed from its current position and left open. Input buffering happens before provider selection and requests, so a reusable snapshot can be sent explicitly to another provider if your application authorizes that action.

## Behavior changes to review

POST retries are now disabled by default. Opt in only after accepting duplicate processing/billing risk. Local default input size is 20 MiB rather than importing the earlier project's configuration. There is no PdfPig dependency or local PDF page-count validation; use the provider's current constraints and optional Mistral page selection. No old fixed eight-page rule is embedded in the common contract.

The package no longer creates remote uploaded file IDs, has no delete-after-processing option, and cannot delete old resources created by V1. Inline submission does not imply that the provider retains no data. Review provider data handling independently.

Custom arbitrary JSON schemas are not a public common API in this release line. Typed schema extraction uses a constrained contract and rejects unsupported types earlier. Existing consumers using raw JSON schema calls require a deliberate adaptation, not a namespace-only change.

The new `ReadAsync` operation is separate from typed extraction. OpenAI's implementation is generative transcription and has no provider-sourced page boundaries; do not assume its `Pages` has the same content as Mistral.

## Release migration checklist

Compile the host application, run its integration tests, compare representative synthetic document outputs, review limits and retry behavior, and update exception handling. Keep real documents and API keys out of unit tests and GitHub issue reports. Pin and validate the model/account configuration before a production cutover.
