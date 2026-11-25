# Phase 3 — Fulfillment and customer library

- Existing `vault_fulfillments` rows acquire nullable `event_sent_at` via additive SQL on startup. No existing key is changed or decrypted during dispatch.
- Vault publishes `license.delivered.v1` with order/user/item/product IDs only, mandatory routing and publisher confirms. The order queue is durable. A crash before marking sent can duplicate delivery; Order applies it idempotently after verifying owner, paid timestamp and exact items.
- Order moves PAID to COMPLETED; rejected payloads go to `order.license-delivered.rejected.v1` for inspection. Do not replay blindly.
- GET `/api/orders/{id}/keys` requires a valid owner token, completed order and `azp=marketplace-web`. Order forwards the bearer only to the internal Vault, which independently checks owner and decrypts on demand. Both responses are no-store. Do not log bodies or persist keys in browser storage.
- This is local development: existing realm lacks API audience mapping, JWT issuer and azp are checked but audience validation is disabled. Configure API audience and HTTPS before production. RabbitMQ needs durable storage and service-specific credentials before production; publisher confirmation cannot guarantee delivery if broker data is lost. No refunds/revocation or notification email in this phase. Never use real key stock with demo payments.

## Test on existing local stack

1. Backup order and license databases and encryption secret separately. Do not rotate the key or reset volumes.
2. Build `dotnet build DigitalLicenseMarketplace.slnx`; run frontend `npm test` and `npm run build`.
3. `docker compose build order license-vault gateway`; `docker compose up -d --no-deps --no-build order license-vault gateway`.
4. Check logs and verify old order changes from PAID to COMPLETED without another payment; `event_sent_at` becomes non-null; inventory remains 5 total / 3 allocated / 2 available for the demo SKU.
5. Log in as buyer, open Orders, reveal three distinct demo keys, hide, copy, reload (keys hidden by default). Log in as another customer; GET the same order keys must return 404. Unauthenticated request must return 401. Own PENDING order must return 409. Verify successful response has Cache-Control: no-store.
6. Restart order and vault; verify same three allocations and no duplicates. To test duplicate event without re-paying, in a disposable test environment only, reset `event_sent_at` for the known demo order to NULL; verify completed order remains completed and inventory unchanged. Do not reset paid-event markers or stock.
7. Test broker interruption/retry in an isolated sandbox; verify no new allocation for existing order. Check rejected queue remains empty for valid events.
