namespace Processor.OrderEnrichment.Events;

/// <summary>
/// Event representing a product catalog update (used to build lookup table).
/// </summary>
/// <param name="ProductId">Unique identifier for the product.</param>
/// <param name="Name">Product name.</param>
/// <param name="Category">Product category.</param>
/// <param name="CurrentPrice">Current list price.</param>
/// <param name="UpdatedAt">When the product was updated.</param>
public record ProductUpdated(
    string ProductId,
    string Name,
    string Category,
    decimal CurrentPrice,
    DateTimeOffset UpdatedAt
);
