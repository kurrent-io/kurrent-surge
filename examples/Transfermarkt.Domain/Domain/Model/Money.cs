namespace Transfermarkt.Domain;

/// <summary>
/// Monetary value with currency.
/// </summary>
[PublicAPI]
public readonly record struct Money : IComparable<Money>, IComparable {
    /// <summary>
    /// Represents zero money (in EUR).
    /// </summary>
    public static readonly Money Zero = new(0, Currency.EUR);

    public Money(decimal amount, Currency currency) {
        Amount   = amount;
        Currency = currency;
    }

    /// <summary>
    /// The monetary amount.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// The currency.
    /// </summary>
    public Currency Currency { get; init; }

    /// <summary>
    /// Whether this represents zero money.
    /// </summary>
    public bool IsZero => Amount == 0;

    /// <summary>
    /// Creates a Money instance from an amount and currency.
    /// </summary>
    public static Money From(decimal amount, Currency? currency = null) => new(amount, currency ?? Currency.EUR);

    /// <summary>
    /// Creates a Money instance in Euros.
    /// </summary>
    public static Money InEuros(decimal amount) => new(amount, Currency.EUR);

    /// <summary>
    /// Creates a Money instance in British Pounds.
    /// </summary>
    public static Money InPounds(decimal amount) => new(amount, Currency.GBP);

    /// <summary>
    /// Creates a Money instance in US Dollars.
    /// </summary>
    public static Money InDollars(decimal amount) => new(amount, Currency.USD);

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount, a.Currency);
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount, a.Currency);

    public override string ToString() => Currency.Format(Amount);

    #region . relational members .

    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);

    public int CompareTo(object? obj) =>
        obj switch {
            null        => 1,
            Money other => CompareTo(other),
            _           => throw new ArgumentException($"Object must be of type {nameof(Money)}")
        };

    public static bool operator <(Money left, Money right)  => left.CompareTo(right) < 0;
    public static bool operator >(Money left, Money right)  => left.CompareTo(right) > 0;
    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;
    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    #endregion
}
