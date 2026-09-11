using Kurrent.Surge.Client;

namespace ClientBenchmark;

sealed record BenchmarkOptions(string ConnectionString, string Stream, int Events, int PayloadSize, bool NoResilience) {
    public const string SchemaName = "BenchmarkEvent";

    public byte[] CreatePayload() {
        var payload = new byte[PayloadSize];
        payload.AsSpan().Fill((byte)'x');
        return payload;
    }

    public void Configure(KurrentClientOptions options) {
        if (NoResilience)
            options.Resilience = KurrentClientResilienceOptions.NoResilience;
    }
}
