namespace Kurrent.Surge.Client.Streams;

static class EpochExtensions {
	static readonly DateTime UnixEpoch = DateTime.UnixEpoch;

	extension(long value) {
        public DateTime FromTicksSinceEpoch() => new(UnixEpoch.Ticks + value, DateTimeKind.Utc);
    }

	extension(DateTime value) {
        public long ToTicksSinceEpoch() => (value - UnixEpoch).Ticks;
    }
}
