using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace TypedDocumentAI;

/// <summary>Reflection-based, strict object schemas backed by the same System.Text.Json contract used for deserialization.</summary>
/// <remarks>NativeAOT and trimming are not supported by this default implementation. Replace IDocumentSchema for that scenario.</remarks>
public sealed partial class SystemTextJsonDocumentSchema : IDocumentSchema
{
    private static readonly HashSet<Type> Scalars =
    [
        typeof(string), typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double),
        typeof(decimal), typeof(char), typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly),
        typeof(TimeOnly), typeof(TimeSpan), typeof(Guid), typeof(Uri)
    ];
    private readonly ConcurrentDictionary<Type, JsonElement> _cache = new();
    private readonly JsonSerializerOptions _options;

    /// <summary>Creates an immutable serialization contract with string enums and strict unknown-member handling.</summary>
    public SystemTextJsonDocumentSchema()
    {
        _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            MaxDepth = 64
        };
        _options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        _options.MakeReadOnly();
    }

    /// <inheritdoc />
    public JsonElement Create<T>() where T : class => _cache.GetOrAdd(typeof(T), CreateSchema);

    /// <inheritdoc />
    public T Deserialize<T>(JsonElement data, JsonElement schema) where T : class
    {
        ValidateShape(data, schema);
        try
        {
            return data.Deserialize<T>(_options)
                ?? throw new DocumentResponseException("The provider returned null instead of a document object.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or FormatException)
        {
            // JsonException can contain response values. Do not retain it as an inner exception.
            throw new DocumentResponseException("The extracted JSON cannot be converted to the requested C# type.");
        }
    }

    private JsonElement CreateSchema(Type type)
    {
        try
        {
            if (!type.IsClass || type.IsAbstract || type == typeof(string) ||
                _options.GetTypeInfo(type).Kind != JsonTypeInfoKind.Object)
            {
                throw new DocumentSchemaException("The root document type must be a concrete object class or record.");
            }
            ValidateGraph(type, []);
            var node = _options.GetJsonSchemaAsNode(type, new JsonSchemaExporterOptions
            {
                TransformSchemaNode = static (context, schema) =>
                {
                    if (schema is JsonObject obj && context.PropertyInfo?.AttributeProvider is ICustomAttributeProvider attributes)
                    {
                        var description = attributes.GetCustomAttributes(typeof(DescriptionAttribute), true)
                            .OfType<DescriptionAttribute>().FirstOrDefault()?.Description;
                        if (!string.IsNullOrWhiteSpace(description))
                        {
                            obj["description"] = description;
                        }
                    }
                    return schema;
                }
            });
            var portable = NormalizeSchema(node);
            return JsonSerializer.SerializeToElement(portable);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            throw new DocumentSchemaException("The document type cannot be represented by the portable schema contract.");
        }
    }

    private void ValidateGraph(Type type, HashSet<Type> path)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.ContainsGenericParameters || type.GetCustomAttribute<JsonConverterAttribute>() is not null)
        {
            throw new DocumentSchemaException("Open generic types and custom JSON converters are not supported.");
        }
        if (Scalars.Contains(type))
        {
            return;
        }
        if (type.IsEnum)
        {
            if (type.IsDefined(typeof(FlagsAttribute), false))
            {
                throw new DocumentSchemaException("Flags enums are not supported by the portable schema contract.");
            }
            return;
        }
        if (path.Count >= 32 || !path.Add(type))
        {
            throw new DocumentSchemaException("Recursive or excessively deep document types are not supported.");
        }
        try
        {
            var info = _options.GetTypeInfo(type);
            if (info.Kind == JsonTypeInfoKind.Enumerable)
            {
                var elementType = type.IsArray ? type.GetElementType() :
                    type.GetInterfaces().Append(type)
                        .FirstOrDefault(candidate => candidate.IsGenericType &&
                            candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
                if (elementType is null || (type.IsArray && type.GetArrayRank() != 1))
                {
                    throw new DocumentSchemaException("Only one-dimensional, generic document collections are supported.");
                }
                ValidateGraph(elementType, path);
                return;
            }
            if (info.Kind != JsonTypeInfoKind.Object || type.IsAbstract || type.IsInterface ||
                info.PolymorphismOptions is not null)
            {
                throw new DocumentSchemaException("Dictionaries, object, polymorphism and abstract document members are not supported.");
            }
            var members = info.Properties.Where(property => property.Get is not null || property.Set is not null).ToArray();
            if (members.Length == 0)
            {
                throw new DocumentSchemaException("Document objects must expose at least one serializable property.");
            }
            foreach (var member in members)
            {
                if (member.IsExtensionData || member.CustomConverter is not null ||
                    member.Get is null || member.Set is null ||
                    member.AttributeProvider is FieldInfo)
                {
                    throw new DocumentSchemaException("Use readable, writable or init-only properties without custom converters or extension data.");
                }
                ValidateGraph(member.PropertyType, path);
            }
        }
        finally
        {
            path.Remove(type);
        }
    }
}
