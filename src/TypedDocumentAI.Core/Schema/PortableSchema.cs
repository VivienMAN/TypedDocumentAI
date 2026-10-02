using System.Globalization;
using System.Text.Json.Nodes;

namespace TypedDocumentAI;

internal static class PortableSchema
{
    internal static JsonObject Normalize(JsonNode root)
    {
        var normalized = Visit(root, root, [], 0);
        if (normalized["properties"] is not JsonObject)
        {
            throw new DocumentSchemaException("The root schema must describe an object.");
        }
        // Both adapters require a non-null object root, even when the exporter marks reference types nullable.
        normalized["type"] = "object";
        if (normalized.ToJsonString().Length > 65536)
        {
            throw new DocumentSchemaException("The generated schema exceeds the 64 KiB character limit.");
        }
        return normalized;
    }

    private static JsonObject Visit(JsonNode node, JsonNode root, HashSet<string> references, int depth)
    {
        if (depth > 32 || node is not JsonObject source)
        {
            throw new DocumentSchemaException("The generated schema is ambiguous or excessively deep.");
        }
        if (source["$ref"] is JsonValue referenceNode && referenceNode.TryGetValue<string>(out var reference))
        {
            if (!reference.StartsWith("#/", StringComparison.Ordinal) || !references.Add(reference))
            {
                throw new DocumentSchemaException("Recursive and external JSON Schema references are not supported.");
            }
            try
            {
                var expanded = Visit(Resolve(root, reference), root, references, depth + 1);
                if (source["description"] is JsonValue description)
                {
                    expanded["description"] = description.DeepClone();
                }
                return expanded;
            }
            finally
            {
                references.Remove(reference);
            }
        }
        var target = new JsonObject();
        foreach (var key in new[] { "type", "description", "enum" })
        {
            if (source[key] is { } value)
            {
                target[key] = value.DeepClone();
            }
        }
        // Provider subsets differ on format/pattern/bounds. .NET conversion still validates dates, numbers and chars.
        if (source["format"] is JsonValue format && format.TryGetValue<string>(out var formatText))
        {
            var description = target["description"]?.GetValue<string>();
            target["description"] = $"{description} Expected string format: {formatText}.".Trim();
        }
        if (source["anyOf"] is JsonArray alternatives)
        {
            var normalizedAlternatives = new JsonArray();
            foreach (var alternative in alternatives)
            {
                normalizedAlternatives.Add(Visit(alternative!, root, references, depth + 1));
            }
            target["anyOf"] = normalizedAlternatives;
        }
        if (source["properties"] is JsonObject properties)
        {
            var normalizedProperties = new JsonObject();
            var required = new JsonArray();
            foreach (var (name, property) in properties)
            {
                normalizedProperties[name] = Visit(property!, root, references, depth + 1);
                required.Add(name);
            }
            target["properties"] = normalizedProperties;
            target["required"] = required;
            target["additionalProperties"] = false;
        }
        if (source["items"] is { } items)
        {
            target["items"] = Visit(items, root, references, depth + 1);
        }
        if (target["type"] is null && target["anyOf"] is null && target["enum"] is null)
        {
            throw new DocumentSchemaException("An unconstrained JSON value is not a supported extraction type.");
        }
        return target;
    }

    private static JsonNode Resolve(JsonNode root, string reference)
    {
        JsonNode? current = root;
        foreach (var encoded in reference[2..].Split('/'))
        {
            var token = encoded.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            current = current switch
            {
                JsonObject obj => obj[token],
                JsonArray array when int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
                    index >= 0 && index < array.Count => array[index],
                _ => null
            };
        }
        return current ?? throw new DocumentSchemaException("The generated schema contains an unresolved reference.");
    }
}
