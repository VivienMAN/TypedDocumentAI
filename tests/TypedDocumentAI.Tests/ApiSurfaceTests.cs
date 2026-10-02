using TypedDocumentAI.Http;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using TypedDocumentAI.Mistral;
using TypedDocumentAI.OpenAI;

namespace TypedDocumentAI.Tests;

public sealed class ApiSurfaceTests
{
    private static readonly Assembly[] Assemblies =
    [typeof(IDocumentClient).Assembly, typeof(DocumentClient).Assembly,
        typeof(MistralDocumentProvider).Assembly, typeof(OpenAiDocumentProvider).Assembly];
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void Implementation_methods_are_private_except_documented_transport_extensions()
    {
        string[] extensions = ["ValidateInput", "ResolveModel", "ToDataUri", "SendJsonAsync",
            "Require", "OptionalString", "OptionalCount", "ParseAnnotation"];
        var exceptions = Assemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute)))
            .SelectMany(type => type.GetMethods(Members))
            .Where(method => !method.IsPublic && !method.IsPrivate && !method.IsSpecialName &&
                !method.IsDefined(typeof(CompilerGeneratedAttribute)))
            .ToArray();
        Assert.All(exceptions, method =>
        {
            Assert.Equal(typeof(HttpDocumentProvider), method.DeclaringType);
            Assert.True(method.IsFamily, method.ToString());
        });
        Assert.Equal(extensions.Order(StringComparer.Ordinal),
            exceptions.Select(method => method.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Public_and_protected_api_matches_reviewed_baseline()
    {
        var actual = Snapshot();
        var baseline = Path.Combine(AppContext.BaseDirectory, "PublicAPI.txt");
        // Diagnostic output is ignored by git. Never update the reviewed baseline automatically.
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "PublicAPI.actual.txt"), actual);
        Assert.True(File.Exists(baseline), "Missing reviewed PublicAPI.txt baseline.");
        Assert.Equal(File.ReadAllText(baseline).Replace("\r\n", "\n"), actual);
    }

    private static bool Exposed(Type type) => type.IsPublic ||
        (type.DeclaringType is not null && Exposed(type.DeclaringType) &&
            (type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem));

    private static bool Exposed(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static string Visibility(MethodBase method) => method.IsPublic ? "public" :
        method.IsFamily ? "protected" : method.IsFamilyOrAssembly ? "protected internal" :
        method.IsPrivate ? "private" : "internal";

    private static string Parameter(ParameterInfo parameter, NullabilityInfoContext nullable) =>
        $"{parameter.ParameterType} {parameter.Name} [{nullable.Create(parameter).ReadState}]" +
        (parameter.IsOptional ? " = " + Convert.ToString(parameter.DefaultValue, CultureInfo.InvariantCulture) : "");

    private static string Snapshot()
    {
        var lines = new List<string>();
        var nullable = new NullabilityInfoContext();
        foreach (var type in Assemblies.SelectMany(assembly => assembly.GetTypes()).Where(Exposed))
        {
            var prefix = type.ToString();
            lines.Add($"TYPE {prefix}: visibility={type.Attributes & TypeAttributes.VisibilityMask}; base={type.BaseType}; abstract={type.IsAbstract}; sealed={type.IsSealed}; " +
                "interfaces=" + string.Join(",", type.GetInterfaces().Select(item => item.ToString()).Order(StringComparer.Ordinal)));
            foreach (var constructor in type.GetConstructors(Members).Where(Exposed))
                lines.Add($"{prefix}: {Visibility(constructor)} ctor({string.Join(", ", constructor.GetParameters().Select(p => Parameter(p, nullable)))})");
            foreach (var method in type.GetMethods(Members).Where(Exposed))
            {
                var constraints = method.IsGenericMethodDefinition
                    ? string.Join(";", method.GetGenericArguments().Select(argument =>
                        $"{argument.Name}:{argument.GenericParameterAttributes}:" +
                        string.Join(",", argument.GetGenericParameterConstraints().Select(item => item.ToString())))) : "";
                lines.Add($"{prefix}: {Visibility(method)} {method}; static={method.IsStatic}; virtual={method.IsVirtual}; " +
                    $"abstract={method.IsAbstract}; final={method.IsFinal}; extension={method.IsDefined(typeof(ExtensionAttribute))}; return={nullable.Create(method.ReturnParameter).ReadState}; " +
                    $"returnModifiers={string.Join(",", method.ReturnParameter.GetRequiredCustomModifiers().Select(item => item.ToString()))}; " +
                    $"parameters=({string.Join(", ", method.GetParameters().Select(p => Parameter(p, nullable)))}); constraints={constraints}");
            }
            foreach (var property in type.GetProperties(Members).Where(p => p.GetAccessors(true).Any(Exposed)))
                lines.Add($"{prefix}: property {property}; nullable={nullable.Create(property).ReadState}; " +
                    "accessors=" + string.Join(",", property.GetAccessors(true).Select(accessor => Visibility(accessor) + " " + accessor.Name)));
            foreach (var field in type.GetFields(Members).Where(f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly))
                lines.Add($"{prefix}: field {field}; static={field.IsStatic}; readonly={field.IsInitOnly}; " +
                    $"nullable={nullable.Create(field).ReadState}; constant=" +
                    (field.IsLiteral ? Convert.ToString(field.GetRawConstantValue(), CultureInfo.InvariantCulture) : "<none>"));
            foreach (var item in type.GetEvents(Members).Where(e => e.GetAddMethod(true) is { } method && Exposed(method)))
                lines.Add($"{prefix}: event {item}");
        }
        return string.Join('\n', lines.Order(StringComparer.Ordinal)) + "\n";
    }
}
