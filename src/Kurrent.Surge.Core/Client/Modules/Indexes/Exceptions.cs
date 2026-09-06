// ReSharper disable CheckNamespace

using Kurrent.Surge.Client.Indexes;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Errors;

namespace Kurrent.Surge.Client.Exceptions;

[PublicAPI]
public class IndexNotFoundException : KurrentException {
    public static Errors.ErrorCode Code { get; } = Errors.ErrorCode.From("INDEX_NOT_FOUND");

    public IndexNotFoundException(IndexName index)
        : this($"Index '{index}' was not found.") => Index = index;

    public IndexNotFoundException(string message) : base(Code, message) { }

    public IndexNotFoundException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public IndexName Index { get; init; } = IndexName.Unset;
}

[PublicAPI]
public class IndexAlreadyExistsException : KurrentException {
    public static Errors.ErrorCode Code { get; } = Errors.ErrorCode.From("INDEX_ALREADY_EXISTS");

    public IndexAlreadyExistsException(IndexName index)
        : this($"Index '{index}' already exists.") => Index = index;

    public IndexAlreadyExistsException(string message) : base(Code, message) { }

    public IndexAlreadyExistsException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public IndexName Index { get; init; } = IndexName.Unset;
}

[PublicAPI]
public class IndexesNotReadyException : KurrentException {
    public static Errors.ErrorCode Code { get; } = Errors.ErrorCode.From("INDEXES_NOT_READY");

    public IndexesNotReadyException(LogPosition currentPosition, LogPosition targetPosition)
        : this($"Indexes are not ready. Current position: {currentPosition}. Target position: {targetPosition}.") {
        CurrentPosition = currentPosition;
        TargetPosition  = targetPosition;
    }

    public IndexesNotReadyException(string message) : base(Code, message) { }

    public IndexesNotReadyException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public LogPosition CurrentPosition { get; init; } = LogPosition.Unset;
    public LogPosition TargetPosition  { get; init; } = LogPosition.Unset;
}

[PublicAPI]
public class SecondaryIndexingDisabledException : KurrentException {
    public static Errors.ErrorCode Code { get; } = Errors.ErrorCode.From("SECONDARY_INDEXING_DISABLED");

    public SecondaryIndexingDisabledException()
        : this("Secondary indexing is disabled on this server.") { }

    public SecondaryIndexingDisabledException(string message) : base(Code, message) { }

    public SecondaryIndexingDisabledException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }
}
