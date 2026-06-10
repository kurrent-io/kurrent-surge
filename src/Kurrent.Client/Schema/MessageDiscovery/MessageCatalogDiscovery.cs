using System.Reflection;

namespace Kurrent.Client;

/// <summary>
/// Enumerates the catalogs exported by loaded assemblies via their generated
/// <see cref="MessageCatalogProviderAttribute"/>s.
/// </summary>
/// <remarks>
/// Fully NativeAOT-compatible: assembly enumeration and attribute reading are supported
/// metadata reflection, attribute applications root the generated catalogs against trimming,
/// and catalogs are obtained through a virtual call — no member reflection anywhere.
/// Memoized process-wide: the result is derived purely from compile-time metadata, so caching
/// it is a pure-function optimization, not shared mutable state.
/// </remarks>
static class MessageCatalogDiscovery {
    static readonly Lazy<IReadOnlyList<MessageCatalog>> Cached = new(Discover, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<MessageCatalog> GetCatalogs() => Cached.Value;

    static IReadOnlyList<MessageCatalog> Discover() {
        // On JIT, assemblies load lazily — a contracts assembly untouched before the first
        // client is constructed would be missed. Walking the reference closure forces them
        // in. Under NativeAOT everything is compiled into the binary, so this is a no-op
        // sweep over already-loaded assemblies.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>(AppDomain.CurrentDomain.GetAssemblies());
        var catalogs = new List<MessageCatalog>();

        while (pending.Count > 0) {
            var assembly = pending.Dequeue();

            if (!visited.Add(assembly.FullName ?? assembly.GetName().Name ?? ""))
                continue;

            foreach (var provider in assembly.GetCustomAttributes<MessageCatalogProviderAttribute>())
                catalogs.AddRange(provider.GetCatalogs());

            foreach (var reference in assembly.GetReferencedAssemblies()) {
                if (visited.Contains(reference.FullName))
                    continue;

                try {
                    pending.Enqueue(Assembly.Load(reference));
                }
                catch {
                    // Reference can't load (platform-specific, trimmed, etc.) — it cannot
                    // contribute catalogs, skip it.
                }
            }
        }

        return catalogs;
    }
}
