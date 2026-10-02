# TypedDocumentAI

**Typed document extraction and document text reading for .NET 10, with independent Mistral and OpenAI adapters.**

[Français](README.fr.md) · [Architecture](docs/ARCHITECTURE.md) · [Add a provider](docs/EXTENDING.md) · [V1 migration](docs/MIGRATING-V1.md)

> **First release target: `1.0.0`.** Local Linux verification passed: compilation, 124 offline C# tests and creation of the four NuGet packages with symbols. See [validation status](docs/VALIDATION.md). GitHub Actions and paid provider tests have not been run; NuGet publication awaits account setup. [Manual publishing guide](docs/RELEASING.md).

## One client, explicit capabilities

Define the C# shape of a document, select a provider, and receive a validated typed result. Raw text reading is a separate operation. Providers do not need to support both operations, and the client never silently sends your document to a different provider.

| Package | Responsibility |
| --- | --- |
| `TypedDocumentAI.Abstractions` | Inputs, options, results and small provider contracts. No third-party dependencies. |
| `TypedDocumentAI.Core` | Routing, schemas, validation, DI, diagnostics and optional HTTP infrastructure. |
| `TypedDocumentAI.Mistral` | Mistral OCR and structured document annotations. |
| `TypedDocumentAI.OpenAI` | OpenAI Responses multimodal transcription and structured extraction. |

There is no dependency from Core to either provider. Install only the adapters you use. This is an unofficial community project, not an official Mistral or OpenAI SDK.

## Start from source

Requirements: .NET SDK 10 and Python 3.10+ for repository verification scripts. Application consumers do not need Python. Provider APIs require separately configured accounts, compatible models and API access.

```sh
python tools/verify.py
```

This restores and builds the solution, runs offline xUnit tests with coverage collection, executes the local custom-provider demo, and packs/checks four `.nupkg` and four `.snupkg` files under `artifacts/packages/`. A failing step stops the command. No paid AI call is part of normal verification.

For an existing application before publication, add source project references:

```sh
dotnet add YourApp.csproj reference path/to/TypedDocumentAI/src/TypedDocumentAI.Mistral/TypedDocumentAI.Mistral.csproj
dotnet add YourApp.csproj reference path/to/TypedDocumentAI/src/TypedDocumentAI.OpenAI/TypedDocumentAI.OpenAI.csproj
```

### Register providers

In an application that uses Microsoft dependency injection:

```csharp
using TypedDocumentAI;
using TypedDocumentAI.Mistral;
using TypedDocumentAI.OpenAI;

services.AddDocumentAI(options => options.DefaultProviderName = "mistral")
    .AddMistral(options =>
    {
        options.ApiKey = Environment.GetEnvironmentVariable("MISTRAL_API_KEY")
            ?? throw new InvalidOperationException("MISTRAL_API_KEY is missing.");
    })
    .AddOpenAI(options =>
    {
        options.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY is missing.");
    });
```

Register only one adapter when only one key is available. With exactly one provider, no default name is needed. With several providers, configure a default or select a name on each call. Provider instances and the common client are singletons: custom providers must be thread-safe.

### Extract a typed document

```csharp
using System.ComponentModel;
using TypedDocumentAI;

public sealed class Invoice
{
    [Description("Invoice number exactly as printed, or null when absent.")]
    public string? Number { get; init; }

    [Description("Final invoice total including taxes, or null when absent.")]
    public decimal? Total { get; init; }

    [Description("Invoice date in YYYY-MM-DD format, or null when absent.")]
    public DateOnly? Date { get; init; }
}
```

Inject `IDocumentClient`, then:

```csharp
var document = await DocumentInput.FromFileAsync(
    "invoice.pdf", cancellationToken: cancellationToken);

var result = await client.ExtractAsync<Invoice>(
    document,
    new ExtractionOptions { ProviderName = "mistral" },
    cancellationToken);

Invoice invoice = result.Value;
string providerUsed = result.Metadata.ProviderName;
```

The same call accepts `ProviderName = "openai"`. Changing providers is an explicit caller decision, not automatic failover. Neither a valid schema nor successful deserialization proves that extracted values are correct; keep business validation and human review where needed.

### Read text and real page metadata

