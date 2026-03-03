namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer;

/// <summary>Lightweight read-only projection of a <see cref="Car"/> for use in events.</summary>
/// <param name="CarId">Unique car identifier.</param>
/// <param name="Name">Generated car name.</param>
/// <param name="Number">Grid number.</param>
/// <param name="Pilot">Pilot name in abbreviated form (e.g. "J. Smith").</param>
/// <param name="Spec">Car performance attributes.</param>
public record struct CarInfo(Guid CarId, string Name, int Number, string Pilot, CarSpec Spec);