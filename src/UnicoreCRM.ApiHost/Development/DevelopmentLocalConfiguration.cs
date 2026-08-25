using Microsoft.Extensions.Configuration.Json;

namespace UnicoreCRM.ApiHost.Development;

/// <summary>
/// Loads the untracked local developer configuration file.
///
/// Real local credentials - the developer's SQL Server login, the demo fixture password and any
/// future mail-provider secret - belong in <c>appsettings.Development.Local.json</c>, which
/// <c>.gitignore</c> excludes. The tracked <c>appsettings.Development.json</c> keeps only safe,
/// credential-free defaults, so a fresh clone starts without carrying anyone's secret and a
/// developer needs no environment variable or user-secrets command to override it.
///
/// The file is optional and Development-only: a missing file is a valid configuration and no
/// deployed host reads it.
/// </summary>
internal static class DevelopmentLocalConfiguration
{
    internal const string FileName = "appsettings.Development.Local.json";

    internal static WebApplicationBuilder AddDevelopmentLocalConfiguration(this WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
        {
            return builder;
        }

        builder.Configuration.AddJsonFile(FileName, optional: true, reloadOnChange: true);

        // AddJsonFile appends, which would rank the local file above environment variables and the
        // command line. The backend/scripts verification harnesses drive isolated databases through
        // ConnectionStrings__UnicoreCRM, and a developer who happens to have a local file must not
        // silently redirect those runs at their own database. The source is therefore moved back to
        // sit immediately after appsettings.{Environment}.json: tracked defaults, then this file,
        // then the ambient overrides that were already authoritative.
        var sources = builder.Configuration.Sources;
        var appendedIndex = sources.Count - 1;
        var anchorIndex = LastIndexOfJsonFile(sources, $"appsettings.{builder.Environment.EnvironmentName}.json");
        if (anchorIndex >= 0 && anchorIndex + 1 < appendedIndex)
        {
            var appended = sources[appendedIndex];
            sources.RemoveAt(appendedIndex);
            sources.Insert(anchorIndex + 1, appended);
        }

        return builder;
    }

    private static int LastIndexOfJsonFile(IList<IConfigurationSource> sources, string fileName)
    {
        for (var index = sources.Count - 1; index >= 0; index--)
        {
            if (sources[index] is JsonConfigurationSource json
                && string.Equals(json.Path, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
