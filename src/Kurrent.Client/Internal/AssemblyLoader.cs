using System.Reflection;

namespace Kurrent.Client;

/// <summary>
/// Provides functionality to load assemblies from a specified directory with optional filtering and error handling.
/// System libraries are automatically excluded.
/// </summary>
/// <example>
/// <code>
/// // Example 1: Load all non-system assemblies from a directory
/// var assemblies = AssemblyLoader
///     .FromDirectory("/path/to/assemblies")
///     .Load();
///
/// // Example 2: Filter by file path - only load plugins
/// var assemblies = AssemblyLoader
///     .FromDirectory("/path/to/assemblies", path => path.Contains("Plugin"))
///     .Load();
///
/// // Example 3: Filter by assembly attributes - only load assemblies with specific attribute
/// var assemblies = AssemblyLoader
///     .FromDirectory("/path/to/assemblies")
///     .WithAssemblyFilter(asm => asm.GetCustomAttribute&lt;PluginAttribute&gt;() != null)
///     .Load();
///
/// // Example 4: Combine path and assembly filters with error handling
/// var assemblies = AssemblyLoader
///     .FromDirectory("/plugins", path => path.EndsWith(".Plugin.dll"))
///     .WithAssemblyFilter(asm => asm.GetTypes().Any(t => typeof(IPlugin).IsAssignableFrom(t)))
///     .WithErrorHandler((path, ex) => Console.WriteLine($"Failed: {path}"))
///     .WithRecursiveSearch(true)
///     .Load();
/// </code>
/// </example>
class AssemblyLoader {
    static readonly string[] SystemLibraryPatterns = [
        "system.",
        "microsoft.",
        "windows.",
        "netstandard.",
        "mscorlib.",
        "dotnet.",
        "runtime.",
        "grpc.core.",
        "grpc.net."
    ];

    /// <summary>
    /// Creates an assembly loader for the specified directory.
    /// </summary>
    /// <param name="directoryPath">The directory path to search for assemblies.</param>
    /// <param name="pathFilter">
    /// Optional predicate to filter assembly files by their full path.
    /// The predicate receives the complete file path (e.g., "/path/to/MyApp.dll").
    /// Return true to include the assembly, false to exclude it.
    /// System libraries are always excluded before this filter is applied.
    /// </param>
    /// <returns>A new <see cref="AssemblyLoader"/> instance.</returns>
    /// <example>
    /// <code>
    /// // Load only assemblies with "Plugin" in the path
    /// var loader = AssemblyLoader.FromDirectory("/path", path => path.Contains("Plugin"));
    ///
    /// // Load only assemblies starting with "MyApp."
    /// var loader = AssemblyLoader.FromDirectory("/path", path => {
    ///     var fileName = Path.GetFileName(path);
    ///     return fileName.StartsWith("MyApp.");
    /// });
    ///
    /// // Exclude test assemblies
    /// var loader = AssemblyLoader.FromDirectory("/path", path => !path.Contains("Test"));
    /// </code>
    /// </example>
    public static AssemblyLoader FromDirectory(string directoryPath, Predicate<string>? pathFilter = null) => new(directoryPath, pathFilter);

    /// <summary>
    /// Creates an assembly loader for the application's base directory.
    /// </summary>
    /// <param name="pathFilter">
    /// Optional predicate to filter assembly files by their full path.
    /// </param>
    /// <returns>A new <see cref="AssemblyLoader"/> instance.</returns>
    public static AssemblyLoader FromAppDomain(Predicate<string>? pathFilter = null) => new(AppDomain.CurrentDomain.BaseDirectory, pathFilter);

    AssemblyLoader(string directoryPath, Predicate<string>? pathFilter) {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

        DirectoryPath      = directoryPath;
        AssemblyPathFilter = pathFilter;
    }

    string                      DirectoryPath       { get; }
    Predicate<string>?          AssemblyPathFilter  { get; }
    Predicate<Assembly>?        AssemblyFilter      { get; set; }
    Action<string, Exception>?  OnLoadError         { get; set; }
    SearchOption                SearchOption        { get; set; } = SearchOption.TopDirectoryOnly;

