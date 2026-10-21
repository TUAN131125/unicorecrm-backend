using System.Text.Json;
using System.Xml.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace UnicoreCRM.ArchitectureTests;

internal static class ArchitectureInventory
{
    internal static string Backend { get; } = FindBackend();
    internal static string Control => Path.Combine(Directory.GetParent(Backend)!.FullName, ".unicore-ai");
    internal static JsonElement Manifest(string name) => JsonDocument.Parse(File.ReadAllText(
        File.Exists(Path.Combine(Backend, "architecture", name)) ? Path.Combine(Backend, "architecture", name) : Path.Combine(Control, "manifests", name))).RootElement.Clone();
    internal static string[] Projects => XDocument.Load(Path.Combine(Backend, "UnicoreCRM.slnx")).Descendants("Project")
        .Select(p => p.Attribute("Path")!.Value).Where(p => p.StartsWith("src/", StringComparison.Ordinal)).ToArray();
    internal static IEnumerable<TypeDefinition> Types()
    {
        foreach (var project in Projects)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(AppContext.BaseDirectory, Path.GetFileNameWithoutExtension(project) + ".dll"));
            foreach (var type in assembly.MainModule.GetTypes().Where(t => t.Name != "<Module>")) yield return type;
        }
    }
    internal static IEnumerable<TypeReference> References(TypeDefinition type)
    {
        if (type.BaseType is { } parent) yield return parent;
        foreach (var i in type.Interfaces) yield return i.InterfaceType;
        foreach (var f in type.Fields) yield return f.FieldType;
        foreach (var a in type.CustomAttributes) yield return a.AttributeType;
        foreach (var m in type.Methods)
        {
            yield return m.ReturnType;
            foreach (var p in m.Parameters) yield return p.ParameterType;
            if (!m.HasBody) continue;
            foreach (var v in m.Body.Variables) yield return v.VariableType;
            foreach (var instruction in m.Body.Instructions)
            {
                if (instruction.Operand is TypeReference t) yield return t;
                if (instruction.Operand is MethodReference method) yield return method.DeclaringType;
                if (instruction.Operand is FieldReference field) yield return field.DeclaringType;
            }
        }
    }
    internal static IEnumerable<TypeReference> Expand(TypeReference type)
    {
        yield return type;
        if (type is GenericInstanceType generic)
            foreach (var argument in generic.GenericArguments)
                foreach (var nested in Expand(argument)) yield return nested;
        else if (type is TypeSpecification specification)
            foreach (var nested in Expand(specification.ElementType)) yield return nested;
    }
    internal static string Namespace(TypeReference type) => type.DeclaringType is { } outer ? Namespace(outer) : type.Namespace;
    internal static bool Under(string value, string prefix) => value == prefix || value.StartsWith(prefix + ".", StringComparison.Ordinal);
    internal static string? Owner(string ns)
    {
        foreach (var module in Manifest("module-map.json").GetProperty("modules").EnumerateArray())
            foreach (var key in new[] { "domainNamespace", "applicationNamespace", "infrastructureNamespace", "apiNamespace" })
                if (module.GetProperty(key).ValueKind == JsonValueKind.String && Under(ns, module.GetProperty(key).GetString()!)) return module.GetProperty("owner").GetString();
        return null;
    }
    internal static string? Layer(string ns) => new[] { "Domain", "Application", "Infrastructure", "Contracts" }.FirstOrDefault(l => ns.Split('.').Contains(l, StringComparer.Ordinal));
    internal static bool ForbiddenLayer(string source, string target) =>
        (source == "Domain" && (target.Contains(".Infrastructure", StringComparison.Ordinal) || target.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) || target.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)))
        || (source == "Application" && target.Contains(".Infrastructure", StringComparison.Ordinal));
    internal static bool ContractAllowed(string edge) => Manifest("dependency-allowlist.json").GetProperty("approvedCrossOwnerEdges")
        .EnumerateArray().Any(item => item.GetString() == edge);
    internal static bool HasCycle(IReadOnlyDictionary<string, string[]> graph)
    {
        var active = new HashSet<string>(); var visited = new HashSet<string>();
        bool Visit(string node)
        {
            if (active.Contains(node)) return true;
            if (!visited.Add(node)) return false;
            active.Add(node);
            if (graph.TryGetValue(node, out var edges) && edges.Any(Visit)) return true;
            active.Remove(node); return false;
        }
        return graph.Keys.Any(Visit);
    }
    internal static IEnumerable<JsonElement> AdmittedOperations() => Manifest("operations/index.json").GetProperty("operations").EnumerateObject()
        .Select(p => Manifest(p.Value.GetString()!.Replace("manifests/", "", StringComparison.Ordinal)));
    internal static IReadOnlyDictionary<string, JsonElement> EffectiveOperations()
    {
        var authority = Path.Combine(Directory.GetParent(Backend)!.FullName, "design-authority", "canonical-design", "authority");
        using var operations = JsonDocument.Parse(File.ReadAllText(Path.Combine(authority, "operation-registry.json")));
        using var extensions = JsonDocument.Parse(File.ReadAllText(Path.Combine(authority, "project-extensions.json")));
        return AuthorityResolver.Resolve(AdmittedOperations(), operations.RootElement, extensions.RootElement);
    }
    internal static string ResolveMigrationPath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            throw new InvalidDataException("Migration path must be Backend-relative.");
        var resolved = Path.GetFullPath(Path.Combine(Backend, relative));
        var within = Path.GetRelativePath(Backend, resolved);
        if (within == ".." || within.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(within)) throw new InvalidDataException("Migration path escapes Backend.");
        return resolved;
    }
    private static string FindBackend()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "UnicoreCRM.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Cannot locate the actual Backend solution.");
    }
}
