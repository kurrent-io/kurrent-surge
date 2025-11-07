// ReSharper disable CheckNamespace

using Kurrent.Client.Exceptions;
using Kurrent.Rpc;

namespace Kurrent.Client {
    public static partial class ErrorDetails {
        /// <summary>
        /// Represents an error indicating that a persistent subscription has been dropped by the server.
        /// </summary>
        public record PersistentSubscriptionMaximumSubscribersReached : KurrentError {
            public override Exception CreateException(Exception? innerException = null) =>
                new PersistentSubscriptionMaximumSubscribersReachedException(this);

            internal static PersistentSubscriptionMaximumSubscribersReached From(Google.Rpc.Status status, KurrentErrorInfo errorInfo) {
                return new PersistentSubscriptionMaximumSubscribersReached {
                    ErrorMessage = status.Message
                };
            }
        }

        /// <summary>
        /// Represents an error indicating that a persistent subscription has been dropped by the server, typically due to an unexpected condition or configuration change.
        /// </summary>
        public record PersistentSubscriptionDropped : KurrentError {
            public override Exception CreateException(Exception? innerException = null) =>
                new PersistentSubscriptionDroppedException(this);

            internal static PersistentSubscriptionDropped From(Google.Rpc.Status status, KurrentErrorInfo errorInfo) {
                return new PersistentSubscriptionDropped {
                    ErrorMessage = status.Message
                };
            }
        }
    }
}

namespace Kurrent.Client.Exceptions {
    public sealed class PersistentSubscriptionMaximumSubscribersReachedException(ErrorDetails.PersistentSubscriptionMaximumSubscribersReached error)
        : KurrentException<ErrorDetails.PersistentSubscriptionMaximumSubscribersReached>(error);

    public sealed class PersistentSubscriptionDroppedException(ErrorDetails.PersistentSubscriptionDropped error)
        : KurrentException<ErrorDetails.PersistentSubscriptionDropped>(error);
}
