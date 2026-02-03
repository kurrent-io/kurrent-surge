using Kurrent.Client.Exceptions;
using Resources;

namespace Kurrent.Processors;

[PublicAPI]
public class FailedToCreateWorkerException(Exception innerException) : KurrentException(Code, ErrorMessage, innerException) {
    static readonly ErrorCode Code         = ErrorCode.From("WORKER_CREATION_FAILED");
    static readonly string    ErrorMessage = "Failed to create record processor instance.";
}

/// <summary>
/// Exception thrown when <see cref="IRecordProcessor.OnInitialize"/> fails.
/// </summary>
[PublicAPI]
public class InitializeException(Exception innerException) : KurrentException(Code, ErrorMessages.InitializeException, innerException) {
    static readonly ErrorCode Code = ErrorCode.From("INITIALIZE_FAILED");

    internal static InitializeException? From(Exception ex) =>
        ex as InitializeException;
}

[PublicAPI]
public class PartitionsAssignedException(Exception innerException) : KurrentException(Code, ErrorMessages.InitializeException, innerException) {
    static readonly ErrorCode Code = ErrorCode.From("PARTITIONS_ASSIGNED_FAILED");

    internal static PartitionsAssignedException? From(Exception ex) =>
        ex as PartitionsAssignedException;
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

// /// <summary>
// /// Exception thrown when <see cref="IRecordProcessor.OnCheckpoint"/> fails.
// /// </summary>
// [PublicAPI]
// public class CheckpointException(Exception innerException) : KurrentException(Code, ErrorMessages.CheckpointException, innerException) {
//     static readonly ErrorCode Code = ErrorCode.From("CHECKPOINT_FAILED");
//
//     internal static CheckpointException? From(Exception ex) =>
//         ex as CheckpointException;
// }

// /// <summary>
// /// Exception thrown when <see cref="IRecordProcessor.OnPartitionsAssigned"/> fails.
// /// </summary>
// [PublicAPI]
// public class PartitionsAssignedException(Exception innerException) : KurrentException(Code, ErrorMessages.AssignmentChangeException, innerException) {
//     static readonly ErrorCode Code = ErrorCode.From("PARTITIONS_ASSIGNED_FAILED");
//
//     internal static PartitionsAssignedException From(Exception ex) =>
//         new PartitionsAssignedException(ex);
// }

/// <summary>
/// Exception thrown when <see cref="IRecordProcessor.OnShutdown"/> fails.
/// </summary>
[PublicAPI]
public class ShutdownException(Exception innerException) : KurrentException(Code, ErrorMessages.ShutdownException, innerException) {
    static readonly ErrorCode Code = ErrorCode.From("SHUTDOWN_FAILED");

    internal static ShutdownException? From(Exception ex) =>
        ex as ShutdownException;
}
