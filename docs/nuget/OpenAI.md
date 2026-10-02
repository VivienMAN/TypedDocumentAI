# TypedDocumentAI.OpenAI

OpenAI adapter for typed C# document extraction and text reading on **.NET 10**. Multimodal transcription and strict structured extraction through the Responses API; Azure OpenAI is not supported.

Install only this adapter; TypedDocumentAI.Core and TypedDocumentAI.Abstractions are included as dependencies. Python is not required by your application. This is an unofficial community library; an independent OpenAI account and API access are required, and requests may incur charges.

## Install

```sh
dotnet new console -n InvoiceDemo -f net10.0
cd InvoiceDemo
dotnet add package TypedDocumentAI.OpenAI
```

Set `OPENAI_API_KEY` in your server-side environment or secret store. Replace `Program.cs` with the following complete example. Credentials do not belong in source code.

<!-- compile:quickstart -->
```csharp
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TypedDocumentAI;
using TypedDocumentAI.OpenAI;

if (args.Length != 2 || args[1] != "--live")
{
    Console.Error.WriteLine("Usage: dotnet run -- <invoice.png|invoice.pdf> --live");
    Console.Error.WriteLine("--live sends your document to OpenAI and may incur API charges.");
    return 2;
}

var services = new ServiceCollection();
services.AddDocumentAI().AddOpenAI(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("Set OPENAI_API_KEY in your environment.");
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

```sh
dotnet run -- invoice.png --live
```

This sends the file to OpenAI. Example output shape using synthetic invoice data:

```json
{ "Number": "INV-001", "Total": 125.50, "Date": "2026-10-01" }
```

Real results may differ. Null represents missing information; validate business rules after extraction.

## Text reading and configuration

For text reading use `await client.ReadAsync(document)`. Configure model and HTTP limits during registration; per-call settings belong to `OpenAiRequestOptions`. Multiple providers require an explicit name or a configured default. There is no cross-provider fallback.

Inputs supported by this adapter: PDF, PNG, JPEG and WEBP. Defaults: 20 MiB input, 16 MiB response, two-minute HTTP deadline, no POST retries. The reflection-based schema engine does not support NativeAOT/trimming, dictionaries, cycles or polymorphism. Document content is buffered in memory. Schema validation does not guarantee extraction accuracy or immunity to prompt injection.

## Try without provider credentials

Clone the [repository](https://github.com/VivienMAN/TypedDocumentAI) and run `dotnet run --project samples/OfflineInvoiceDemo`. The [offline demo](https://github.com/VivienMAN/TypedDocumentAI/blob/main/samples/OfflineInvoiceDemo/Program.cs) returns a fixed synthetic response and uses the real schema/typed client. It makes no provider request and does not perform OCR.

## Documentation and support

[Provider comparison](https://github.com/VivienMAN/TypedDocumentAI#choose-a-provider) · [Schemas](https://github.com/VivienMAN/TypedDocumentAI/blob/main/docs/SCHEMAS.md) · [Operations](https://github.com/VivienMAN/TypedDocumentAI/blob/main/docs/OPERATIONS.md) · [Live example](https://github.com/VivienMAN/TypedDocumentAI/blob/main/samples/InvoiceConsole/Program.cs) · [Security](https://github.com/VivienMAN/TypedDocumentAI/blob/main/SECURITY.md).

Maintained by [VivienMAN](https://github.com/VivienMAN) and contributors, with best-effort support through [issues](https://github.com/VivienMAN/TypedDocumentAI/issues). MIT license applies to this source; dependencies and provider services have their own terms. Source Link and portable symbols are provided for debugging.
