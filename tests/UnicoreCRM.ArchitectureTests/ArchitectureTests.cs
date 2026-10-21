using Xunit;
using static UnicoreCRM.ArchitectureTests.ArchitectureInventory;

namespace UnicoreCRM.ArchitectureTests;

public sealed class ArchitectureTests
{
    [Fact]
    public void All_fourteen_product_projects_are_present_and_inspected()
    {
        Assert.Equal(14, Projects.Length);
        foreach (var project in Projects)
        {
            Assert.True(File.Exists(Path.Combine(Backend, project)), project);
            Assert.True(Types().Any(t => t.Module.Assembly.Name.Name == Path.GetFileNameWithoutExtension(project)), project);
        }
    }

    [Fact]
    public void Layer_and_workflow_dependencies_obey_the_canonical_boundaries()
    {
        var errors = new List<string>();
        foreach (var type in Types())
        foreach (var reference in References(type).SelectMany(Expand))
        {
            var from = Namespace(type); var to = Namespace(reference);
            if (ForbiddenLayer(Layer(from) ?? "", to)) errors.Add($"Layer: {type.FullName} -> {reference.FullName}");
            if (Under(from, "UnicoreCRM.Workflows") && !Under(to, "UnicoreCRM.Workflows") && to.Contains(".Infrastructure.Persistence", StringComparison.Ordinal))
                errors.Add($"Workflow participant persistence: {type.FullName} -> {reference.FullName}");
            if (Under(from, "UnicoreCRM.BuildingBlocks") && to.StartsWith("UnicoreCRM.", StringComparison.Ordinal) && !Under(to, "UnicoreCRM.BuildingBlocks"))
                errors.Add($"BuildingBlocks business dependency: {type.FullName} -> {reference.FullName}");
        }
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Distinct()));
    }

    [Fact]
    public void Owner_mapping_is_complete_and_cross_owner_edges_are_explicit()
    {
        Assert.Equal("DENY_UNMAPPED", Manifest("module-map.json").GetProperty("defaultPolicy").GetString());
        Assert.Equal("DENY", Manifest("dependency-allowlist.json").GetProperty("defaultPolicy").GetString());
        var errors = new HashSet<string>();
        foreach (var type in Types().Where(t => Layer(Namespace(t)) is not null))
        {
            var from = Namespace(type); var owner = Owner(from);
            if (owner is null) errors.Add($"Unmapped source owner: {from}");
            foreach (var reference in References(type).SelectMany(Expand).Where(t => Namespace(t).StartsWith("UnicoreCRM.", StringComparison.Ordinal)))
            {
                var to = Namespace(reference); var targetOwner = Owner(to);
                if (Under(to, "UnicoreCRM.BuildingBlocks")) continue;
                if (targetOwner is null) { errors.Add($"Unmapped target owner: {to}"); continue; }
                if (owner is null || owner == targetOwner) continue;
                if (Layer(to) is "Domain" or "Infrastructure") errors.Add($"Foreign internal reference: {type.FullName} -> {reference.FullName}");
                else
                {
                    var sourceName = from.Split('.'); var layer = Array.IndexOf(sourceName, Layer(from));
                    var targetName = to.Split('.'); var targetLayer = Array.IndexOf(targetName, "Contracts");
                    var edge = layer > 0 && targetLayer > 0 ? $"{sourceName[layer - 1]}.{sourceName[layer]} -> {targetName[targetLayer - 1]}.Contracts.{reference.Name}" : "UNCLASSIFIED";
                    if (!ContractAllowed(edge)) errors.Add($"Unapproved contract: {edge}");
                }
            }
        }
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Order()));
    }

    [Fact]
    public void Durable_contexts_and_migrations_have_explicit_owner_metadata()
    {
        var owners = Manifest("persistence-ownership.json").GetProperty("owners").EnumerateArray().ToArray();
        var errors = new List<string>();
        foreach (var context in Types().Where(t => t.BaseType?.FullName == "Microsoft.EntityFrameworkCore.DbContext"))
        {
            var records = owners.Where(o => o.GetProperty("dbContext").ValueKind == System.Text.Json.JsonValueKind.String && o.GetProperty("dbContext").GetString() == context.FullName).ToArray();
            if (records.Length != 1) { errors.Add($"Unassigned DbContext: {context.FullName}"); continue; }
            var record = records[0];
            var migrationRoot = ResolveMigrationPath(record.GetProperty("migrationRoot").GetString()!);
            if (!Directory.Exists(migrationRoot) || !Directory.EnumerateFiles(migrationRoot, "*.cs").Any()) errors.Add($"Missing migrations: {context.FullName}");
            var schema = record.GetProperty("schema").GetString();
            var schemaBound = context.Methods.Where(m => m.HasBody).Any(method => method.Body.Instructions
                .Any(i => i.Operand is Mono.Cecil.MethodReference call && call.Name == "HasDefaultSchema" && i.Previous?.Operand as string == schema));
            if (!schemaBound) errors.Add($"Declared schema not bound: {context.FullName}");
        }
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void Integration_event_envelope_has_explicit_version_and_identity()
    {
        var envelopes = Types().Where(t => t.Name.Contains("IntegrationEventEnvelope", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(envelopes);
        foreach (var envelope in envelopes)
            foreach (var required in new[] { "SchemaVersion", "EventId", "EventType", "WorkspaceId", "SourceOwner" })
                Assert.Contains(envelope.Properties, p => p.Name == required);
    }

    [Fact]
    public void Product_project_dependency_graph_is_acyclic()
    {
        var graph = Projects.ToDictionary(p => Path.GetFullPath(Path.Combine(Backend, p)), p =>
            System.Xml.Linq.XDocument.Load(Path.Combine(Backend, p)).Descendants("ProjectReference")
                .Select(reference => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.Combine(Backend, p))!, reference.Attribute("Include")!.Value))).ToArray());
        Assert.False(HasCycle(graph), "Circular product project dependency detected.");
    }
}
