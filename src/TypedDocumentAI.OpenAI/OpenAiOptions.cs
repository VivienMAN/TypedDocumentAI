using TypedDocumentAI.Http;

namespace TypedDocumentAI.OpenAI;

/// <summary>Configuration for the OpenAI Responses API document adapter, not the Azure OpenAI API.</summary>
public sealed class OpenAiOptions : HttpProviderOptions
{
    /// <summary>Creates defaults for a vision-capable structured-output model. An API key is still required.</summary>
    public OpenAiOptions()
    {
        BaseAddress = new Uri("https://api.openai.com/v1/");
        Model = "gpt-4.1-mini";
    }

    /// <summary>Default maximum response tokens. Incomplete responses are errors, not partial successes.</summary>
    public int MaxOutputTokens { get; set; } = 8192;

    /// <inheritdoc />
    public override IReadOnlyList<string> GetValidationErrors()
    {
        var errors = base.GetValidationErrors().ToList();
        if (MaxOutputTokens is < 1 or > 131072)
        {
            errors.Add("MaxOutputTokens must be between 1 and 131072; the selected model may impose a lower limit.");
        }
        return errors.AsReadOnly();
    }
}

/// <summary>OpenAI-specific per-call settings. Mistral settings are deliberately not accepted.</summary>
public sealed record OpenAiRequestOptions : ProviderRequestOptions
{
    /// <summary>Optional response-token limit override. The selected model must support it.</summary>
    public int? MaxOutputTokens { get; init; }
}