    /// <summary>
    /// Configures a predicate to filter assemblies after they are loaded.
    /// </summary>
    /// <param name="assemblyFilter">
    /// Predicate that receives the loaded <see cref="Assembly"/> and returns true to include it, false to exclude it.
    /// This filter is applied after the assembly is successfully loaded, allowing inspection of types, attributes, and metadata.
    /// </param>
    /// <returns>The current <see cref="AssemblyLoader"/> instance for method chaining.</returns>
    /// <example>
    /// <code>
    /// // Filter by custom attribute
    /// loader.WithAssemblyFilter(asm => asm.GetCustomAttribute&lt;PluginAttribute&gt;() != null);
    ///
    /// // Filter by assembly name
    /// loader.WithAssemblyFilter(asm => asm.GetName().Name.StartsWith("MyApp"));
    ///
    /// // Filter by contained types
    /// loader.WithAssemblyFilter(asm => asm.GetTypes().Any(t => typeof(IPlugin).IsAssignableFrom(t)));
    ///
    /// // Filter by assembly version
    /// loader.WithAssemblyFilter(asm => asm.GetName().Version >= new Version(2, 0));
    /// </code>
    /// </example>
    public AssemblyLoader WithAssemblyFilter(Predicate<Assembly> assemblyFilter) {
        AssemblyFilter = assemblyFilter;
        return this;
    }

    /// <summary>
    /// Configures an error handler to be invoked when assembly loading fails.
    /// </summary>
    /// <param name="errorHandler">Action to handle load errors, receiving the assembly path and the exception.</param>
    /// <returns>The current <see cref="AssemblyLoader"/> instance for method chaining.</returns>
    public AssemblyLoader WithErrorHandler(Action<string, Exception> errorHandler) {
        OnLoadError = errorHandler;
        return this;
    }

    /// <summary>
    /// Configures whether to search subdirectories recursively.
    /// </summary>
    /// <param name="recursive">True to search subdirectories, false for top-level directory only.</param>
    /// <returns>The current <see cref="AssemblyLoader"/> instance for method chaining.</returns>
    public AssemblyLoader WithRecursiveSearch(bool recursive = true) {
        SearchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return this;
    }

    /// <summary>
    /// Loads all matching assemblies from the configured directory.
    /// System libraries are automatically excluded.
    /// </summary>
    /// <returns>An array of successfully loaded <see cref="Assembly"/> instances.</returns>
    /// <remarks>
    /// The loading process follows these steps:
    /// 1. Enumerate all .dll files in the directory (respecting recursive search setting)
    /// 2. Exclude system libraries (System.*, Microsoft.*, etc.)
    /// 3. Apply path filter if configured (filters by file path)
    /// 4. Load the assembly using Assembly.LoadFrom
    /// 5. Apply assembly filter if configured (filters by assembly metadata)
    /// 6. Add to results or invoke error handler on failure
    /// </remarks>
    public Assembly[] Load() {
        var loadedAssemblies = new List<Assembly>();
        var assemblyFiles    = Directory.EnumerateFiles(DirectoryPath, "*.dll", SearchOption);

        foreach (var assemblyPath in assemblyFiles) {
            var fileName = Path.GetFileName(assemblyPath);

            if (IsSystemLibrary(fileName))
                continue;

            if (AssemblyPathFilter is not null && !AssemblyPathFilter(assemblyPath))
                continue;

            TryLoadAssembly(assemblyPath, loadedAssemblies);
        }

        return [..loadedAssemblies];
    }

    void TryLoadAssembly(string assemblyPath, List<Assembly> loadedAssemblies) {
        try {
            var assembly = Assembly.LoadFrom(assemblyPath);

            if (AssemblyFilter is not null && !AssemblyFilter(assembly))
                return;

            loadedAssemblies.Add(assembly);
        }
        catch (Exception ex) {
            OnLoadError?.Invoke(assemblyPath, ex);
        }
    }

    static bool IsSystemLibrary(string fileName) =>
        SystemLibraryPatterns.Any(pattern => fileName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase));
}
