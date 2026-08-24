namespace UnicoreCRM.ApiHost.Development;

/// <summary>
/// Development-only orchestration for the local demo fixture. It composes the
/// canonical IdentityAuth, Workspace and AccessControl Development bootstrap
/// configuration from one local fixture declaration plus external environment
/// state. ApiHost owns no Identity, Workspace or AccessControl persistence:
/// every record is still written by its canonical owner.
/// </summary>
internal static class DevelopmentDemoBootstrap
{
    private const string EnabledVariable = "UNICORE_DEV_SEED_ENABLED";
    private const string EmailVariable = "UNICORE_DEV_SEED_EMAIL";
    private const string PasswordVariable = "UNICORE_DEV_SEED_PASSWORD";
    private const string SectionName = "DevelopmentDemoBootstrap";

    internal static WebApplicationBuilder AddDevelopmentDemoBootstrap(this WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment() || !IsEnabled(builder.Configuration[EnabledVariable]))
            return builder;

        var email = builder.Configuration[EmailVariable]?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException($"{EnabledVariable} requires {EmailVariable} from local Development environment state.");
        var password = builder.Configuration[PasswordVariable];
        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException($"{EnabledVariable} requires {PasswordVariable} from local Development environment state.");

        var fixture = builder.Configuration.GetSection(SectionName);
        var workspaceKey = fixture["Workspace:Key"]?.Trim();
        if (string.IsNullOrWhiteSpace(workspaceKey))
            throw new InvalidOperationException($"The {SectionName} Development fixture must declare Workspace:Key.");

        builder.Configuration.AddInMemoryCollection(Compose(fixture, email, password, workspaceKey));
        return builder;
    }

    private static bool IsEnabled(string? value) =>
        bool.TryParse(value, out var enabled) && enabled;

    private static Dictionary<string, string?> Compose(
        IConfigurationSection fixture,
        string email,
        string password,
        string workspaceKey)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["IdentityAuth:DevelopmentBootstrap:Enabled"] = "true",
            ["IdentityAuth:DevelopmentBootstrap:Email"] = email,
            ["IdentityAuth:DevelopmentBootstrap:Password"] = password,
            ["Workspace:DevelopmentBootstrap:Enabled"] = "true",
            ["Workspace:DevelopmentBootstrap:IdentityEmail"] = email,
            ["AccessControl:DevelopmentBootstrap:Enabled"] = "true",
            ["AccessControl:DevelopmentBootstrap:IdentityEmail"] = email,
            ["AccessControl:DevelopmentBootstrap:WorkspaceKey"] = workspaceKey
        };
        Copy(fixture.GetSection("DisplayName"), "IdentityAuth:DevelopmentBootstrap:DisplayName", values);
        Copy(fixture.GetSection("Workspace"), "Workspace:DevelopmentBootstrap:MemberWorkspace", values);
        Copy(fixture.GetSection("IsolationWorkspace"), "Workspace:DevelopmentBootstrap:NonMemberWorkspace", values);
        Copy(fixture.GetSection("RoleName"), "AccessControl:DevelopmentBootstrap:RoleName", values);
        Copy(fixture.GetSection("Capabilities"), "AccessControl:DevelopmentBootstrap:Capabilities", values);
        return values;
    }

    private static void Copy(IConfigurationSection source, string targetPath, IDictionary<string, string?> values)
    {
        foreach (var entry in source.AsEnumerable(true))
        {
            if (entry.Value is null)
                continue;
            values[entry.Key.Length == 0 ? targetPath : $"{targetPath}:{entry.Key}"] = entry.Value;
        }
    }
}
