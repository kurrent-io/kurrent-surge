using Kurrent.Client.Exceptions;
using Resources;

namespace Kurrent.Client.Streams;

/// <summary>
/// Exception thrown when <see cref="IRecordProcessor.Initialize"/> fails.
/// </summary>
[PublicAPI]
public class InitializeException(Exception innerException) : KurrentException(Code, ErrorMessages.InitializeException, innerException) {
    static readonly ErrorCode Code = ErrorCode.From("INITIALIZE_FAILED");

    internal static InitializeException? From(Exception ex) =>
        ex as InitializeException;
}

/// <summary>
/// Exception thrown when <see cref="IRecordProcessor.ProcessRecords"/> fails.
/// </summary>
[PublicAPI]
public class ProcessRecordsException(Exception innerException) : KurrentException(Code, ErrorMessages.ProcessRecordsException, innerException) {
    static readonly ErrorCode Code = ErrorCode.From("PROCESS_RECORDS_FAILED");

    internal static ProcessRecordsException? From(Exception ex) =>
        ex as ProcessRecordsException;
}

/// <summary>
/// Exception thrown when <see cref="IRecordProcessor.OnHeartbeat"/> fails.
/// </summary>
[PublicAPI]
public class HeartbeatException(Exception innerException) : KurrentException(Code, ErrorMessages.HeartbeatException, innerException) {
    static readonly ErrorCode Code = ErrorCode.From("HEARTBEAT_FAILED");

    internal static HeartbeatException? From(Exception ex) =>
        ex as HeartbeatException;
}


/// <summary>
/// Exception thrown when <see cref="IRecordProcessor.OnShutdown"/> fails.
/// </summary>
[PublicAPI]
public class ShutdownException(Exception innerException) : KurrentException(Code, ErrorMessages.ShutdownException, innerException) {
    static readonly ErrorCode Code = ErrorCode.From("SHUTDOWN_FAILED");

    internal static ShutdownException? From(Exception ex) =>
        ex as ShutdownException;
}
