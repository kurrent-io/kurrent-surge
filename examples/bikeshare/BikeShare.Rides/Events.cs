using Kurrent.Surge.Schema.Catalogs;

[assembly: MessageCatalog("BikeShare.Rides", AlwaysActive = true)]

namespace BikeShare.Rides;

public record BikeUnlocked(string BikeId, string RiderId);
public record RideStarted(string RiderId, string BikeId);

public record BikeReturned(string BikeId, string RiderId, int Minutes);
public record RideEnded(string RiderId, string BikeId, int Minutes, decimal Fare);
