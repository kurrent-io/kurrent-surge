namespace Kurrent.Surge.Codecs.MessagePack.Tests;

[MessagePackObject]
public record TestOrder {
    [Key(0)] public required Guid    OrderId  { get; init; }
    [Key(1)] public required string  Sku      { get; init; }
    [Key(2)] public required decimal Price    { get; init; }
    [Key(3)] public required int     Quantity { get; init; }
}

[MessagePackObject]
public record TestCustomer {
    [Key(0)] public required string Name     { get; init; }
    [Key(1)] public required string Email    { get; init; }
    [Key(2)] public required bool   IsActive { get; init; }
}

[MessagePackObject]
public record TestInvoiceLine {
    [Key(0)] public required string  Description { get; init; }
    [Key(1)] public required decimal UnitPrice   { get; init; }
    [Key(2)] public required int     Quantity    { get; init; }
}

// Deliberately undecorated — no [MessagePackObject], so no formatter (generated or built-in)
// covers it. Used to prove the serializer fails loud instead of falling back to Reflection.Emit.
public record TestUnformattedMessage(string Value);

[GeneratedMessagePackResolver]
public partial class TestMessagesResolver;
