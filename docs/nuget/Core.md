# TypedDocumentAI.Core

Typed document client, schema validation, provider routing, Microsoft dependency injection and reusable HTTP transport for **.NET 10**. Core has no dependency on either Mistral or OpenAI; register an adapter or your own provider to process documents.

## Install

```sh
dotnet add package TypedDocumentAI.Core
```

For Mistral/OpenAI integration, install that adapter instead; Core and Abstractions arrive as dependencies. Python is not required by consumers.

## Register a custom provider

```csharp
using Microsoft.Extensions.DependencyInjection;
using TypedDocumentAI;

// SyntheticInvoiceProvider is the complete fixed-response provider in the linked demo.
var services = new ServiceCollection();
services.AddDocumentAI().AddProvider<SyntheticInvoiceProvider>();
await using var container = services.BuildServiceProvider();
var client = container.GetRequiredService<IDocumentClient>();
```

The complete [OfflineInvoiceDemo](https://github.com/VivienMAN/TypedDocumentAI/blob/main/samples/OfflineInvoiceDemo/Program.cs) generates a schema, validates fixed synthetic JSON and constructs an `Invoice` without a key or provider request. Clone the repository and run `dotnet run --project samples/OfflineInvoiceDemo`. This is a simulated response, not an OCR engine.

With multiple providers, configure `DefaultProviderName` or select `ProviderName` per call. There is no automatic fallback. Providers and the client are singletons; custom implementations must be thread-safe. `HttpDocumentProvider` is an optional documented extension base, not a requirement for local engines.

## Schema and operational limits

The reflection-based schema engine supports concrete classes/records with writable or `init` properties, nested objects, generic collections, scalar/date types and string enums. It rejects dictionaries, cycles, get-only properties and polymorphism. It does not claim NativeAOT/trimming support. Nullable fields represent unavailable information; shape validation does not certify factual accuracy.

HTTP defaults are 20 MiB input, 16 MiB response, a two-minute deadline and no POST retries. Documents are buffered in memory. Providers receive document content; library logs and errors omit it. Custom transports must retain equivalent controls.

## Documentation and support

[Architecture](https://github.com/VivienMAN/TypedDocumentAI/blob/main/docs/ARCHITECTURE.md) · [Schemas](https://github.com/VivienMAN/TypedDocumentAI/blob/main/docs/SCHEMAS.md) · [Extension API](https://github.com/VivienMAN/TypedDocumentAI/blob/main/docs/EXTENDING.md) · [Operations](https://github.com/VivienMAN/TypedDocumentAI/blob/main/docs/OPERATIONS.md) · [Security](https://github.com/VivienMAN/TypedDocumentAI/blob/main/SECURITY.md) · [Issues](https://github.com/VivienMAN/TypedDocumentAI/issues).

Maintained by [VivienMAN](https://github.com/VivienMAN) and contributors; support is best effort. MIT licensed; dependency licenses apply independently. Source Link and portable symbols are included.
