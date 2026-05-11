using System.Collections.Concurrent;
using System.Runtime.Loader;

namespace Kurrent.Client.Schema.Serialization;

/// <summary>
/// Must be replaced by source generators or a shared type mapper or both.
/// </summary>
static class ForbidenTypeResolver {
    static readonly ConcurrentQueue<string> PendingDlls;

    static readonly ConcurrentDictionary<string, Type?> TypeCache = new();
    
    static readonly Lock DiskLock = new();

    static ForbidenTypeResolver() {
        var allLibs = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll");
        
        var loadedLocations = new HashSet<string>(
            AssemblyLoadContext
                .Default.Assemblies
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => Path.GetFullPath(a.Location)),
            StringComparer.OrdinalIgnoreCase
        );

        var filtered = allLibs.Where(path => {
            var fullPath = Path.GetFullPath(path);
            var fileName = Path.GetFileName(path);

            return !IsSystemName(fileName) && !loadedLocations.Contains(fullPath);
        });

        PendingDlls = new ConcurrentQueue<string>(filtered);
    }
    
    public static Type? ResolveType(string typeName) {
        if (TypeCache.TryGetValue(typeName, out var cachedType))
            return cachedType;
        
        var type = Type.GetType(typeName, false) 
                ?? SearchLoadedAssemblies(typeName) 
                ?? LoadAndScanFromDisk(typeName);

        return TypeCache.GetOrAdd(typeName, type);
    }

    static Type? SearchLoadedAssemblies(string typeName) {
        foreach (var assembly in AssemblyLoadContext.Default.Assemblies.Where(ass => !IsSystemName(ass.FullName))) {
            if (assembly?.GetType(typeName, false) is { } foundType)
                return foundType;
        }

        return null;
    }

    static Type? LoadAndScanFromDisk(string typeName) {
        if (PendingDlls.IsEmpty)
            return null;
        
        using (DiskLock.EnterScope()) {
            // Double-check cache inside lock to handle race conditions
            if (TypeCache.TryGetValue(typeName, out var cached))
                return cached;

            while (PendingDlls.TryDequeue(out var dllPath)) {
                try {
                    // Loads into Default context (cached by the runtime)
                    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);
                    if (assembly?.GetType(typeName, false) is { } foundType)
                        return foundType;
                }
                catch (BadImageFormatException) { 
                    /* Skip native/unmanaged DLLs */
                }
                catch (Exception) { 
                    /* Log other load errors as needed */
                }
            }
        }

        return null;
    }
    
    static bool IsSystemName(string? name) {
        if (string.IsNullOrEmpty(name))
            return true;

        return name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase);
    }
}