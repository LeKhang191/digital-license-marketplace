namespace OrderService.Infrastructure.VnPay;

public sealed class VnPayOptions
{
    public string TmnCode { get; set; } = "";
    public string HashSecret { get; set; } = "";
    public string PayUrl { get; set; } =
        "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";

    public string ReturnUrl { get; set; } =
        "http://localhost:5173/payment/result";

    public string Locale { get; set; } = "vn";
    public int ExpireMinutes { get; set; } = 15;
}
