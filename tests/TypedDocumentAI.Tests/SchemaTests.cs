using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypedDocumentAI.Tests;

public sealed class SchemaTests
{
    private readonly SystemTextJsonDocumentSchema _schema = new();

    [Fact]
    public void Root_is_a_non_null_strict_object()
    {
        var schema = _schema.Create<Invoice>();
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "Number", "Total", "Date" },
            schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("Invoice number", schema.GetProperty("properties").GetProperty("Number").GetProperty("description").GetString());
    }

    [Fact]
    public void Valid_data_is_converted_to_the_requested_type()
    {
        using var json = JsonDocument.Parse(TestData.Annotation);
        var invoice = _schema.Deserialize<Invoice>(json.RootElement, _schema.Create<Invoice>());
        Assert.Equal("INV-001", invoice.Number);
        Assert.Equal(125.50m, invoice.Total);
        Assert.Equal(new DateOnly(2026, 10, 1), invoice.Date);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Number\":\"A\",\"Total\":1}")]
    [InlineData("{\"Number\":\"A\",\"Total\":1,\"Date\":null,\"Unexpected\":true}")]
    [InlineData("{\"Number\":\"A\",\"Total\":\"one\",\"Date\":null}")]
    [InlineData("{\"Number\":\"A\",\"Number\":\"B\",\"Total\":1,\"Date\":null}")]
    [InlineData("{\"number\":\"A\",\"Total\":1,\"Date\":null}")]
    [InlineData("null")]
    [InlineData("[]")]
    public void Missing_unknown_duplicate_or_wrongly_typed_values_are_rejected(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Throws<DocumentResponseException>(() => _schema.Deserialize<Invoice>(document.RootElement, _schema.Create<Invoice>()));
    }

    [Fact]
    public void Nullable_fields_accept_explicit_null()
    {
        using var json = JsonDocument.Parse("""{"Number":null,"Total":null,"Date":null}""");
        var result = _schema.Deserialize<Invoice>(json.RootElement, _schema.Create<Invoice>());
        Assert.Null(result.Total);
        Assert.Null(result.Number);
        Assert.Null(result.Date);
    }

    [Fact]
    public void Non_nullable_references_reject_null()
    {
        using var json = JsonDocument.Parse("""{"Name":null}""");
        Assert.Throws<DocumentResponseException>(() => _schema.Deserialize<RequiredName>(json.RootElement, _schema.Create<RequiredName>()));
    }

    [Fact]
    public void Invalid_dates_are_rejected_without_leaking_response_values()
    {
        using var json = JsonDocument.Parse("""{"Number":"SECRET","Total":1,"Date":"SECRET-DATE"}""");
        var exception = Assert.Throws<DocumentResponseException>(() => _schema.Deserialize<Invoice>(json.RootElement, _schema.Create<Invoice>()));
        Assert.DoesNotContain("SECRET", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Json_names_and_ignored_members_follow_the_serializer_contract()
    {
        var schema = _schema.Create<NamedDocument>();
        var properties = schema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("external_name", out _));
        Assert.False(properties.TryGetProperty("Name", out _));
        Assert.False(properties.TryGetProperty("Ignored", out _));
    }

    [Fact]
    public void Enums_nested_objects_and_arrays_are_supported()
    {
        using var json = JsonDocument.Parse("""{"State":"Open","Lines":[{"Label":"A","Quantity":2}]}""");
        var result = _schema.Deserialize<Order>(json.RootElement, _schema.Create<Order>());
        Assert.Equal(State.Open, result.State);
        Assert.Equal(2, Assert.Single(result.Lines).Quantity);
    }

    [Fact]
    public void Nested_missing_fields_are_not_replaced_with_dotnet_defaults()
    {
        using var json = JsonDocument.Parse("""{"State":"Open","Lines":[{"Label":"A"}]}""");
        Assert.Throws<DocumentResponseException>(() => _schema.Deserialize<Order>(json.RootElement, _schema.Create<Order>()));
    }

    [Theory]
    [InlineData("{\"State\":42,\"Lines\":[]}")]
    [InlineData("{\"State\":\"Unknown\",\"Lines\":[]}")]
    [InlineData("{\"State\":\"Open\",\"Lines\":[{\"Label\":\"A\",\"Quantity\":1.5}]}")]
    public void Unknown_enums_and_fractional_integers_are_rejected(string data)
    {
        using var json = JsonDocument.Parse(data);
        Assert.Throws<DocumentResponseException>(() => _schema.Deserialize<Order>(json.RootElement, _schema.Create<Order>()));
    }

    [Fact]
    public void Repeated_nested_types_are_expanded_without_reference_cycles()
    {
        var schema = _schema.Create<Repeated>();
        Assert.DoesNotContain("$ref", schema.GetRawText());
        Assert.True(schema.GetProperty("properties").GetProperty("Second").TryGetProperty("properties", out _));
    }

    [Fact]
    public void Cyclic_types_are_rejected() => Assert.Throws<DocumentSchemaException>(() => _schema.Create<Recursive>());

    [Fact]
    public void Self_referential_collections_are_rejected() => Assert.Throws<DocumentSchemaException>(() => _schema.Create<RecursiveListContainer>());

    [Fact]
    public void Dictionaries_are_rejected() => Assert.Throws<DocumentSchemaException>(() => _schema.Create<DictionaryDocument>());

    [Fact]
    public void Readonly_dictionary_interfaces_are_rejected() => Assert.Throws<DocumentSchemaException>(() => _schema.Create<ReadonlyDictionaryDocument>());

    [Fact]
    public void Unconstrained_object_members_are_rejected() => Assert.Throws<DocumentSchemaException>(() => _schema.Create<ObjectDocument>());

    [Fact]
    public void Get_only_properties_are_rejected() => Assert.Throws<DocumentSchemaException>(() => _schema.Create<GetOnlyDocument>());

    [Fact]
    public void String_roots_are_rejected() => Assert.Throws<DocumentSchemaException>(() => _schema.Create<string>());

    [Fact]
    public void Collection_roots_are_rejected() => Assert.Throws<DocumentSchemaException>(() => _schema.Create<List<Invoice>>());

    [Fact]
    public async Task Shared_schema_cache_is_safe_for_concurrent_requests()
    {
        var schemas = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => _schema.Create<Invoice>())));
        Assert.All(schemas, schema => Assert.Equal(schemas[0].GetRawText(), schema.GetRawText()));
    }

    public sealed class RequiredName { public string Name { get; init; } = string.Empty; }
    public sealed class NamedDocument
    {
        [JsonPropertyName("external_name")] public string? Name { get; init; }
        [JsonIgnore] public Dictionary<string, object> Ignored { get; init; } = [];
    }
    public enum State { Open, Closed }
    public sealed class Line { public string? Label { get; init; } public int Quantity { get; init; } }
    public sealed class Order { public State State { get; init; } public List<Line> Lines { get; init; } = []; }
    public sealed class Repeated { public Line? First { get; init; } public Line? Second { get; init; } }
    public sealed class Recursive { public Recursive? Child { get; init; } }
    public sealed class RecursiveList : List<RecursiveList> { }
    public sealed class RecursiveListContainer { public RecursiveList? Children { get; init; } }
    public sealed class DictionaryDocument { public Dictionary<string, string> Values { get; init; } = []; }
    public sealed class ReadonlyDictionaryDocument { public IReadOnlyDictionary<string, string>? Values { get; init; } }
    public sealed class ObjectDocument { public object? Value { get; init; } }
    public sealed class GetOnlyDocument { public string Value => "fixed"; }
}
