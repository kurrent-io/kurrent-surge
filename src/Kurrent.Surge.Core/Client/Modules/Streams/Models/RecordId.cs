namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// Identifies a record: time-ordered, cheap to mint, and lexicographically sortable —
/// <see cref="ToString"/> renders the 26-character sortable form.
/// </summary>
/// <remarks>
/// The wire speaks GUID-format strings, produced at the mapping via <see cref="ToGuid"/>.
/// <see cref="Guid.NewGuid"/> pays a per-call OS entropy read (~250 ns measured on macOS);
/// the generated minting uses buffered randomness (~30 ns) and its timestamp prefix survives
/// the Guid conversion, giving the server index locality.
/// </remarks>
[PublicAPI]
[UniqueIdentifier]
public readonly partial record struct RecordId;
