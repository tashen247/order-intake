// Money rules in one place: supported currencies (all with 2 minor units), commercial rounding
// (midpoint away from zero) for line totals, and a helper to count significant decimal places
// so prices with more than 2 decimals can be rejected rather than silently rounded.

namespace OrderIntake.Core.Domain;

public static class Money
{
    /// <summary>Currencies the app accepts. All use two minor units, which keeps rounding uniform.</summary>
    public static readonly IReadOnlySet<string> SupportedCurrencies =
        new HashSet<string>(StringComparer.Ordinal) { "ZAR", "USD", "EUR", "GBP" };

    public const int MinorUnits = 2;

    /// <summary>Banker's rounding surprises people on invoices; commercial rounding is what finance expects.</summary>
    public static decimal Round(decimal amount) =>
        Math.Round(amount, MinorUnits, MidpointRounding.AwayFromZero);

    public static decimal LineTotal(int quantity, decimal unitPrice) => Round(quantity * unitPrice);

    /// <summary>Significant decimal places, ignoring trailing zeros (10.50m → 1, 10.505m → 3).</summary>
    public static int DecimalPlaces(decimal value)
    {
        // Dividing by a 1 with many trailing zeros normalises the scale (strips trailing zeros).
        var normalised = value / 1.000000000000000000000000000000000m;
        return (decimal.GetBits(normalised)[3] >> 16) & 0xFF;
    }
}
