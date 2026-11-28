BEGIN;

CREATE TABLE IF NOT EXISTS support_tickets (
    "Id" uuid PRIMARY KEY,
    "OrderId" uuid NOT NULL,
    "LicenseId" uuid NULL,
    "CustomerId" varchar(64) NOT NULL,
    "Category" varchar(32) NOT NULL,
    "Subject" varchar(150) NOT NULL,
    "Description" varchar(3000) NOT NULL,
    "Status" varchar(20) NOT NULL,
    "AssignedRole" varchar(32) NOT NULL,
    "HandledBy" varchar(64) NULL,
    "Version" uuid NOT NULL,
    "CreatedAtUtc" timestamptz NOT NULL,
    "UpdatedAtUtc" timestamptz NOT NULL,
    "ResolutionOutcome" varchar(32) NULL,
    "RejectionReason" varchar(500) NULL
);

ALTER TABLE support_tickets ALTER COLUMN "OrderId" DROP NOT NULL;
ALTER TABLE support_tickets ADD COLUMN IF NOT EXISTS "ResolutionOutcome" varchar(32) NULL;
ALTER TABLE support_tickets ADD COLUMN IF NOT EXISTS "RejectionReason" varchar(500) NULL;

CREATE TABLE IF NOT EXISTS support_messages (
    "Id" uuid PRIMARY KEY,
    "TicketId" uuid NOT NULL,
    "SenderId" varchar(64) NOT NULL,
    "SenderRole" varchar(32) NOT NULL,
    "Content" varchar(3000) NOT NULL,
    "CreatedAtUtc" timestamptz NOT NULL,

    CONSTRAINT "FK_support_messages_support_tickets_TicketId"
        FOREIGN KEY ("TicketId")
        REFERENCES support_tickets ("Id")
        ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS
    "IX_support_tickets_CustomerId"
ON support_tickets ("CustomerId");

CREATE INDEX IF NOT EXISTS
    "IX_support_tickets_AssignedRole_Status"
ON support_tickets ("AssignedRole", "Status");

CREATE UNIQUE INDEX IF NOT EXISTS
    "IX_support_tickets_OrderId_Category"
ON support_tickets ("OrderId", "Category")
WHERE "Status" IN ('OPEN', 'IN_PROGRESS');

CREATE INDEX IF NOT EXISTS
    "IX_support_messages_TicketId_CreatedAtUtc"
ON support_messages ("TicketId", "CreatedAtUtc");

CREATE TABLE IF NOT EXISTS support_external_links (
    "TicketId" uuid PRIMARY KEY REFERENCES support_tickets ("Id") ON DELETE RESTRICT,
    "State" text NOT NULL CHECK ("State" IN ('PENDING', 'LINKED', 'UNCERTAIN')),
    "IssueId" text UNIQUE,
    "IssueKey" text,
    "ExternalStatus" text,
    "CommentsJson" jsonb NOT NULL DEFAULT '[]'::jsonb,
    "CreatedAtUtc" timestamptz NOT NULL,
    "SyncedAtUtc" timestamptz,
    "Version" uuid NOT NULL
);

COMMIT;
