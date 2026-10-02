# Portable typed schema contract

## One serializer contract

The default `SystemTextJsonDocumentSchema` uses .NET's JSON Schema exporter and a read-only `JsonSerializerOptions` instance for deserialization. Property names are case-sensitive, enums use strings, unknown members are disallowed and nullable annotations are respected where System.Text.Json supports them. Schemas are cached per C# type in a concurrent cache.

`[Description]` contributes field guidance. `[JsonPropertyName]` changes the wire key. `[JsonIgnore]` removes a property according to the serializer contract. Every emitted object property is in `required`; optional information is represented as an explicit nullable value, not an omitted field. Prefer nullable fields whenever a document might not contain an answer.

## Supported types

Concrete root classes or records with readable, writable or `init` properties; nested concrete objects; one-dimensional generic arrays/collections; strings, booleans, standard numeric primitives, decimal, char, Guid, Uri and standard date/time types; non-flags enums represented as strings.

Get-only properties, fields, arbitrary `object`, dictionaries (including read-only dictionary interfaces), polymorphism, abstract object shapes, cycles, custom converters, flags enums and ambiguous/overly deep graphs are rejected before a provider call. Constructors must be compatible with System.Text.Json. The library does not execute a trial deserialization before calling a provider, so unusual constructor failures can still occur after an API request.

Generic collection element reference nullability is not fully enforceable through runtime System.Text.Json metadata. Do not rely on `List<string>` versus `List<string?>` alone as a complete element-nullability guarantee. Validate domain constraints explicitly after extraction.

## Local validation

The portable subset includes object/array/scalar/null types, enum alternatives, `anyOf`, properties and array items. Every object member is mandatory and extra or duplicate keys are rejected locally. Deserialization then enforces C# conversion, including number range and date representations. Invalid data raises a safe `DocumentResponseException` rather than a partially populated result.

Internal schema references emitted by .NET are expanded; external and recursive references are rejected. Root objects cannot be null. The schema graph is bounded and the serialized schema is limited to 65,536 characters. Format annotations become natural-language guidance to reduce differences between providers. Arbitrary JSON Schema constraints, regular expressions, string/array lengths and DataAnnotations business validation are not implemented by the local shape validator.

The validator is **not** a general-purpose JSON Schema engine. There is no public arbitrary-schema extraction method in this release line. Provider platforms impose additional limits on schema depth, field count and unsupported keywords; a locally valid schema can still be rejected upstream.

## Accuracy and trust

A valid integer is not necessarily the right amount. Extraction prompts tell the model to avoid inventing values and to treat document instructions as untrusted, but prompts are not a security boundary. Validate accounting rules, dates, identities and required business fields separately. Do not use raw extracted values to authorize payments, execute commands or update privileged records without appropriate application checks.

The default implementation uses reflection and has no NativeAOT/trimming compatibility claim. It is not a source generator.
