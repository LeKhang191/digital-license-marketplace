# Phase 5.1 — durable fulfillment inbox

The existing `license.order-paid` binding is unchanged. No queue purge or database reset is required. VaultWorker writes a deduplicated order to `vault_paid_inbox` before ACK. FulfillmentRetryWorker independently processes due rows. A shortage or allocation failure schedules a retry 60 seconds later without blocking other orders. After 20 failed attempts the row becomes DEAD; invalid messages are recorded in `vault_paid_rejected` by fingerprint only. Existing allocated orders remain protected by the original allocation transaction and order-id idempotency. The existing delivery dispatcher still publishes completion events.

Use only Sandbox and demo keys for tests. Do not expose these tables to clients. This is a database-backed delayed retry/dead-letter design, not a RabbitMQ TTL/DLX topology. It avoids relying on classic queue dead-letter forwarding for paid orders. Schema additions are idempotent startup SQL, not EF migrations. Keep one Vault replica for now; no retention policy, admin retry UI, automatic refund or reservation is provided.

## Validation

- Backup license_db; build solution and rebuild only license-vault.
- Existing COMPLETED orders and allocated counts must not change.
- Create two Sandbox orders on different SKUs: first with zero stock, second with adequate stock. Pay both. The first remains PAID with PENDING inbox row and INSUFFICIENT_STOCK; second completes without waiting for first to be restocked.
- Import enough demo keys for first SKU, verify it completes on the next due attempt.
- Restart Vault while a row is pending; verify it resumes, no duplicate allocation.
- In a disposable test environment replay identical paid event; verify no extra keys and only one inbox row. Send malformed event and check rejected fingerprint, then verify next valid order still completes.
- Verify DEAD row is not retried automatically. After operator investigates and restocks, reset only the selected DEAD row using the SQL below. Do not reset DONE or refund an order while its fulfillment is pending. Refunding and payment/stock coordination are not implemented yet.

```sql
SELECT order_id,status,attempts,next_attempt_at,last_error FROM vault_paid_inbox ORDER BY created_at;
SELECT fingerprint,reason,created_at FROM vault_paid_rejected;
-- Replace UUID only after verifying payment and stock in Sandbox:
UPDATE vault_paid_inbox SET status='PENDING', attempts=0,
 next_attempt_at=now(), last_error=NULL, updated_at=now()
 WHERE order_id='00000000-0000-0000-0000-000000000000' AND status='DEAD';
```

Rollback warning: once the new worker ACKs a message, its pending job lives in PostgreSQL. Do not revert to the old worker without draining/reconciling PENDING and DEAD rows first. Never delete inbox rows to force redelivery.
