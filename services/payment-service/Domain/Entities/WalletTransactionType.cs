namespace PaymentService.Domain.Entities;

public static class WalletTransactionType
{
    public const string Refund = "REFUND";
    public const string Purchase = "PURCHASE";
    public const string TopUp = "TOPUP";
}
