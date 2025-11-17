namespace Kurrent.Testing.TUnit;

[PublicAPI]
public static partial class TestContextExtensions {
	extension(TestContext? ctx) {
        /// <summary>
        /// Attempts to extract an item of type T from the TestContext's StateBag.
        /// </summary>
        public bool TryExtractItem<T>(string? key, out T item) {
            key ??= TestContext.GenerateKey<T>();

            if (ctx is not null && ctx.StateBag.Items.TryGetValue(key, out var val) && val is T i) {
                item = (T)i;
                return true;
            }

            item = default!;
            return false;
        }

        /// <summary>
        /// Attempts to extract an item of type T from the TestContext's StateBag.
        /// </summary>
        public bool TryExtractItem<T>(out T item) where T : notnull =>
            TryExtractItem(ctx, null, out item);

        /// <summary>
        /// Extracts an item from the TestContext's StateBag, throwing if the key does not exist.
        /// </summary>
        public T ExtractItem<T>(string? key = null) where T : notnull {
            key ??= TestContext.GenerateKey<T>();
            return !TryExtractItem(ctx, key, out T item)
                ? throw new InvalidOperationException($"'{key}' item not found in the TestContext StateBag!")
                : item;
        }


        /// <summary>
        /// Attempts to inject an item into the TestContext's StateBag.
        /// </summary>
        public bool TryInjectItem<T>(T item, string? key = null) where T : notnull {
            key ??= TestContext.GenerateKey<T>();
            return ctx?.StateBag.Items.TryAdd(key, item) ?? throw new InvalidOperationException("No current TestContext available!");
        }

        /// <summary>
        /// Injects an item into the TestContext's StateBag, throwing if the key already exists.
        /// </summary>
        public T InjectItem<T>(T item, string? key = null) where T : notnull {
            key ??= TestContext.GenerateKey<T>();
            return !TryInjectItem(ctx, item, key)
                ? throw new InvalidOperationException($"'{key}' item already exists in the TestContext StateBag!")
                : item;
        }

        static string GenerateKey<T>() => $"${typeof(T).FullName ?? typeof(T).Name}";
    }
}
