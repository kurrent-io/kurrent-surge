using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Client.Connectors;

[PublicAPI]
public static class LogPositionExtensions {
    extension(LogPosition position) {
        /// <summary>
        /// Ensures the log position is an absolute, non-negative coordinate in the log.
        /// Relative or symbolic sentinels (<see cref="LogPosition.Unset"/>, <see cref="LogPosition.Earliest"/>, <see cref="LogPosition.Latest"/>) are rejected.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The position is negative or equal to <see cref="LogPosition.Latest"/>.</exception>
        public void EnsureAbsolute() { // TODO SS: nah, we need to accept earliest but not unset or latest. This is a connector-specific requirement, so maybe this should be in the connector base class instead of here.
            if (position.Value >= 0 && position != LogPosition.Latest)
                return;

            throw new ArgumentOutOfRangeException(
                nameof(position),
                position,
                "Connectors require a concrete non-negative log position. Sentinel values (Unset, Earliest, Latest) are not supported. Pass 0 to start from the beginning of the log, or use the overload without a log position to resume from the connector's last known position."
            );
        }
    }
}
