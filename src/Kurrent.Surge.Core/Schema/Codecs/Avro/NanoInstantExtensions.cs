using NodaTime;

namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Conversions between <see cref="NanoInstant"/> and NodaTime's <see cref="Instant"/>.
/// </summary>
/// <remarks>
/// Both are nanosecond-precision points on the global timeline, so a conversion loses nothing inside
/// <see cref="NanoInstant"/>'s range — a signed 64-bit nanosecond count, 1677-09-21 to 2262-04-11.
/// <para>
/// These exist so a codebase on the BCL time model can still hand a value to NodaTime without going
/// through <see cref="System.DateTime"/> and truncating to 100 nanoseconds on the way.
/// </para>
/// </remarks>
[PublicAPI]
public static class NanoInstantExtensions {
    extension(NanoInstant source) {
        /// <summary>Converts to an <see cref="Instant"/>. Exact.</summary>
        public Instant ToInstant() => NodaConstants.UnixEpoch + Duration.FromNanoseconds(source.TotalNanoseconds);
    }

    extension(Instant source) {
        /// <summary>Converts to a <see cref="NanoInstant"/>.</summary>
        /// <exception cref="System.OverflowException">
        /// The instant is outside the range a signed 64-bit nanosecond count can express.
        /// </exception>
        public NanoInstant ToNanoInstant() => new((source - NodaConstants.UnixEpoch).ToInt64Nanoseconds());
    }
}
