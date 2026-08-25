namespace UnicoreCRM.ApiHost.Development;

/// <summary>
/// Development-only orchestration for the local demo fixture. It composes the canonical
/// IdentityAuth, Workspace and AccessControl Development bootstrap configuration from one local
/// fixture declaration. ApiHost owns no Identity, Workspace or AccessControl persistence: every
/// record is still written by its canonical owner.
///
/// Everything here is a <em>default</em>. A value that is already configured - by environment
/// variable, user secret, command line or an appsettings file - is never overwritten, so the local
/// one-click experience never fights an explicit configuration. The whole composition is skipped
/// outside the Development environment, so no non-Development host can pick up a demo credential.
/// </summary>
internal static class DevelopmentDemoBootstrap
{
    private const string EnabledVariable = "UNICORE_DEV_SEED_ENABLED";
    private const string EmailVariable = "UNICORE_DEV_SEED_EMAIL";
    private const string PasswordVariable = "UNICORE_DEV_SEED_PASSWORD";
    private const string SectionName = "DevelopmentDemoBootstrap";

    internal static WebApplicationBuilder AddDevelopmentDemoBootstrap(this WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
            return builder;
        // The fixture is on by default in Development. An explicit opt-out still wins.
        if (builder.Configuration[EnabledVariable] is { } enabled && !IsEnabled(enabled))
            return builder;

        var fixture = builder.Configuration.GetSection(SectionName);
        var email = FirstConfigured(builder.Configuration[EmailVariable], fixture["Email"]);
        var password = FirstConfigured(builder.Configuration[PasswordVariable], fixture["Password"]);
        var workspaceKey = fixture["Workspace:Key"]?.Trim();
        if (email is null || password is null || string.IsNullOrWhiteSpace(workspaceKey))
        {
            // Nothing to seed. A Development host without a fixture is still a valid configuration.
            return builder;
        }

        builder.Configuration.AddInMemoryCollection(Compose(builder.Configuration, fixture, email, password, workspaceKey));
        return builder;
    }

    private static bool IsEnabled(string? value) =>
        bool.TryParse(value, out var enabled) && enabled;

    private static string? FirstConfigured(params string?[] candidates) =>
        candidates.Select(candidate => candidate?.Trim())
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

    private static Dictionary<string, string?> Compose(
        IConfiguration configuration,
        IConfigurationSection fixture,
        string email,
        string password,
        string workspaceKey)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        SetDefault(configuration, values, "IdentityAuth:DevelopmentBootstrap:Enabled", "true");
        SetDefault(configuration, values, "IdentityAuth:DevelopmentBootstrap:Email", email);
        SetDefault(configuration, values, "IdentityAuth:DevelopmentBootstrap:Password", password);
        SetDefault(configuration, values, "Workspace:DevelopmentBootstrap:Enabled", "true");
        SetDefault(configuration, values, "Workspace:DevelopmentBootstrap:IdentityEmail", email);
        SetDefault(configuration, values, "AccessControl:DevelopmentBootstrap:Enabled", "true");
        SetDefault(configuration, values, "AccessControl:DevelopmentBootstrap:IdentityEmail", email);
        SetDefault(configuration, values, "AccessControl:DevelopmentBootstrap:WorkspaceKey", workspaceKey);
        Copy(configuration, fixture.GetSection("DisplayName"), "IdentityAuth:DevelopmentBootstrap:DisplayName", values);
        Copy(configuration, fixture.GetSection("Workspace"), "Workspace:DevelopmentBootstrap:MemberWorkspace", values);
        Copy(configuration, fixture.GetSection("IsolationWorkspace"), "Workspace:DevelopmentBootstrap:NonMemberWorkspace", values);
        Copy(configuration, fixture.GetSection("RoleName"), "AccessControl:DevelopmentBootstrap:RoleName", values);
        Copy(configuration, fixture.GetSection("Capabilities"), "AccessControl:DevelopmentBootstrap:Capabilities", values);
        return values;
    }

    private static void Copy(
        IConfiguration configuration,
        IConfigurationSection source,
        string targetPath,
        IDictionary<string, string?> values)
    {
        foreach (var entry in source.AsEnumerable(true))
        {
            if (entry.Value is null)
                continue;
            SetDefault(configuration, values, entry.Key.Length == 0 ? targetPath : $"{targetPath}:{entry.Key}", entry.Value);
        }
    }

    /// <summary>Supplies a value only where the developer has not configured one already.</summary>
    private static void SetDefault(
        IConfiguration configuration,
        IDictionary<string, string?> values,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(configuration[key]))
            return;
        values[key] = value;
    }
}
