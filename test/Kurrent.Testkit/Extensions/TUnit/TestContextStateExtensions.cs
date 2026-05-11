using System.Diagnostics.CodeAnalysis;

namespace Kurrent.Testkit;

[PublicAPI]
public static partial class TestContextStateExtensions {
	extension(TestContext? testContext) {
        public bool ContainsItem<T>(string? key = null) where T : notnull {
            key ??= TestContext.GenerateKey<T>();
            return testContext is { } && testContext.StateBag.Items.ContainsKey(key);
        }

        /// <summary>
        /// Extracts an item from the TestContext's StateBag, returning null if the key does not exist.
        /// </summary>
        public T? ExtractItem<T>(string? key = null) where T : notnull {
            key ??= TestContext.GenerateKey<T>();
            return testContext is { } && testContext.StateBag.Items.TryGetValue(key, out var value)
                              && value is T item ? item : default;
        }

        /// <summary>
        /// Extracts an item from the TestContext's StateBag, throwing if the key does not exist.
        /// </summary>
        public T ExtractRequiredItem<T>(string? key = null) where T : notnull =>
            testContext.ExtractItem<T>(key) ?? throw new InvalidOperationException($"'{key}' item does not exist in the TestContext StateBag!");

        /// <summary>
        /// Attempts to extract an item of type T from the TestContext's StateBag.
        /// </summary>
        public bool TryExtractItem<T>(string? key, [MaybeNullWhen(false)] out T item) where T : notnull =>
            (item = testContext.ExtractItem<T>(key)) is { };

        /// <summary>
        /// Attempts to extract an item of type T from the TestContext's StateBag.
        /// </summary>
        public bool TryExtractItem<T>([MaybeNullWhen(false)] out T item) where T : notnull =>
            (item = testContext.ExtractItem<T>()) is { };

        /// <summary>
        ///   Attempts to inject an item into the TestContext's StateBag.
        /// </summary>
        public T InjectItem<T>(T item, string? key = null) where T : notnull {
            ArgumentNullException.ThrowIfNull(testContext);
            key ??= TestContext.GenerateKey<T>();
            testContext.StateBag.Items[key] = item;
            return item;
        }

        static string GenerateKey<T>() => $"${typeof(T).FullName ?? typeof(T).Name}";
    }
}
