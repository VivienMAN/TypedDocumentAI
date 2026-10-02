# Implement another provider

## Start with the smallest true capability

Implement `IOcrProvider` for text recognition/transcription, `IStructuredDocumentProvider` for schema-guided JSON extraction, or both. Each inherits `IDocumentProvider`, which provides the stable routing name and advertised adapter capabilities.

Do not implement structured extraction by throwing `NotImplementedException`. An OCR-only engine can simply omit that interface; the client reports the unsupported operation before invoking it. Raw text plus a later LLM extraction stage is a separate application pipeline, not an implicit responsibility of every OCR adapter.

The runnable [CustomProviderDemo](../samples/CustomProviderDemo/Program.cs) is a complete offline `text/plain` decoder. It demonstrates registration and capability separation; it is not an OCR engine and does not pretend to process images.

```csharp
services.AddDocumentAI()
    .AddProvider<PlainTextProvider>();
```

This is the same extension point used by the built-in adapter registrations. A factory overload supports named instances or custom construction. Provider registrations are singletons, so avoid mutable per-request fields and scoped service dependencies.

## Contract checklist

Choose a unique 1-64 character ASCII letter/digit/underscore/hyphen name. Names are compared case-insensitively. Validate document size, MIME type, cancellation and options before sending content anywhere. Reject options belonging to another provider instead of ignoring them.

Return owned data. In particular, clone a `JsonElement` before disposing the `JsonDocument` that created it. Do not return memory backed by disposed resources. For extraction, return `JsonExtractionResult`; the common client applies local shape validation and deserialization.

For OCR, return actual recognized text. Populate `Pages` only with real page segmentation, with provider-sourced page indices. Set `PageText`, `PageSelection` or `HeadersAndFooters` only when the adapter implements the corresponding behavior. Leave unknown usage fields null. Schema-valid JSON is not evidence that recognition was accurate.

Keep failures safe. Do not attach raw provider bodies, annotations, secrets or document content to exception messages or inner exceptions. Preserve caller cancellation. Do not swallow refusals, silently repair arbitrary JSON, or silently switch vendors.

## Optional HTTP base

`TypedDocumentAI.Http.HttpDocumentProvider` supplies bounded JSON responses, safe errors, configuration snapshots, per-request authentication, timeouts and opt-in retries. Derive from it only for an HTTP JSON provider whose needs fit this model. Register its client through `AddDocumentHttpClient`, and select the name used in the base constructor.

This base deliberately does not implement an arbitrary provider protocol, multipart upload, polling, streaming results or cloud-specific signing. An adapter requiring those features can use its own transport while preserving the operation contracts.

## Replacing the schema engine

Register an `IDocumentSchema` before `AddDocumentAI`, whose default registration uses `TryAddSingleton`. `Create<T>` and `Deserialize<T>` must share a compatible contract. A replacement is responsible for its own validation and each provider's supported schema subset. NativeAOT/trimming must be tested end to end, including the provider implementation; replacing the schema engine alone is not a compatibility certification.

## Tests expected for a new adapter

Cover exact JSON payloads, response parsing, wrong options, media and size limits, malformed responses, missing required extraction fields, caller cancellation, timeouts, safe error messages and capability metadata. Reuse the HTTP test-double pattern in [TestSupport.cs](../tests/TypedDocumentAI.Tests/TestSupport.cs). Add registration tests and at least one explicit live smoke test using synthetic data, separate from normal PR CI.
