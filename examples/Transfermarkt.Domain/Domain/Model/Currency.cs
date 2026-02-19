
// ReSharper disable InconsistentNaming

namespace Transfermarkt.Domain;

/// <summary>
/// Currency using ISO 4217 codes.
/// </summary>
[PublicAPI]
public readonly record struct Currency {
    /// <summary>
    /// Euro - Primary currency for European football transfers.
    /// </summary>
    public static readonly Currency EUR = new("EUR", "Euro", "€");

    /// <summary>
    /// British Pound Sterling.
    /// </summary>
    public static readonly Currency GBP = new("GBP", "British Pound", "£");

    /// <summary>
    /// United States Dollar.
    /// </summary>
    public static readonly Currency USD = new("USD", "US Dollar", "$");

    /// <summary>
    /// Swiss Franc.
    /// </summary>
    public static readonly Currency CHF = new("CHF", "Swiss Franc", "CHF");

    /// <summary>
    /// Japanese Yen.
    /// </summary>
    public static readonly Currency JPY = new("JPY", "Japanese Yen", "¥");

    /// <summary>
    /// All defined currencies.
    /// </summary>
    public static readonly IReadOnlyList<Currency> All = [EUR, GBP, USD, CHF, JPY];

    static readonly Dictionary<string, Currency> ByCode = All.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

    public Currency(string code, string name, string symbol) {
        Code   = code;
        Name   = name;
        Symbol = symbol;
    }

    /// <summary>
    /// ISO 4217 currency code (e.g., "EUR", "GBP", "USD").
    /// </summary>
    public string Code { get; init; }

    /// <summary>
    /// Full currency name.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Currency symbol (e.g., "€", "£", "$").
    /// </summary>
    public string Symbol { get; init; }

    /// <summary>
    /// Gets a currency by its ISO code.
    /// </summary>
    public static Currency? FromCode(string code) => ByCode.TryGetValue(code, out var currency) ? currency : null;

    /// <summary>
    /// Formats an amount in this currency.
    /// </summary>
    public string Format(decimal amount) => $"{Symbol}{amount:N0}";

    /// <summary>
    /// Formats an amount in this currency with decimal places.
    /// </summary>
    public string Format(decimal amount, int decimals) => $"{Symbol}{amount.ToString($"N{decimals}")}";

    public override string ToString() => Code;
}
