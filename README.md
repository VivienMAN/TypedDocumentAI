# TypedDocumentAI

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/) [![MIT](https://img.shields.io/badge/license-MIT-0f766e)](LICENSE)

Extract typed C# objects from documents, or read their text, with independently selectable Mistral and OpenAI adapters. Define an `Invoice`, send a PDF or image, and get an `ExtractionResult<Invoice>` whose shape is validated locally.

[Français](README.fr.md) · [Architecture](docs/ARCHITECTURE.md) · [Provider differences](#choose-a-provider) · [Security](SECURITY.md)

This is an unofficial community library. Provider accounts, API access and usage charges are separate from the MIT-licensed package.

## Try it without an API key

Clone this repository, install the .NET 10 SDK and run:

```sh
dotnet run --project samples/OfflineInvoiceDemo
```

The demo registers a custom provider returning a fixed synthetic response. The real client generates a schema, validates that response and produces an `Invoice`. It performs no OCR, inference or provider request.

```text
OFFLINE DEMO: fixed synthetic response, no OCR, network or paid API call.
{
  "Number": "INV-001",
  "Total": 125.50,
  "Date": "2026-10-01"
}
```

.NET may restore dependencies on the first run; the demo itself makes no network request. JSON numeric formatting may omit trailing zeroes.

## Install and extract a document

**Runtime requirement: .NET 10.** Install only the adapter you need; Core and Abstractions arrive as dependencies. Python is needed only for repository verification, never by your application.

The following NuGet commands apply after the first publication. Until then, use the repository demos or project references. Check [releases](https://github.com/VivienMAN/TypedDocumentAI/releases) for publication status.

```sh
dotnet new console -n InvoiceDemo -f net10.0
cd InvoiceDemo
dotnet add package TypedDocumentAI.Mistral
```

Set `MISTRAL_API_KEY` in your server-side environment or secret store, then replace `Program.cs` with this complete example. Do not put credentials in source code.

<!-- compile:quickstart -->
```csharp
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TypedDocumentAI;
using TypedDocumentAI.Mistral;

if (args.Length != 2 || args[1] != "--live")
{
    Console.Error.WriteLine("Usage: dotnet run -- <invoice.png|invoice.pdf> --live");
    Console.Error.WriteLine("--live sends your document to Mistral and may incur API charges.");
    return 2;
}

var services = new ServiceCollection();
services.AddDocumentAI().AddMistral(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable("MISTRAL_API_KEY")
        ?? throw new InvalidOperationException("Set MISTRAL_API_KEY in your environment.");
});
await using var container = services.BuildServiceProvider();
var client = container.GetRequiredService<IDocumentClient>();
var document = await DocumentInput.FromFileAsync(args[0]);
var result = await client.ExtractAsync<Invoice>(document);
Console.WriteLine(JsonSerializer.Serialize(result.Value,
    new JsonSerializerOptions { WriteIndented = true }));
return 0;

public sealed class Invoice
{
    [Description("Invoice number exactly as printed, or null when absent")]
    public string? Number { get; init; }
    [Description("Printed final total including taxes, or null when absent")]
    public decimal? Total { get; init; }
    [Description("Invoice date in YYYY-MM-DD format, or null when absent")]
    public DateOnly? Date { get; init; }
}
```
<!-- /compile:quickstart -->

Run it with a synthetic image or PDF:

```sh
dotnet run -- invoice.png --live
```

This sends the file to Mistral and may incur charges. Nullable fields represent missing information. A schema-valid object still requires your business validation; it does not prove that the model read the right total.

For OpenAI, install `TypedDocumentAI.OpenAI`, set `OPENAI_API_KEY` and follow its [complete quickstart](docs/nuget/OpenAI.md). You need only one provider key for either example.

## Choose a provider

| Behavior | Mistral | OpenAI |
| --- | --- | --- |
| Typed extraction | OCR document annotations | Responses structured output |
| `ReadAsync` | Native OCR Markdown | Generative text transcription |
| PDF, PNG, JPEG, WEBP | Supported by this adapter | Supported by this adapter |
| Actual page indices | Available | Not supplied; `Pages` is empty |
| Page selection, header/footer options | `MistralRequestOptions` | Not exposed |
| Confidence, geometry, batch processing | Not exposed | Not exposed |

These describe this library's adapters, not every upstream feature. [Provider contracts](docs/UPSTREAM-CONTRACTS.md) list the official references.

The defaults are configurable: `mistral-ocr-latest` for Mistral and `gpt-4.1-mini` for OpenAI. Provider availability and limits can change.

Register several providers only when you need them. Choose one explicitly with `ExtractionOptions.ProviderName`, or configure `DefaultProviderName`; there is no automatic cross-provider fallback. Provider-specific options stay in their own packages.

## Package family

| Package | Intended use |
| --- | --- |
| `TypedDocumentAI.Abstractions` | Provider interfaces, input snapshots, options and result types; no third-party dependencies. |
| `TypedDocumentAI.Core` | Typed client, schema validation, routing, DI and reusable HTTP transport. |
| `TypedDocumentAI.Mistral` | Mistral registration and adapter. |
| `TypedDocumentAI.OpenAI` | OpenAI registration and adapter; Azure OpenAI is not supported. |

Implement `IOcrProvider`, `IStructuredDocumentProvider`, or both for a new engine. See [extending the library](docs/EXTENDING.md).

## Limits and failure handling

- .NET 10 only. NativeAOT and trimming are not supported by the default reflection-based schema engine.
- Documents are buffered snapshots: 20 MiB input, 16 MiB response and a two-minute HTTP deadline by default. Base64 and concurrent requests require additional memory.
- POST retries are off by default because replay can duplicate charges. Unsupported settings, refusals, incomplete responses and malformed data are reported as errors.
- Schemas support concrete classes/records, writable or `init` properties, nested objects, generic collections, scalar/date types and string enums. Dictionaries, cycles, polymorphism and arbitrary JSON Schema are excluded.
- Library errors and tracing omit document bodies and credentials. OpenAI's `store: false` is not a zero-retention guarantee.

See [schemas](docs/SCHEMAS.md), [operations](docs/OPERATIONS.md) and [security](SECURITY.md) for the full contracts.

## Demos and documentation

| Example | What it demonstrates |
| --- | --- |
| [OfflineInvoiceDemo](samples/OfflineInvoiceDemo/Program.cs) | Complete typed extraction with a fixed synthetic response; no provider key. |
| [CustomProviderDemo](samples/CustomProviderDemo/Program.cs) | A minimal `text/plain` provider and `ReadAsync`; no OCR. |
| [InvoiceConsole](samples/InvoiceConsole/Program.cs) | Real Mistral/OpenAI reading and extraction; requires `--live` and one key. |

The [synthetic invoice](samples/InvoiceConsole/Fixtures/invoice.png) contains `INV-001`, `2026-10-01` and a total of `125.50`. Live results can differ; verify them.

```sh
# Each command sends the synthetic image and may incur charges:
dotnet run --project samples/InvoiceConsole -- mistral samples/InvoiceConsole/Fixtures/invoice.png extract --live
dotnet run --project samples/InvoiceConsole -- openai samples/InvoiceConsole/Fixtures/invoice.png extract --live
```

## Contribute and get help

Maintained by [VivienMAN](https://github.com/VivienMAN) and contributors. Use [issues](https://github.com/VivienMAN/TypedDocumentAI/issues) for reproducible bugs and focused feature requests; support is best effort. For security defects, follow [SECURITY.md](SECURITY.md) and do not post sensitive material publicly.

```sh
python tools/verify.py
```

This runs compilation, offline tests, API/visibility checks, all offline demos, NuGet packaging, local-feed installation and compilation of the documented quickstarts. See [testing](docs/TESTING.md), [actual validation status](docs/VALIDATION.md), [contributing](CONTRIBUTING.md), [releasing](docs/RELEASING.md) and [prototype migration](docs/MIGRATING-V1.md).

CI and publishing are manual. Live provider calls are separate opt-in operations. MIT license applies to this source; restored dependencies and provider services have their own terms.
