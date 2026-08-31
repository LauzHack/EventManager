namespace EventManager.Models;

/// <summary>
/// Currency, including an exchange rate to the currency used by the event.
/// </summary>
public sealed class Currency(string code, decimal exchangeRate)
{
    /// <summary>
    /// The currency code, such as CHF.
    /// </summary>
    public string Code { get; private set; } = code;

    /// <summary>
    /// The exchange rate, implicitly to whichever currency the event uses.
    /// </summary>
    public decimal ExchangeRate { get; set; } = exchangeRate;
}