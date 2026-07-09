namespace Kurrent.Surge.Client.Streams;

[PublicAPI]
public static class MetadataConditionalExtensions {
	extension(Metadata source) {
		public Metadata WithIf<T>(string key, T? value, bool condition) {
			if (!condition) return source;
			return source.With(key, value);
		}

		public Metadata WithIf<T>(string key, Func<T?> valueFactory, bool condition) {
			if (!condition) return source;
			return source.With(key, valueFactory());
		}

		public Metadata WithIf<T>(string key, Func<T?> valueFactory, Func<Metadata, bool> predicate) {
			if (!predicate(source)) return source;
			return source.With(key, valueFactory());
		}
	}
}
