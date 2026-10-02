# TypedDocumentAI.Abstractions

Provider-neutral document contracts for **.NET 10**, with no third-party package dependencies. This package contains input snapshots, options, results and the interfaces used by the TypedDocumentAI client and provider adapters. It does not perform OCR or send requests by itself.

## Install

```sh
dotnet add package TypedDocumentAI.Abstractions
```

For actual Mistral or OpenAI extraction, install that provider package instead; it brings Abstractions and Core automatically. For a custom engine, implement `IOcrProvider` for text reading, `IStructuredDocumentProvider` for typed JSON extraction, or both. `IDocumentProvider` supplies the name and advertised capabilities.

## Input and output

```csharp
using TypedDocumentAI;

var document = await DocumentInput.FromFileAsync("invoice.png");
Console.WriteLine($"{document.FileName}: {document.ContentType}, {document.Length} bytes");
```

Creating an input reads the local file; it does not upload it. Inputs defensively copy provided bytes, buffer streams with a size limit, and never dispose the caller's stream. The default input limit is 20 MiB.

`ExtractionResult<T>` holds the typed value and real processing metadata. `OcrResult` holds text and available page data. Null metadata means unavailable; empty pages do not imply invented segmentation. Provider code must preserve cancellation, own the lifetime of its JSON results and keep sensitive content out of exceptions.

## Documentation and support

[Custom-provider guide](https://github.com/VivienMAN/TypedDocumentAI/blob/main/docs/EXTENDING.md) · [Minimal offline provider](https://github.com/VivienMAN/TypedDocumentAI/blob/main/samples/CustomProviderDemo/Program.cs) · [Typed offline demo](https://github.com/VivienMAN/TypedDocumentAI/blob/main/samples/OfflineInvoiceDemo/Program.cs) · [Security](https://github.com/VivienMAN/TypedDocumentAI/blob/main/SECURITY.md) · [Issues](https://github.com/VivienMAN/TypedDocumentAI/issues).

Maintained by [VivienMAN](https://github.com/VivienMAN) and contributors; support is best effort. MIT licensed. Source Link and portable symbols are included.
