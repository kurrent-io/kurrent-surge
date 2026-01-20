namespace Kurrent.Client.Tests.Processors.Leases;

public static class Identifiers {
	public static string GenerateShortId(string prefix) =>
		$"{prefix}-{Guid.NewGuid():N}"[..16];
}
