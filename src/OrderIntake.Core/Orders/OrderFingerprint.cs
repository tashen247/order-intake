// SHA-256 fingerprint of a normalised order submission. Same reference + same fingerprint is a
// harmless resubmit; same reference + different fingerprint is a clash that must be refused.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OrderIntake.Core.Orders;

/// <summary>
/// A stable hash of what the user actually submitted, after normalisation. Two submissions with
/// the same reference and the same fingerprint are the same order sent twice (a retry or a
/// double-click); the same reference with a different fingerprint is a genuine clash.
/// </summary>
public static class OrderFingerprint
{
    public static string Compute(ValidOrder order)
    {
        // Line order is not meaningful to the business, so sort it out of the hash.
        var canonical = new
        {
            r = order.ExternalReference.ToUpperInvariant(),
            ci = order.Customer.Id.ToUpperInvariant(),
            e = order.Customer.Email,
            n = order.Customer.Name,
            c = order.Currency,
            o = order.Notes,
            i = order.Items
                .OrderBy(x => x.Sku, StringComparer.OrdinalIgnoreCase)
                .Select(x => new { s = x.Sku.ToUpperInvariant(), n = x.Name, q = x.Quantity, p = x.UnitPrice.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) }),
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonical);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
