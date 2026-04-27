using Kurrent.Client.Exceptions;

namespace Kurrent.Processors.Checkpointing;

/// <summary>
/// Thrown by a <see cref="CheckpointStore"/> implementation when an underlying operation
/// (initialize / load / store / reset) fails. The originating store is identified in the
/// message prefix; the original exception is attached as <see cref="Exception.InnerException"/>.
/// </summary>
[PublicAPI]
public sealed class CheckpointStoreException : KurrentException {
    public CheckpointStoreException(string storeName, string message, Exception? innerException = null)
        : base(ErrorCode.FromType<CheckpointStoreException>(), $"[{storeName}] {message}", innerException) { }
}
