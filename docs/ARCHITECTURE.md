# Architecture and extension boundaries

## Dependency direction

```text
Application
  └── IDocumentClient
        └── TypedDocumentAI.Core
              └── TypedDocumentAI.Abstractions

TypedDocumentAI.Mistral ──references──> Core
TypedDocumentAI.OpenAI  ──references──> Core
Custom provider        ──references──> Abstractions (and optionally Core)
```

The application composes providers with the builder. Core resolves `IEnumerable<IDocumentProvider>` and uses operation interfaces, never a switch over vendor names.

## Operation flow

`DocumentInput` snapshots a bounded local document. `DocumentClient` validates options and selects exactly one registered name. For extraction, it creates a schema before a paid request, calls `IStructuredDocumentProvider`, then validates the returned JSON and deserializes through the same schema service. For text reading it calls `IOcrProvider` and preserves the adapter's reported output.

Providers know their wire formats, MIME support, model defaults and provider-specific settings. Core does not manufacture pages, bounding boxes, confidence or usage metrics. Missing measurements remain absent. Providers may implement just one operation contract; capability discovery reports the interfaces actually implemented.

## Why four packages, not a monolithic SDK

Abstractions is dependency-free. Core carries Microsoft HTTP/DI infrastructure and the default schema engine. Local engines can implement the contracts without inheriting `HttpDocumentProvider`. Independent adapters can be added without editing Core or installing unrelated vendor libraries. There is no chat, embeddings, agents or general-purpose SDK surface in this repository.

## API and implementation visibility

Consumers use the published operation interfaces and result/options types. `HttpDocumentProvider` intentionally exposes a small protected contract to adapter subclasses in other assemblies; see [EXTENDING.md](EXTENDING.md). Other authored implementation methods are private. Schema generation, normalization and shape validation are parts of one partial `SystemTextJsonDocumentSchema` class rather than assembly-wide helper APIs.

A reviewed public/protected reflection snapshot and a separate visibility regression guard accidental API expansion. They do not replace compatibility analysis or real provider integration tests.

## Lifetime and concurrency

The client, schema cache and adapter registrations are singletons. Providers must be thread-safe. Built-in providers copy configuration when constructed, and per-call options are not written back into global settings. Mistral copies page selections before use. Do not mutate collection-valued options while a call is starting.

HTTP clients are created from `IHttpClientFactory` for each operation and disposed afterward, while the factory manages handler pooling. There is no singleton-held `HttpClient` that prevents handler refresh. Named registrations isolate defaults and credentials. Runtime configuration reload and key rotation without rebuilding the container are not provided in this release line.

`DocumentInput` copies caller bytes and consumes caller streams from their current position, leaving them open. File-path helpers own and close their file streams. Memory is bounded per document, not across the entire process. Applications must control concurrency and release references promptly.

## Failure semantics

Unknown names, duplicate registrations, unsupported operations/settings and invalid document types fail before network access. Provider errors do not reroute. Caller cancellation remains `OperationCanceledException`; the adapter's deadline becomes `DocumentTimeoutException`. Incomplete OpenAI generations and refusals have distinct exceptions. HTTP status/request ID may be exposed, not response bodies.

Core's `ActivitySource` is `TypedDocumentAI`. Activities contain the selected provider and error type, not the document, filename, prompt, key or extracted fields. The application decides whether and where to export traces.

## Future evolution

New provider: implement the applicable small interfaces and add a registration extension. New provider-specific feature: add an optional property to that provider's options or introduce a separate optional operation interface; do not burden all adapters with fake implementations. Add universal capabilities only when at least two real implementations demonstrate the same semantics.

Batch jobs, asynchronous server jobs, geometry, confidence, URLs and automatic fallback are intentionally not promised. They should receive explicit contracts and compatibility tests before becoming part of the common API. Additive result properties can preserve existing constructors; avoid changing positional record parameters or existing interface signatures in a patch/minor release. All four packages use one version while their dependency graph evolves.
