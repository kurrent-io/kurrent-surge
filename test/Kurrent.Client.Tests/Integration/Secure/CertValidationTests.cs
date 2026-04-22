using Kurrent.Client.Streams;
using Kurrent.Sdk.Testkit.Fixtures;

namespace Kurrent.Client.Tests.Secure;

[Category("Secure")]
[Category("Integration")]
public class CertValidationTests {
    [ClassDataSource<KurrentDBSecureTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBSecureTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask rejects_server_when_ca_does_not_match(CancellationToken ct) {
        var wrongCa = Path.Combine(Fixture.CertsDirectory!, "user-admin", "user-admin.crt");

        var cs = Fixture.ConnectionString.Replace(
            "tlsVerifyCert=false",
            $"tlsVerifyCert=true&tlsCaFile={Uri.EscapeDataString(wrongCa)}");

        await using var client = KurrentClientOptions.Parse(cs)
            .GetBuilder()
            .WithLoggerFactory(Fixture.LoggerFactory)
            .ConfigureResilience(KurrentClientResilienceOptions.NoResilience)
            .CreateClient();

        var req = AppendStreamRequest.For($"secure-cert-{Guid.NewGuid():N}")
            .WithRecord(new TestEvent("should never reach server"))
            .ExpectingState(ExpectedStreamState.NoStream)
            .Build();

        await Assert.That(async () => await client.Streams.AppendStream(req, ct))
            .ThrowsException();
    }

    record TestEvent(string Description);
}
