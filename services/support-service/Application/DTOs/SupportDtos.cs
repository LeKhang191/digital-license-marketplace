namespace SupportService.Application.DTOs;

public sealed record CreateSupportTicketRequest(
    Guid? OrderId,
    Guid? LicenseId,
    string Category,
    string Subject,
    string Description
);

public sealed record AddSupportMessageRequest(
    string Content
);

public sealed record UpdateSupportStatusRequest(
    string Status
);

public sealed record RejectSupportTicketRequest(
    string Reason
);
