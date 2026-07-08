using Kurrent.Client.Legacy;
using KurrentDB.Client;

namespace Kurrent.Client.Tests.Legacy;

[Category("Legacy")]
public class UserCredentialsTests {
	[Test]
	public async Task anonymous_connection_produces_safe_empty_credentials() {
		var settings = KurrentClientOptions
			.Parse("kurrentdb://localhost:2113?tls=false")
			.ConvertToLegacySettings();

		await Assert.That(settings.DefaultCredentials).IsNotNull();
		await Assert.That(settings.DefaultCredentials).IsEqualTo(UserCredentials.Empty);
		await Assert.That(settings.DefaultCredentials.ToString()).IsEqualTo("");
	}

	[Test]
	public async Task authenticated_connection_produces_real_credentials() {
		var settings = KurrentClientOptions
			.Parse("kurrentdb://admin:changeit@localhost:2113?tls=false")
			.ConvertToLegacySettings();

		await Assert.That(settings.DefaultCredentials).IsNotEqualTo(UserCredentials.Empty);
		await Assert.That(settings.DefaultCredentials.Username).IsEqualTo("admin");
		await Assert.That(settings.DefaultCredentials.Password).IsEqualTo("changeit");
	}
}
