using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.Common;

/// <summary>
/// Resolves the one business owner authoritative for a resource key. Registration is validated
/// once at construction: two owners claiming the same resource key is a composition defect, not a
/// runtime decision, and must not silently resolve to whichever happened to register first.
/// A resource key with no registered owner has no authoritative record facts, so record access
/// for it fails closed.
/// </summary>
internal sealed class RecordAccessFactProviderRegistry
{
    private readonly Dictionary<string, IRecordAccessFactProvider> providers;

    public RecordAccessFactProviderRegistry(IEnumerable<IRecordAccessFactProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        this.providers = new Dictionary<string, IRecordAccessFactProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            var key = provider.Descriptor.ResourceKey;
            if (!this.providers.TryAdd(key, provider))
            {
                throw new InvalidOperationException(
                    $"More than one record-access fact provider claims the resource key '{key}'.");
            }
        }
    }

    internal IRecordAccessFactProvider? Find(string resourceKey) =>
        providers.TryGetValue(resourceKey, out var provider) ? provider : null;
}
