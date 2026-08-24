using System.Text.RegularExpressions;

namespace UnicoreCRM.Platform.Workspace.Contracts;

/// <summary>
/// The accepted workspace identifier shape. Both the route value and the
/// <c>X-Workspace-Id</c> header are checked against it before any membership lookup runs.
/// </summary>
internal static partial class WorkspaceIdContract
{
    internal static bool IsValid(string value) => Pattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
