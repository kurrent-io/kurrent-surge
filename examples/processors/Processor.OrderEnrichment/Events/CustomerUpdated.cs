namespace Processor.OrderEnrichment.Events;

/// <summary>
/// Event representing a customer profile update (used to build lookup table).
/// </summary>
/// <param name="CustomerId">Unique identifier for the customer.</param>
/// <param name="Name">Customer's full name.</param>
/// <param name="Email">Customer's email address.</param>
/// <param name="Tier">Customer tier (Bronze, Silver, Gold, Platinum).</param>
/// <param name="UpdatedAt">When the profile was updated.</param>
public record CustomerUpdated(
    string CustomerId,
    string Name,
    string Email,
    string Tier,
    DateTimeOffset UpdatedAt
);
