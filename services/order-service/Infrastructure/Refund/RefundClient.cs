using System.Net.Http.Json;
using System.Text.Json;
using OrderService.Domain.Entities;

namespace OrderService.Infrastructure.Refund;

public sealed record LicenseGuardResponse(
    Guid OrderId,
    Guid RequestId,
    string Status);

public sealed record RefundCreditResponse(
    Guid RefundId,
    Guid RequestId,
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Status,
    DateTime? CompletedAtUtc);

public sealed class RefundClient(
    IHttpClientFactory clients)
{
    private async Task<(T? Result, string? Error)>
        SendAsync<T>(
            string client,
            string path,
            object body,
            CancellationToken ct)
        where T : class
    {
        try
        {
            using var response = await clients
                .CreateClient(client)
                .PostAsJsonAsync(path, body, ct);

            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode is 400 or 404 or 409)
                {
                    var error = await response.Content
                        .ReadFromJsonAsync<JsonElement>(
                            cancellationToken: ct);

                    if (error.TryGetProperty("code", out var code))
                        return (null, code.GetString());
                }

                return (null, "REFUND_SERVICE_UNAVAILABLE");
            }

            var result = await response.Content
                .ReadFromJsonAsync<T>(cancellationToken: ct);

            return result is null
                ? (null, "INVALID_REFUND_RESPONSE")
                : (result, null);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException ||
            (ex is OperationCanceledException &&
             !ct.IsCancellationRequested))
        {
            return (null, "REFUND_SERVICE_UNAVAILABLE");
        }
    }

    public Task<(LicenseGuardResponse? Result, string? Error)>
        PrepareAsync(
            RefundOperation operation,
            CancellationToken ct)
        => SendAsync<LicenseGuardResponse>(
            "RefundLicense",
            "api/internal/license-refunds/prepare",
            new
            {
                operation.RequestId,
                operation.OrderId,
                operation.CustomerId,
                operation.Mode,
                operation.DefectiveLicenseId
            },
            ct);

    public Task<(RefundCreditResponse? Result, string? Error)>
        CreditAsync(
            RefundOperation operation,
            decimal expectedAmount,
            CancellationToken ct)
        => SendAsync<RefundCreditResponse>(
            "RefundPayment",
            "api/internal/refunds/wallet",
            new
            {
                operation.RequestId,
                operation.OrderId,
                operation.CustomerId,
                operation.PaymentRef,
                ExpectedAmount = expectedAmount,
                operation.Reason,
                operation.RequestedBy
            },
            ct);

    public Task<(LicenseGuardResponse? Result, string? Error)>
        FinalizeAsync(
            RefundOperation operation,
            CancellationToken ct)
        => SendAsync<LicenseGuardResponse>(
            "RefundLicense",
            "api/internal/license-refunds/finalize",
            new
            {
                operation.OrderId,
                operation.RequestId
            },
            ct);
}
