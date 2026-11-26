using System.Net.Http.Json;
using System.Text.Json;

namespace OrderService.Infrastructure.Payment;

public sealed record PaymentSessionRequest(
    Guid OrderId,
    string CustomerId,
    decimal Amount,
    DateTime OrderCreatedAtUtc,
    string ClientIp);

public sealed record PaymentSessionResponse(
    Guid PaymentId,
    Guid OrderId,
    string PaymentRef,
    string Status,
    DateTime ExpiresAtUtc,
    string PaymentUrl);

public sealed record WalletPaymentResponse(
    Guid PaymentId,
    Guid OrderId,
    string PaymentRef,
    string Status);

public sealed class PaymentClient(HttpClient http)
{
    public async Task<(
        PaymentSessionResponse? Session,
        string? Error)> CreateSessionAsync(
        PaymentSessionRequest request,
        CancellationToken ct)
    {
        try
        {
            using var response =
                await http.PostAsJsonAsync(
                    "api/internal/payments/session",
                    request,
                    ct);

            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode == 409)
                {
                    var body = await response.Content
                        .ReadFromJsonAsync<JsonElement>(
                            cancellationToken: ct);

                    if (body.TryGetProperty(
                            "code", out var code))
                    {
                        return (null, code.GetString());
                    }
                }

                return (null, "PAYMENT_UNAVAILABLE");
            }

            var session = await response.Content
                .ReadFromJsonAsync<PaymentSessionResponse>(
                    cancellationToken: ct);

            if (session is null ||
                session.PaymentId == Guid.Empty ||
                session.OrderId != request.OrderId ||
                string.IsNullOrWhiteSpace(session.PaymentRef) ||
                string.IsNullOrWhiteSpace(session.PaymentUrl))
            {
                return (null, "PAYMENT_UNAVAILABLE");
            }

            return (session, null);
        }
        catch (HttpRequestException)
        {
            return (null, "PAYMENT_UNAVAILABLE");
        }
        catch (OperationCanceledException)
            when (!ct.IsCancellationRequested)
        {
            return (null, "PAYMENT_UNAVAILABLE");
        }
        catch (JsonException)
        {
            return (null, "PAYMENT_UNAVAILABLE");
        }
    }

    public async Task<(
        WalletPaymentResponse? Payment,
        string? Error)> PayWithWalletAsync(
        PaymentSessionRequest request,
        CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                "api/internal/payments/wallet",
                request, ct);

            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode == 409)
                {
                    var body = await response.Content
                        .ReadFromJsonAsync<JsonElement>(
                            cancellationToken: ct);

                    if (body.TryGetProperty("code", out var code))
                        return (null, code.GetString());
                }

                return (null, "PAYMENT_UNAVAILABLE");
            }

            var payment = await response.Content
                .ReadFromJsonAsync<WalletPaymentResponse>(
                    cancellationToken: ct);

            if (payment is null ||
                payment.PaymentId == Guid.Empty ||
                payment.OrderId != request.OrderId ||
                payment.PaymentRef != $"W{request.OrderId:N}" ||
                payment.Status != "SUCCEEDED")
            {
                return (null, "PAYMENT_UNAVAILABLE");
            }

            return (payment, null);
        }
        catch (HttpRequestException)
        {
            return (null, "PAYMENT_UNAVAILABLE");
        }
        catch (OperationCanceledException)
            when (!ct.IsCancellationRequested)
        {
            return (null, "PAYMENT_UNAVAILABLE");
        }
        catch (JsonException)
        {
            return (null, "PAYMENT_UNAVAILABLE");
        }
    }

    public async Task<(
        WalletPaymentResponse? Payment,
        string? Error)> FindWalletPaymentAsync(
        PaymentSessionRequest request,
        CancellationToken ct)
    {
        try
        {
            using var response =
                await http.PostAsJsonAsync(
                    "api/internal/payments/wallet/status",
                    request, ct);

            if ((int)response.StatusCode == 404)
            {
                var body = await response.Content
                    .ReadFromJsonAsync<JsonElement>(
                        cancellationToken: ct);

                if (body.TryGetProperty("code", out var code) &&
                    code.GetString() == "PAYMENT_NOT_FOUND")
                {
                    return (null, "PAYMENT_NOT_FOUND");
                }

                return (null, "PAYMENT_UNAVAILABLE");
            }

            if (!response.IsSuccessStatusCode)
                return (null, "PAYMENT_UNAVAILABLE");

            var payment = await response.Content
                .ReadFromJsonAsync<WalletPaymentResponse>(
                    cancellationToken: ct);

            if (payment is null ||
                payment.PaymentId == Guid.Empty ||
                payment.OrderId != request.OrderId ||
                payment.PaymentRef != $"W{request.OrderId:N}" ||
                payment.Status != "SUCCEEDED")
            {
                return (null, "PAYMENT_UNAVAILABLE");
            }

            return (payment, null);
        }
        catch (HttpRequestException)
        {
            return (null, "PAYMENT_UNAVAILABLE");
        }
        catch (OperationCanceledException)
            when (!ct.IsCancellationRequested)
        {
            return (null, "PAYMENT_UNAVAILABLE");
        }
        catch (JsonException)
        {
            return (null, "PAYMENT_UNAVAILABLE");
        }
    }
}
