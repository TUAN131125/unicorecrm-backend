namespace UnicoreCRM.Platform.AccessControl.Domain;

/// <summary>
/// The one canonical comparison rule for AccessControl resource keys and field keys.
///
/// <para>Every place that groups, resolves or matches one of those keys - effective-policy
/// aggregation, the record-fact provider registry, scope resolution, field-security resolution and
/// owner-side enforcement - must use this rule. When aggregation and resolution disagree about
/// casing, a role spelling a key one way and a role spelling it another produce two separate
/// effective entries, and the more permissive one can be resolved while the restrictive one is
/// never consulted. That is a policy bypass, not a cosmetic inconsistency.</para>
/// </summary>
internal static class RecordAccessKey
{
    internal static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    internal static StringComparison Comparison => StringComparison.OrdinalIgnoreCase;

    internal static IEqualityComparer<(string ResourceKey, string FieldKey)> PairComparer { get; } = new ResourceFieldComparer();

    internal static bool Matches(string left, string right) => string.Equals(left, right, Comparison);

    private sealed class ResourceFieldComparer : IEqualityComparer<(string ResourceKey, string FieldKey)>
    {
        public bool Equals((string ResourceKey, string FieldKey) left, (string ResourceKey, string FieldKey) right) =>
            Matches(left.ResourceKey, right.ResourceKey) && Matches(left.FieldKey, right.FieldKey);

        public int GetHashCode((string ResourceKey, string FieldKey) value) =>
            HashCode.Combine(
                Comparer.GetHashCode(value.ResourceKey),
                Comparer.GetHashCode(value.FieldKey));
    }
}
