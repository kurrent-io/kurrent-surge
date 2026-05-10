using Kurrent.Client.Streams;
using Kurrent.Sdk.Testkit.Fixtures;

namespace Kurrent.Client.Tests.Secure;

[Category("Secure")]
[Category("Integration")]
public class AuthenticationTests {
    [ClassDataSource<KurrentDBSecureTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBSecureTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask anonymous_client_cannot_append(CancellationToken ct) {
        await using var client = KurrentClient.Create(Fixture.ConnectionString, opts => {
            opts.LoggerFactory = Fixture.LoggerFactory;
            opts.Resilience    = KurrentClientResilienceOptions.NoResilience;
        });

        var req = AppendStreamRequest.For($"secure-auth-{Guid.NewGuid():N}")
            .WithMessage(new TestEvent("no creds"))
            .ExpectingState(ExpectedStreamState.NoStream)
            .Build();

        await Assert.That(async () => await client.Streams.AppendStream(req, ct))
            .ThrowsException();
    }

    record TestEvent(string Description);
}
