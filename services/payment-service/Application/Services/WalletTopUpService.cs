using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaymentService.Domain.Entities;
using PaymentService.Infrastructure.Persistence;
using PaymentService.Infrastructure.VnPay;

namespace PaymentService.Application.Services;

public sealed record WalletTopUpSessionResponse(
    Guid TopUpId,
    string PaymentRef,
    decimal Amount,
    string Status,
    DateTime ExpiresAtUtc,
    string PaymentUrl);

public sealed record WalletTopUpStatusResponse(
    Guid TopUpId,
    string PaymentRef,
    decimal Amount,
    string Status,
    string? VnpTransactionNo,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc,
    decimal WalletBalance,
    string Currency);

public sealed class WalletTopUpService(
    PaymentDbContext db,
    VnPayService vnpay,
    WalletService wallets,
    IOptions<VnPayOptions> options)
{
    public const string RefPrefix = "TOPUP";
    public const decimal MinAmountVnd = 10_000m;
    public const decimal MaxAmountVnd = 50_000_000m;

    private static string NormalizeClientIp(string? rawIp)
    {
        if (!string.IsNullOrWhiteSpace(rawIp))
        {
            var candidate = rawIp.Split(',')[0].Trim();
            if (IPAddress.TryParse(candidate, out var ip))
            {
                if (ip.IsIPv4MappedToIPv6)
                    ip = ip.MapToIPv4();
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return ip.ToString();
            }
        }

        return "127.0.0.1";
    }

    public async Task<(WalletTopUpSessionResponse? Session, string? Error)> CreateAsync(
        string customerId,
        decimal amount,
        string? rawClientIp,
        CancellationToken ct)
    {
        if (!vnpay.IsConfigured)
            return (null, "PAYMENT_NOT_CONFIGURED");

        if (string.IsNullOrWhiteSpace(customerId) || customerId.Length > 64)
            return (null, "INVALID_CUSTOMER");

        if (amount < MinAmountVnd ||
            amount > MaxAmountVnd ||
            decimal.Round(amount, 0) != amount)
        {
            return (null, "INVALID_TOPUP_AMOUNT");
        }

        var minutes = options.Value.ExpireMinutes;
        if (minutes <= 0 || minutes > 1440)
            return (null, "INVALID_PAYMENT_CONFIGURATION");

        var now = DateTime.UtcNow;
        var topUp = new WalletTopUp
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Amount = amount,
            PaymentRef = RefPrefix + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant(),
            Status = PaymentStatus.Pending,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(minutes)
        };

        db.WalletTopUps.Add(topUp);
        await db.SaveChangesAsync(ct);

        var clientIp = NormalizeClientIp(rawClientIp);
        var url = vnpay.CreateWalletTopUpUrl(topUp, clientIp);

        return (new WalletTopUpSessionResponse(
            topUp.Id,
            topUp.PaymentRef,
            topUp.Amount,
            topUp.Status,
            topUp.ExpiresAtUtc,
            url), null);
    }

    public async Task<WalletTopUpStatusResponse?> GetByRefAsync(
        string customerId,
        string paymentRef,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(paymentRef) || paymentRef.Length > 64)
            return null;

        var topUp = await db.WalletTopUps
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.PaymentRef == paymentRef && x.CustomerId == customerId,
                ct);

        if (topUp is null)
            return null;

        var balance = await wallets.GetBalanceAsync(customerId, ct);

        return new WalletTopUpStatusResponse(
            topUp.Id,
            topUp.PaymentRef,
            topUp.Amount,
            topUp.Status,
            topUp.VnpTransactionNo,
            topUp.CreatedAtUtc,
            topUp.PaidAtUtc,
            balance.Balance,
            balance.Currency);
    }
}
