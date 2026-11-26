BEGIN;

CREATE TABLE IF NOT EXISTS notification_messages (
    "Id" uuid PRIMARY KEY,
    "OrderId" uuid NOT NULL,
    "CustomerId" varchar(64) NOT NULL,
    "RecipientEmail" varchar(320) NOT NULL,
    "Type" varchar(20) NOT NULL,
    "Amount" numeric(12,2) NOT NULL,
    "Status" varchar(20) NOT NULL DEFAULT 'PENDING',
    "RetryCount" integer NOT NULL DEFAULT 0,
    "CreatedAtUtc" timestamptz NOT NULL,
    "NextRetryAtUtc" timestamptz NOT NULL,
    "SentAtUtc" timestamptz NULL,
    "LastError" varchar(100) NULL,

    CONSTRAINT notification_order_type_unique
        UNIQUE ("OrderId", "Type"),

    CONSTRAINT notification_type_check
        CHECK ("Type" IN ('COMPLETED', 'REFUNDED')),

    CONSTRAINT notification_status_check
        CHECK ("Status" IN (
            'PENDING', 'SENT', 'FAILED', 'SKIPPED'
        )),

    CONSTRAINT notification_retry_check
        CHECK ("RetryCount" >= 0)
);

CREATE INDEX IF NOT EXISTS notification_pending_idx
ON notification_messages ("Status", "NextRetryAtUtc");

COMMIT;
