namespace Kurrent.Surge.Schema.Catalogs.Generators.Tests.Samples.SmartHome;

/// <summary>A device was paired to the home hub and assigned to a room.</summary>
public record DevicePaired(string DeviceId, string Model, string Room, DateTimeOffset PairedAt);
