using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;

namespace Kurrent.Testing.TUnit;

[PublicAPI]
public static partial class TestContextExtensions {
	/// <summary>
	/// Attempts to extract an item from the TestContext's StateBag.
	/// </summary>
	public static bool TryExtractItem(this TestContext? ctx, string key, [MaybeNullWhen(false)] out object item) {
		if (ctx is not null && ctx.StateBag.Items.TryGetValue(key, out var val)) {
			item = val!;
			return true;
		}

		item = null;
		return false;
	}

	/// <summary>
	/// Attempts to extract an item of type T from the TestContext's StateBag.
	/// </summary>
	public static bool TryExtractItem<T>(this TestContext? ctx, string? key, out T item) {
		key ??= $"${typeof(T).FullName ?? typeof(T).Name}";

		if (ctx.TryExtractItem(key, out var i)) {
			item = (T)i;
			return true;
		}

		item = default!;
		return false;
	}

	/// <summary>
	/// Attempts to extract an item of type T from the TestContext's StateBag.
	/// </summary>
	public static bool TryExtractItem<T>(this TestContext? ctx, out T item) where T : notnull =>
		TryExtractItem(ctx, null, out item);

	/// <summary>
	/// Extracts an item from the TestContext's StateBag, throwing if the key does not exist.
	/// </summary>
	public static T ExtractItem<T>(this TestContext? ctx, string? key = null) where T : notnull {
		key ??= $"${typeof(T).FullName ?? typeof(T).Name}";
		return !TryExtractItem(ctx, key, out T item)
			? throw new InvalidOperationException($"'{key}' item not found in the TestContext StateBag!")
			: item;
	}

	/// <summary>
	/// Attempts to inject an item into the TestContext's StateBag.
	/// </summary>
	public static bool TryInjectItem<T>(this TestContext? ctx, T item, string? key = null) where T : notnull {
		key ??= $"${typeof(T).FullName ?? typeof(T).Name}";
		return ctx?.StateBag.Items.TryAdd(key, item) ?? throw new InvalidOperationException("No current TestContext available!");
	}

	/// <summary>
	/// Injects an item into the TestContext's StateBag, throwing if the key already exists.
	/// </summary>
	public static T InjectItem<T>(this TestContext? ctx, T item, string? key = null) where T : notnull {
		key ??= $"${typeof(T).FullName ?? typeof(T).Name}";
		return !TryInjectItem(ctx, item, key)
			? throw new InvalidOperationException($"'{key}' item already exists in the TestContext StateBag!")
			: item;
	}
}

// public static partial class TestContextLoggingExtensions {
// 	const string LoggerFactoryKey = "$TestLoggerFactory";
//     const string LoggerKey        = "$TestLogger";
//
//     extension(TestContext? ctx) {
//         public Serilog.ILogger AddLogging(TestLogger testLogger) {
//             ILoggerFactory factory = new SerilogLoggerFactory(testLogger, dispose: true);
//             ctx.InjectItem(factory, LoggerFactoryKey);
//             ctx.InjectItem(factory.CreateLogger(null!), LoggerKey);
//             return testLogger;
//         }
//     }
// }

public static class TestContextLoggingExtensions {
    const string LoggerFactoryKey = "$TestLoggerFactory";
    const string LoggerKey        = "$TestLogger";

    extension(TestContext? ctx) {
        public Serilog.ILogger AddLogging(TestLogger testLogger) {
            ILoggerFactory factory = new SerilogLoggerFactory(testLogger, dispose: true);
            ctx.InjectItem(factory, LoggerFactoryKey);
            ctx.InjectItem(factory.CreateLogger(null!), LoggerKey);
            return testLogger;
        }
    }

    extension(TestContext) {
        public static bool TryGetLoggerFactory(out ILoggerFactory factory) =>
            TestContext.Current.TryExtractItem(LoggerFactoryKey, out factory);

        public static ILoggerFactory CurrentLoggerFactory =>
            TestContext.Current.ExtractItem<ILoggerFactory>(LoggerFactoryKey);

        public static ILogger CurrrentLogger =>
            TestContext.Current.ExtractItem<ILogger>(LoggerKey);
    }
}
