using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Secure;

[Category("Secure")]
[Category("Integration")]
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

        var req = AppendStreamRequest.For($"secure-auth-{Guid.NewGuid():N}")
            .AddMessage(new TestEvent("no creds"))
            .ExpectedState(ExpectedStreamState.NoStream)
            .Build();

        await Assert.That(async () => await client.Streams.AppendStream(req, ct))
            .ThrowsException();
    }

    record TestEvent(string Description);
}
