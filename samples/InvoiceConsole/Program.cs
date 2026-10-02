using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TypedDocumentAI;
using TypedDocumentAI.Mistral;
using TypedDocumentAI.OpenAI;

if (args.Length != 4 || args[3] != "--live" || args[0] is not ("mistral" or "openai") || args[2] is not ("read" or "extract"))
{
    Console.Error.WriteLine("Usage: InvoiceConsole <mistral|openai> <document-path> <read|extract> --live");
    Console.Error.WriteLine("The --live flag sends this document to the selected provider and may incur API charges.");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
try
{
    var providerName = args[0];
    var keyName = providerName == "mistral" ? "MISTRAL_API_KEY" : "OPENAI_API_KEY";
    var key = Environment.GetEnvironmentVariable(keyName);
    if (string.IsNullOrWhiteSpace(key))
    {
        Console.Error.WriteLine($"Set the {keyName} environment variable first.");
        return 2;
    }
    var services = new ServiceCollection();
    var builder = services.AddDocumentAI(options => options.DefaultProviderName = providerName);
    if (providerName == "mistral")
    {
        builder.AddMistral(options =>
        {
            options.ApiKey = key;
            if (Environment.GetEnvironmentVariable("MISTRAL_MODEL") is { Length: > 0 } model)
            {
                options.Model = model;
            }
        });
    }
    else
    {
        builder.AddOpenAI(options =>
        {
            options.ApiKey = key;
            if (Environment.GetEnvironmentVariable("OPENAI_MODEL") is { Length: > 0 } model)
            {
                options.Model = model;
            }
        });
    }
    await using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    var client = container.GetRequiredService<IDocumentClient>();
    var document = await DocumentInput.FromFileAsync(args[1], cancellationToken: cancellation.Token);
    var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
    if (args[2] == "extract")
    {
        var result = await client.ExtractAsync<Invoice>(document, cancellationToken: cancellation.Token);
        Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
    }
    else
    {
        var result = await client.ReadAsync(document, cancellationToken: cancellation.Token);
        Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
    }
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Operation cancelled.");
    return 130;
}
catch (DocumentException exception)
{
    Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
    return 1;
}
catch (OptionsValidationException)
{
    Console.Error.WriteLine("Provider configuration is invalid.");
    return 2;
}
catch (IOException)
{
    Console.Error.WriteLine("The local document could not be read.");
    return 2;
}
catch (UnauthorizedAccessException)
{
    Console.Error.WriteLine("Access to the local document was denied.");
    return 2;
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

public sealed class Invoice
{
    [Description("Invoice number exactly as printed")]
    public string? Number { get; init; }

    [Description("Total amount including taxes, as a JSON number, without calculating it")]
    public decimal? Total { get; init; }

    [Description("Invoice date, as YYYY-MM-DD")]
    public DateOnly? Date { get; init; }
}
