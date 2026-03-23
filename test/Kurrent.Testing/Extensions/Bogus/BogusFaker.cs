// ReSharper disable CheckNamespace

namespace Bogus;

/// <summary>
/// In order to use Faker as a TUnit ClassDataSource we must have a parameterless ctor.
/// </summary>
public class BogusFaker() : Faker("en") { }
