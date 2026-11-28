using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SupportService.Application.DTOs;
using SupportService.Domain.Entities;
using SupportService.Infrastructure.Persistence;

namespace SupportService.Controllers;

[ApiController]
[Authorize]
[Route("api/support/tickets")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SupportController(
    SupportDbContext db,
    IHttpClientFactory clients,
    IConfiguration config,
    ILogger<SupportController> log
) : ControllerBase
{
    private string? UserId => User.FindFirst("sub")?.Value;

    private bool IsCustomer =>
        User.IsInRole("customer") &&
        !User.IsInRole("catalog_manager") &&
        !User.IsInRole("license_coordinator") &&
        !User.IsInRole("transaction_auditor") &&
        !User.IsInRole("admin");

    private bool IsStaff =>
        User.IsInRole("license_coordinator") ||
        User.IsInRole("transaction_auditor") ||
        User.IsInRole("catalog_manager");

    private bool CanAccess =>
        !string.IsNullOrWhiteSpace(UserId) &&
        (IsCustomer || IsStaff);

    public sealed record CategoryMeta(
        string Code,
        string Label,
        string Description,
        string AssignedRole,
        string AssignedRoleLabel,
        bool RequiresOrder,
        bool RequiresLicense,
        string RefundMode);

    private static readonly IReadOnlyList<CategoryMeta> CategoryCatalog =
    [
        new("REFUND_REQUEST",
            "Hoàn tiền đơn chưa mở key",
            "Yêu cầu hoàn tiền về Ví DLM cho đơn hàng đã mua trong vòng 7 ngày và chưa xem key.",
            "transaction_auditor", "Kiểm toán giao dịch",
            true, false, "UNREVEALED"),
        new("INVALID_KEY",
            "Key lỗi / không kích hoạt được",
            "Báo cáo key đã xem nhưng không hợp lệ hoặc gặp lỗi khi kích hoạt trên Steam để đổi key mới hoặc hoàn tiền.",
            "license_coordinator", "Điều phối kho License",
            true, true, "VERIFIED_DEFECT"),
        new("KEY_ALREADY_USED",
            "Key đã có người sử dụng",
            "Báo cáo key đã bị kích hoạt trước đó (Duplicate Product Code) để xác minh với nhà cung cấp và đổi/hoàn tiền.",
            "license_coordinator", "Điều phối kho License",
            true, true, "VERIFIED_DEFECT"),
        new("KEY_NOT_RECEIVED",
            "Đã thanh toán nhưng chưa nhận key",
            "Đơn đã thanh toán thành công nhưng quá lâu chưa được cấp key vào tài khoản.",
            "license_coordinator", "Điều phối kho License",
            true, false, "NONE"),
        new("PAYMENT_ISSUE",
            "Sự cố thanh toán đơn hàng",
            "Lỗi khi thanh toán qua VNPay / Ví DLM, trừ tiền nhưng đơn chưa cập nhật.",
            "transaction_auditor", "Kiểm toán giao dịch",
            true, false, "NONE"),
        new("WALLET_TOPUP_ISSUE",
            "Sự cố nạp tiền Ví DLM (VNPay)",
            "Đã thanh toán nạp tiền qua VNPay nhưng số dư Ví DLM chưa cộng hoặc sai lệch.",
            "transaction_auditor", "Kiểm toán giao dịch",
            false, false, "NONE"),
        new("WRONG_PRODUCT",
            "Sai game / sai khu vực kích hoạt",
            "Phản ánh sản phẩm nhận được không đúng phiên bản, nền tảng hoặc vùng kích hoạt như mô tả.",
            "catalog_manager", "Quản lý danh mục",
            true, false, "NONE"),
        new("CATALOG_INFO",
            "Hỏi đáp / báo lỗi thông tin sản phẩm",
            "Hỏi đáp về cấu hình, khu vực kích hoạt hoặc góp ý chỉnh sửa thông tin game trên cửa hàng.",
            "catalog_manager", "Quản lý danh mục",
            false, false, "NONE"),
        new("OTHER",
            "Vấn đề hỗ trợ khác",
            "Các yêu cầu hỗ trợ chung khác không thuộc nhóm trên.",
            "transaction_auditor", "Kiểm toán giao dịch",
            false, false, "NONE"),
    ];

    private static readonly Dictionary<string, CategoryMeta> CategoriesByCode =
        CategoryCatalog.ToDictionary(c => c.Code, StringComparer.Ordinal);

    private sealed record OwnedOrderItem(
        Guid Id,
        Guid ProductId
    );

    private sealed record OwnedOrder(
        Guid Id,
        string Status,
        List<OwnedOrderItem> Items
    );

    private sealed record OwnedLicense(
        Guid Id,
        Guid OrderId,
        Guid OrderItemId,
        Guid ProductId
    );

    private IQueryable<SupportTicket> AccessibleTickets()
    {
        var query = db.SupportTickets.AsQueryable();

        if (IsCustomer)
            return query.Where(x => x.CustomerId == UserId);

        var coordinator = User.IsInRole("license_coordinator");
        var auditor = User.IsInRole("transaction_auditor");
        var catalog = User.IsInRole("catalog_manager");

        return query.Where(x =>
            (coordinator && x.AssignedRole == "license_coordinator") ||
            (auditor && x.AssignedRole == "transaction_auditor") ||
            (catalog && x.AssignedRole == "catalog_manager"));
    }

    private static bool ValidText(string? value, int min, int max)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Trim().Length >= min &&
           value.Trim().Length <= max;

    private static bool AllowedOrderStatus(
        string category, string status)
        => category switch
        {
            "INVALID_KEY" or "KEY_ALREADY_USED" or "WRONG_PRODUCT"
                => status == "COMPLETED",

            "KEY_NOT_RECEIVED"
                => status == "PAID",

            "PAYMENT_ISSUE"
                => status == "PENDING" ||
                   status == "PAID" ||
                   status == "COMPLETED",

            "REFUND_REQUEST"
                => status == "PAID" ||
                   status == "COMPLETED",

            "WALLET_TOPUP_ISSUE" or "CATALOG_INFO" or "OTHER" => true,

            _ => false
        };

    private async Task<OwnedOrder?> VerifyOrderAsync(
        Guid orderId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/orders/{orderId:D}");

        if (!AuthenticationHeaderValue.TryParse(
                Request.Headers.Authorization.ToString(),
                out var auth) ||
            !string.Equals(auth.Scheme, "Bearer",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException(
                "Missing bearer token");
        }

        request.Headers.Authorization = auth;

        using var response = await clients
            .CreateClient("SupportOrders")
            .SendAsync(request, ct);

        if (response.StatusCode ==
            System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var order = await response.Content
            .ReadFromJsonAsync<OwnedOrder>(
                cancellationToken: ct)
            ?? throw new JsonException(
                "Invalid order response");

        if (order.Id != orderId ||
            order.Items is null ||
            string.IsNullOrWhiteSpace(order.Status))
        {
            throw new JsonException(
                "Invalid order data");
        }

        return order;
    }

    private async Task<bool> VerifyLicenseAsync(
        Guid licenseId, OwnedOrder order, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/licenses/me?orderId={order.Id}");

        if (!AuthenticationHeaderValue.TryParse(
                Request.Headers.Authorization.ToString(),
                out var auth) ||
            !string.Equals(auth.Scheme, "Bearer",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException("Missing bearer token");
        }

        request.Headers.Authorization = auth;

        using var response = await clients
            .CreateClient("SupportLicenses")
            .SendAsync(request, ct);

        response.EnsureSuccessStatusCode();

        var licenses = await response.Content
            .ReadFromJsonAsync<List<OwnedLicense>>(
                cancellationToken: ct)
            ?? throw new JsonException("Invalid license response");

        return licenses.Any(x =>
            x.Id == licenseId &&
            x.OrderId == order.Id &&
            order.Items.Any(i =>
                i.Id == x.OrderItemId &&
                i.ProductId == x.ProductId));
    }

    private IActionResult Error(
        int status, string code, string message)
        => StatusCode(status, new { code, error = message });

    private IActionResult Changed()
        => Error(409, "TICKET_CHANGED",
            "Ticket has changed. Refresh and retry.");

    private static void Touch(SupportTicket ticket)
    {
        ticket.Version = Guid.NewGuid();
        ticket.UpdatedAtUtc = DateTime.UtcNow;
    }

    // GET /api/support/tickets/categories
    [HttpGet("categories")]
    [AllowAnonymous]
    public IActionResult ListCategories()
        => Ok(CategoryCatalog);

    // POST /api/support/tickets
    [HttpPost]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> Create(
        CreateSupportTicketRequest request,
        CancellationToken ct)
    {
        if (UserId is null)
            return Unauthorized();

        var category = request.Category?.Trim().ToUpperInvariant();

        if (category is null ||
            !CategoriesByCode.TryGetValue(category, out var meta) ||
            !ValidText(request.Subject, 3, 150) ||
            !ValidText(request.Description, 5, 3000))
        {
            return Error(400, "INVALID_TICKET",
                "Invalid ticket information.");
        }

        var hasOrder = request.OrderId.HasValue && request.OrderId.Value != Guid.Empty;

        if (meta.RequiresOrder && !hasOrder)
        {
            return Error(400, "INVALID_TICKET",
                "Order ID is required for this category.");
        }

        if (!hasOrder && request.LicenseId.HasValue && request.LicenseId.Value != Guid.Empty)
        {
            return Error(400, "INVALID_TICKET",
                "Order ID is required when specifying a license.");
        }

        OwnedOrder? order = null;

        if (hasOrder)
        {
            try
            {
                order = await VerifyOrderAsync(request.OrderId!.Value, ct);
            }
            catch (Exception ex) when (
                ex is HttpRequestException or JsonException ||
                (ex is OperationCanceledException &&
                 !ct.IsCancellationRequested))
            {
                return Error(503, "ORDER_SERVICE_UNAVAILABLE",
                    "Unable to verify order ownership.");
            }

            if (order is null)
                return NotFound();

            if (!AllowedOrderStatus(category, order.Status))
                return Error(409, "INVALID_ORDER_STATUS",
                    "Order status does not allow this request.");
        }

        if (meta.RequiresLicense &&
            (!request.LicenseId.HasValue ||
             request.LicenseId.Value == Guid.Empty))
        {
            return Error(400, "LICENSE_REQUIRED",
                "License ID is required.");
        }

        if (request.LicenseId.HasValue && request.LicenseId.Value != Guid.Empty)
        {
            if (order is null || order.Status != "COMPLETED")
                return Error(409, "INVALID_ORDER_STATUS",
                    "Order has no eligible delivered license.");

            try
            {
                if (!await VerifyLicenseAsync(
                    request.LicenseId.Value, order, ct))
                    return NotFound();
            }
            catch (Exception ex) when (
                ex is HttpRequestException or JsonException ||
                (ex is OperationCanceledException &&
                 !ct.IsCancellationRequested))
            {
                return Error(503, "LICENSE_SERVICE_UNAVAILABLE",
                    "Unable to verify license ownership.");
            }
        }

        if (order is not null)
        {
            var duplicate = await db.SupportTickets.AnyAsync(x =>
                x.OrderId == order.Id &&
                x.Category == category &&
                (x.Status == "OPEN" ||
                 x.Status == "IN_PROGRESS"), ct);

            if (duplicate)
                return Error(409, "DUPLICATE_TICKET",
                    "An active ticket already exists.");
        }

        var ticket = new SupportTicket
        {
            OrderId = order?.Id,
            LicenseId = request.LicenseId is { } lid && lid != Guid.Empty ? lid : null,
            CustomerId = UserId,
            Category = category,
            Subject = request.Subject.Trim(),
            Description = request.Description.Trim(),
            AssignedRole = meta.AssignedRole
        };

        db.SupportTickets.Add(ticket);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Error(409, "DUPLICATE_TICKET",
                "An active ticket already exists.");
        }

        return CreatedAtAction(
            nameof(Get), new { id = ticket.Id }, ticket);
    }

    // GET /api/support/tickets
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? category,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!CanAccess)
            return Forbid();

        if (page < 1 || page > 10000 ||
            pageSize < 1 || pageSize > 100)
            return BadRequest();

        var query = AccessibleTickets().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            status = status.Trim().ToUpperInvariant();

            if (status is not (
                "OPEN" or "IN_PROGRESS" or "RESOLVED" or "CLOSED"))
                return BadRequest();

            query = query.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalizedCat = category.Trim().ToUpperInvariant();
            if (!CategoriesByCode.ContainsKey(normalizedCat))
                return BadRequest();

            query = query.Where(x => x.Category == normalizedCat);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.OrderId,
                x.LicenseId,
                x.Category,
                x.Subject,
                x.Status,
                x.AssignedRole,
                x.HandledBy,
                x.ResolutionOutcome,
                x.RejectionReason,
                x.CreatedAtUtc,
                x.UpdatedAtUtc
            })
            .ToListAsync(ct);

        return Ok(new { items, total, page, pageSize });
    }

    // GET /api/support/tickets/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(
        Guid id, CancellationToken ct)
    {
        if (!CanAccess)
            return Forbid();

        var ticket = await AccessibleTickets()
            .AsNoTracking()
            .Include(x => x.Messages
                .OrderBy(m => m.CreatedAtUtc)
                .ThenBy(m => m.Id))
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        return ticket is null ? NotFound() : Ok(ticket);
    }

    // POST /api/support/tickets/{id}/messages
    [HttpPost("{id:guid}/messages")]
    public async Task<IActionResult> AddMessage(
        Guid id,
        AddSupportMessageRequest request,
        CancellationToken ct)
    {
        if (!CanAccess)
            return Forbid();

        if (!ValidText(request.Content, 1, 3000))
            return Error(400, "INVALID_MESSAGE",
                "Invalid message content.");

        var ticket = await AccessibleTickets()
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        if (ticket is null)
            return NotFound();

        if (ticket.Status is "RESOLVED" or "CLOSED")
            return Error(409, "TICKET_NOT_ACTIVE",
                "Ticket no longer accepts messages.");

        if (!IsCustomer &&
            (ticket.Status != "IN_PROGRESS" ||
             ticket.HandledBy != UserId))
            return Forbid();

        var message = new SupportMessage
        {
            TicketId = ticket.Id,
            SenderId = UserId!,
            SenderRole = IsCustomer
                ? "customer"
                : ticket.AssignedRole,
            Content = request.Content.Trim()
        };

        db.SupportMessages.Add(message);
        Touch(ticket);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Changed();
        }

        return Ok(message);
    }

    // PATCH /api/support/tickets/{id}/status
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        UpdateSupportStatusRequest request,
        CancellationToken ct)
    {
        if (!CanAccess)
            return Forbid();

        var next = request.Status?.Trim().ToUpperInvariant();

        if (next is not ("IN_PROGRESS" or "RESOLVED" or "CLOSED"))
            return BadRequest();

        var ticket = await AccessibleTickets()
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        if (ticket is null)
            return NotFound();

        if (IsCustomer)
        {
            if (next != "CLOSED")
                return Forbid();

            if (ticket.Status != "RESOLVED")
                return Error(409, "INVALID_TRANSITION",
                    "Only resolved tickets can be closed.");
        }
        else
        {
            if (next == "CLOSED")
                return Forbid();

            if (ticket.Status == "OPEN" &&
                next == "IN_PROGRESS")
            {
                ticket.HandledBy = UserId;
            }
            else if (ticket.Status == "IN_PROGRESS" &&
                     next == "RESOLVED")
            {
                if (ticket.HandledBy != UserId)
                    return Forbid();
            }
            else
            {
                return Error(409, "INVALID_TRANSITION",
                    "Invalid ticket status transition.");
            }
        }

        ticket.Status = next;
        Touch(ticket);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Changed();
        }

        return Ok(new
        {
            ticket.Id,
            ticket.Status,
            ticket.HandledBy,
            ticket.ResolutionOutcome,
            ticket.RejectionReason,
            ticket.UpdatedAtUtc
        });
    }

    // POST /api/support/tickets/{id}/reject
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid id,
        RejectSupportTicketRequest request,
        CancellationToken ct)
    {
        if (!CanAccess || IsCustomer)
            return Forbid();

        if (!ValidText(request.Reason, 5, 500))
            return Error(400, "INVALID_REASON",
                "Vui lòng nhập lý do từ chối từ 5 đến 500 ký tự.");

        var ticket = await AccessibleTickets()
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        if (ticket is null)
            return NotFound();

        if (ticket.Status != "IN_PROGRESS" || ticket.HandledBy != UserId)
            return Error(409, "INVALID_TRANSITION",
                "Chỉ có thể từ chối phiếu đang xử lý do bạn phụ trách.");

        if (ticket.ResolutionOutcome == "APPROVED_REFUND")
            return Error(409, "ALREADY_APPROVED",
                "Phiếu này đã được duyệt hoàn tiền trước đó.");

        var reason = request.Reason.Trim();
        ticket.ResolutionOutcome = "REJECTED";
        ticket.RejectionReason = reason;
        ticket.Status = "RESOLVED";

        db.SupportMessages.Add(new SupportMessage
        {
            TicketId = ticket.Id,
            SenderId = UserId!,
            SenderRole = ticket.AssignedRole,
            Content = $"[TỪ CHỐI YÊU CẦU] Lý do: {reason}"
        });

        Touch(ticket);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Changed();
        }

        var emailQueued = false;
        if (ticket.OrderId.HasValue &&
            ticket.AssignedRole is "license_coordinator" or "transaction_auditor")
        {
            emailQueued = await TryNotifyOrderRejectionAsync(ticket, reason, ct);
        }

        return Ok(new
        {
            ticket.Id,
            ticket.Status,
            ticket.ResolutionOutcome,
            ticket.RejectionReason,
            ticket.HandledBy,
            ticket.UpdatedAtUtc,
            notificationQueued = emailQueued
        });
    }

    private async Task<bool> TryNotifyOrderRejectionAsync(
        SupportTicket ticket,
        string reason,
        CancellationToken ct)
    {
        try
        {
            var internalKey = config["InternalService:Key"];
            if (string.IsNullOrWhiteSpace(internalKey))
                return false;

            if (!AuthenticationHeaderValue.TryParse(
                    Request.Headers.Authorization.ToString(),
                    out var auth) ||
                !string.Equals(auth.Scheme, "Bearer",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using var req = new HttpRequestMessage(
                HttpMethod.Post,
                $"api/internal/support/tickets/{ticket.Id:D}/refund/reject-notify");
            req.Headers.Authorization = auth;
            req.Headers.Add("X-Internal-Service-Key", internalKey);
            req.Content = JsonContent.Create(new
            {
                ticketId = ticket.Id,
                orderId = ticket.OrderId!.Value,
                customerId = ticket.CustomerId,
                reason
            });

            using var res = await clients
                .CreateClient("SupportOrders")
                .SendAsync(req, ct);

            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            log.LogWarning(
                "Could not queue refund rejection email for ticket {TicketId}: {Error}",
                ticket.Id, ex.GetType().Name);
            return false;
        }
    }
}
