namespace Kurrent.Client.Streams;

#if NET9_0_OR_GREATER

public sealed class QueryNormalizationException(string message) : Exception(message);

#endif
