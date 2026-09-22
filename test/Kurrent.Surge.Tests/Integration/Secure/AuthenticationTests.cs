using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Tests.Secure;

[Category("Secure")]
[Category("Integration")]
[Timeout(10_000)]
public class AuthenticationTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    [Skip("KurrentDBSecureFixture currently runs the same insecure image as the base fixture — licensed auth features are not enabled, so anonymous clients are not rejected. Re-enable when the secure fixture enforces authentication.")]
    public async ValueTask anonymous_client_cannot_append(CancellationToken ct) {
        await using var client = KurrentClient.Create(Fixture.AnonymousConnectionString, opts => {
            opts.LoggerFactory = Fixture.LoggerFactory;
            opts.Resilience    = KurrentClientResilienceOptions.NoResilience;
        });

        var stream = $"secure-auth-{Guid.NewGuid():N}";
        var record = AppendRecord.Create(new TestEvent("no creds"), r => r with { Stream = stream });

        await Assert.That(async () => await client.Streams.AppendStream(stream, [record], ExpectedStreamState.NoStream, ct))
            .ThrowsException();
    }

    record TestEvent(string Description);
}