```csharp
var result = await client.ReadAsync(document,
    new DocumentRequestOptions
    {
        ProviderName = "mistral",
        ProviderOptions = new MistralRequestOptions
        {
            Pages = new[] { 0, 1 },
            ExtractHeaders = true,
            ExtractFooters = true
        }
    }, cancellationToken);

Console.WriteLine(result.Text);
```

Mistral page indices are zero-based. `Pages` preserves the page indices and order returned upstream, including selected pages. OpenAI returns document-level text without synthetic page boundaries.

| Capability in this release line | Mistral adapter | OpenAI adapter |
| --- | --- | --- |
| PDF, PNG, JPEG and WEBP input | Yes | Yes |
| Typed extraction from C# schema | OCR document annotations | Responses strict structured output |
| `ReadAsync` | Native OCR Markdown | Generative transcription |
| Page-indexed output | Upstream page indices | No, empty `Pages` |
| Page selection | `MistralRequestOptions.Pages` | Not exposed |
| Separate page header/footer | Optional | Not exposed |
| Free-form instructions for raw reading | Rejected | Supported |
| Confidence scores, bounding boxes, batch jobs | Not exposed | Not exposed |
| URL inputs, remote Files API resources | Not exposed | Not exposed |

This table describes these adapters, not every feature available on the upstream platforms. See [upstream contracts and primary sources](docs/UPSTREAM-CONTRACTS.md).

## Provider-specific settings stay provider-specific

```csharp
var result = await client.ExtractAsync<Invoice>(document,
    new ExtractionOptions
    {
        ProviderName = "openai",
        SchemaName = "invoice",
        Instructions = "Use the document's printed final total. Do not calculate missing amounts.",
        ProviderOptions = new OpenAiRequestOptions
        {
            Model = "gpt-4.1-mini",
            MaxOutputTokens = 4096
        }
    }, cancellationToken);
```

Passing Mistral settings to OpenAI or vice versa throws before HTTP. Unsupported options are not silently ignored. Mistral header/footer switches apply to `ReadAsync`, not to typed extraction. Models are configurable; availability, limits and charges are controlled by the provider.

## Reliability and data handling

Inputs are buffered snapshots, not streaming OCR. The default local/provider input limit is **20 MiB**, the response limit is **16 MiB**, and the HTTP operation deadline is **two minutes**. Base64, JSON serialization and concurrent requests require additional memory. [Operational details](docs/OPERATIONS.md) explain how to tune these limits and concurrency.

POST retries are **off by default**. Opt in deliberately with `MaxRetryAttempts`: a timeout or network failure can hide a successful paid request. Enabled retries use bounded exponential backoff and `Retry-After`; they never retry malformed JSON or refusals. There is no cross-provider retry/fallback.

The built-in HTTP setup requires HTTPS, disables redirects and cookies, and redacts request headers from factory logging. Documents and prompts are not written to library logs or exception messages. OpenAI requests set `store: false`, but this **does not establish zero data retention**. Both providers still receive the document. See [SECURITY.md](SECURITY.md).

## Schema contract and limits

Supported shapes are concrete classes/records with writable or `init` properties, nested objects, one-dimensional generic collections, common .NET scalar/date types and string enums. `[Description]`, `[JsonPropertyName]` and `[JsonIgnore]` are supported. All emitted object keys are required; missing information should use nullable values.

The default schema service rejects cycles, dictionaries, unconstrained `object`, polymorphism, get-only members, custom converters and flags enums. It uses reflection and does **not** claim NativeAOT/trimming support. Its local validator covers only the finite schema subset it emits, not arbitrary JSON Schema. See [schema contract](docs/SCHEMAS.md).

## Examples and repository guides

The [invoice console](samples/InvoiceConsole/Program.cs) requires an explicit `--live` flag and uses one selected API key. The [custom provider demo](samples/CustomProviderDemo/Program.cs) runs completely offline and proves plugin registration without pretending to be a third OCR engine.

```sh
# No credentials, network or paid API request:
dotnet run --project samples/CustomProviderDemo

# After setting MISTRAL_API_KEY; sends a synthetic invoice and may incur charges:
dotnet run --project samples/InvoiceConsole -- mistral samples/InvoiceConsole/Fixtures/invoice.png extract --live
```

Read [testing](docs/TESTING.md), [release setup](docs/RELEASING.md), [contributing](CONTRIBUTING.md) and [migration from V1](docs/MIGRATING-V1.md). MIT license for this source; upstream service terms and separately restored dependencies still apply.
