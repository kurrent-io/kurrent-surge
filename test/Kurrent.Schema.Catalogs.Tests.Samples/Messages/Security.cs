namespace Kurrent.Schema.Catalogs.Tests.Samples.SmartHome.Security;

/// <summary>A motion sensor was triggered in a room.</summary>
public record MotionDetected(string DeviceId, string Room, DateTimeOffset DetectedAt);

/// <summary>A smart lock engaged.</summary>
public record DoorLocked(string DeviceId, string Room);

/// <summary>A smart lock disengaged, attributed to whoever unlocked it.</summary>
public record DoorUnlocked(string DeviceId, string Room, string UnlockedBy);
