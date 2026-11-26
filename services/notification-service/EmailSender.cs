using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace Notification.Worker;

public sealed class EmailSender(IConfiguration config)
{
    public async Task SendAsync(
        NotificationMessage notification,
        CancellationToken ct)
    {
        var host = config["Smtp:Host"] ?? "smtp.gmail.com";
        var port = config.GetValue("Smtp:Port", 587);

        var username = config["Smtp:Username"];
        var password = config["Smtp:Password"];
        var from = config["Smtp:FromEmail"];
        var fromName = config["Smtp:FromName"]
            ?? "Digital License Marketplace";

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(from) ||
            !config.GetValue<bool>("Smtp:EnableSsl"))
        {
            throw new InvalidOperationException(
                "SMTP configuration is incomplete");
        }

        var amount = notification.Amount.ToString(
            "N0", CultureInfo.GetCultureInfo("vi-VN"));

        using var message = new MailMessage
        {
            From = new MailAddress(from, fromName),
            IsBodyHtml = false,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8
        };

        message.To.Add(notification.RecipientEmail);

        if (notification.Type == "COMPLETED")
        {
            message.Subject =
                "Đơn hàng của bạn đã hoàn tất";

            message.Body = $"""
                Xin chào,

                Đơn hàng của bạn đã hoàn tất.

                Mã đơn hàng: {notification.OrderId}
                Số tiền: {amount} VND

                License Key đã sẵn sàng.
                Vui lòng đăng nhập Digital License Marketplace
                và truy cập đơn hàng để nhận License Key.

                Cảm ơn bạn đã mua hàng!
                """;
        }
        else if (notification.Type == "REFUNDED")
        {
            message.Subject =
                "Đơn hàng đã được hoàn tiền";

            message.Body = $"""
                Xin chào,

                Đơn hàng của bạn đã được hoàn tiền thành công.

                Mã đơn hàng: {notification.OrderId}
                Số tiền hoàn lại: {amount} VND

                Số tiền đã được cộng vào Marketplace Wallet.
                Bạn có thể sử dụng số dư cho các lần mua tiếp theo.

                Digital License Marketplace
                """;
        }
        else
        {
            throw new InvalidDataException(
                "Unsupported notification type");
        }

        using var smtp = new SmtpClient(host, port)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(
                username, password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(ct);

        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        await smtp.SendMailAsync(message, timeout.Token);
    }
}
