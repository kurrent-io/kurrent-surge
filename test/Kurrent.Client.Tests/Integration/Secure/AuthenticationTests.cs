using Kurrent.Client.Streams;
using Kurrent.SDK.Testing.Fixtures;

namespace Kurrent.Client.Tests.Secure;

[Category("Secure")]
[Category("Integration")]
public class AuthenticationTests {
    [ClassDataSource<KurrentDBSecureTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBSecureTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask anonymous_client_cannot_append(CancellationToken ct) {
        await using var client = KurrentClientOptions.Parse(Fixture.AnonymousConnectionString)
            .GetBuilder()
            .WithLoggerFactory(Fixture.LoggerFactory)
            .ConfigureResilience(KurrentClientResilienceOptions.NoResilience)
            .CreateClient();

        var req = AppendStreamRequest.For($"secure-auth-{Guid.NewGuid():N}")
            .WithRecord(new TestEvent("no creds"))
            .ExpectingState(ExpectedStreamState.NoStream)
            .Build();

        await Assert.That(async () => await client.Streams.AppendStream(req, ct))
            .ThrowsException();
    }

    record TestEvent(string Description);
}
