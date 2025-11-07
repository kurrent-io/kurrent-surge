// ReSharper disable CheckNamespace

using Kurrent.Client.Exceptions;
using Kurrent.Rpc;

namespace Kurrent.Client {
    public static partial class ErrorDetails {
        public record ProjectionsSubsystemRestartFailed : KurrentError {
            internal static StreamNotFound From(Google.Rpc.Status status, KurrentErrorInfo errorInfo) {
                return new StreamNotFound {
                    ErrorMessage = status.Message
                };
            }

            public override Exception CreateException(Exception? innerException = null) =>
                new ProjectionsSubsystemRestartFailedException(this);
        }
    }
}

namespace Kurrent.Client.Exceptions {
    [PublicAPI]
    public sealed class ProjectionsSubsystemRestartFailedException(ErrorDetails.ProjectionsSubsystemRestartFailed error)
        : KurrentException<ErrorDetails.ProjectionsSubsystemRestartFailed>(error);
}
