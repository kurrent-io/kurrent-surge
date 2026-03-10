namespace Transfermarkt.Domain;

/// <summary>
/// Terms specific to loan transfers.
/// </summary>
[PublicAPI]
public readonly record struct LoanTerms {
    /// <summary>
    /// Represents no loan terms (not a loan).
    /// </summary>
    public static readonly LoanTerms None = new(DateOnly.MinValue, Money.Zero, Money.Zero);

    public LoanTerms(DateOnly loanEndDate, Money buyClause, Money obligationClause) {
        LoanEndDate      = loanEndDate;
        BuyClause        = buyClause;
        ObligationClause = obligationClause;
    }

    /// <summary>
    /// The date when the loan ends.
    /// </summary>
    public DateOnly LoanEndDate { get; init; }

    /// <summary>
    /// Buy clause amount (option to buy). Zero if no buy clause.
    /// </summary>
    public Money BuyClause { get; init; }

    /// <summary>
    /// Obligation clause amount (obligation to buy if conditions are met). Zero if no obligation clause.
    /// </summary>
    public Money ObligationClause { get; init; }

    /// <summary>
    /// Whether this represents actual loan terms.
    /// </summary>
    public bool IsSet => this != None;

    /// <summary>
    /// Whether there is a buy clause.
    /// </summary>
    public bool HasBuyClause => BuyClause.Amount > 0;

    /// <summary>
    /// Whether there is an obligation clause.
    /// </summary>
    public bool HasObligationClause => ObligationClause.Amount > 0;

    /// <summary>
    /// Creates loan terms from the specified values.
    /// </summary>
    public static LoanTerms From(DateOnly loanEndDate, Money? buyClause = null, Money? obligationClause = null) =>
        new(loanEndDate, buyClause ?? Money.Zero, obligationClause ?? Money.Zero);

    public override string ToString() {
        if (!IsSet) return "N/A";

        var parts = new List<string> { $"Until {LoanEndDate}" };

        if (HasBuyClause) parts.Add($"Buy: {BuyClause}");
        if (HasObligationClause) parts.Add($"Obligation: {ObligationClause}");

        return string.Join(", ", parts);
    }
}
