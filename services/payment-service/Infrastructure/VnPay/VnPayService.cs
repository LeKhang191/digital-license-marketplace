using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PaymentService.Domain.Entities;

namespace PaymentService.Infrastructure.VnPay;

public sealed class VnPayService(IOptions<VnPayOptions> options)
{
    private VnPayOptions O => options.Value;

    public bool IsConfigured =>
    !string.IsNullOrWhiteSpace(O.TmnCode) &&
    !string.IsNullOrWhiteSpace(O.HashSecret) &&
    Uri.TryCreate(O.PayUrl, UriKind.Absolute, out var uri) &&
    uri.Scheme == Uri.UriSchemeHttps &&
    !string.IsNullOrWhiteSpace(O.ReturnUrl);

    public static long ToVnpAmount(decimal total) =>
        (long)Math.Round(total, 0, MidpointRounding.AwayFromZero) * 100;

    public bool IsPaymentExpired(Payment payment) =>
        DateTime.UtcNow >=
            payment.ExpiresAtUtc;

    public string CreatePaymentUrl(Payment payment, string clientIp)
    {
        var now = payment.CreatedAtUtc.AddHours(7);

        var p = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Version"] = "2.1.0",
            ["vnp_Command"] = "pay",
            ["vnp_TmnCode"] = O.TmnCode,
            ["vnp_Amount"] = ToVnpAmount(payment.Amount)
                .ToString(CultureInfo.InvariantCulture),
            ["vnp_CreateDate"] = Fmt(now),
            ["vnp_ExpireDate"] = Fmt(payment.ExpiresAtUtc.AddHours(7)),
            ["vnp_CurrCode"] = "VND",
            ["vnp_IpAddr"] = clientIp,
            ["vnp_Locale"] = O.Locale,
            ["vnp_OrderInfo"] = $"Thanh toan don hang {payment.OrderId:N}",
            ["vnp_OrderType"] = "other",
            ["vnp_ReturnUrl"] = O.ReturnUrl,
            ["vnp_TxnRef"] = payment.PaymentRef
                ?? throw new InvalidOperationException("PaymentRef is empty")
        };

        var query = BuildQuery(p);
        return $"{O.PayUrl.TrimEnd('?')}?{query}&vnp_SecureHash={Hmac(query)}";
    }

    public bool ValidateMerchant(string tmnCode) =>
        !string.IsNullOrWhiteSpace(tmnCode) &&
        string.Equals(
            O.TmnCode,
            tmnCode,
            StringComparison.Ordinal);

    public bool ValidateSignature(IReadOnlyDictionary<string, string> query)
    {
        if (!query.TryGetValue("vnp_SecureHash", out var received) ||
            string.IsNullOrEmpty(received)) return false;

        var data = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in query)
        {
            if (!key.StartsWith("vnp_", StringComparison.Ordinal)) continue;
            if (key is "vnp_SecureHash" or "vnp_SecureHashType") continue;
            data[key] = value;
        }

        var expected = Hmac(BuildQuery(data));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(received.ToLowerInvariant()));
    }

    private static string Fmt(DateTime t) =>
        t.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    private static string BuildQuery(SortedDictionary<string, string> data) =>
        string.Join("&", data
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => $"{WebUtility.UrlEncode(kv.Key)}=" +
                          $"{WebUtility.UrlEncode(kv.Value)}"));

    private string Hmac(string data)
    {
        using var h = new HMACSHA512(Encoding.UTF8.GetBytes(O.HashSecret));
        return Convert.ToHexString(
            h.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }
}
