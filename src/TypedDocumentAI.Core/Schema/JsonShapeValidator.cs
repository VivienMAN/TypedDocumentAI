using System.Globalization;
using System.Text.Json;

namespace TypedDocumentAI;

// Validates only the finite portable subset emitted by this schema engine, not arbitrary JSON Schema documents.
public sealed partial class SystemTextJsonDocumentSchema
{
    private static void ValidateShape(JsonElement data, JsonElement schema)
    {
        if (!MatchesShape(data, schema, 0))
        {
            throw new DocumentResponseException("The extracted JSON does not match the required document shape.");
        }
    }

    private static bool MatchesShape(JsonElement value, JsonElement schema, int depth)
    {
        if (depth > 64 || schema.ValueKind != JsonValueKind.Object || value.ValueKind == JsonValueKind.Undefined)
        {
            return false;
        }
        if (schema.TryGetProperty("anyOf", out var alternatives) &&
            !alternatives.EnumerateArray().Any(alternative => MatchesShape(value, alternative, depth + 1)))
        {
            return false;
        }
        if (schema.TryGetProperty("type", out var type))
        {
            var matches = type.ValueKind == JsonValueKind.Array
                ? type.EnumerateArray().Any(candidate => HasJsonType(value, candidate.GetString()))
                : HasJsonType(value, type.GetString());
            if (!matches)
            {
                return false;
            }
        }
        if (schema.TryGetProperty("enum", out var choices) &&
            !choices.EnumerateArray().Any(choice => JsonElement.DeepEquals(choice, value)))
        {
            return false;
        }
        if (value.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var properties))
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in value.EnumerateObject())
            {
                // Duplicate keys are ambiguous and are rejected, even when the values happen to agree.
                if (!names.Add(member.Name) || !properties.TryGetProperty(member.Name, out var memberSchema) ||
                    !MatchesShape(member.Value, memberSchema, depth + 1))
                {
                    return false;
                }
            }
            foreach (var member in properties.EnumerateObject())
            {
                if (!names.Contains(member.Name))
                {
                    return false;
                }
            }
        }
        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            return value.EnumerateArray().All(item => MatchesShape(item, items, depth + 1));
        }
        return true;
    }

    private static bool HasJsonType(JsonElement value, string? type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number &&
            decimal.TryParse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            decimal.Truncate(number) == number,
        _ => false
    };
}
