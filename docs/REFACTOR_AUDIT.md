# Audit & staged remediation (2026-11-23)

## Scope and evidence

Inspected: source ZIP `digital-license-marketplace-review(2).zip` and accompanying `review-fixes.patch`. Patch was mostly already included in the source snapshot; reapplying it causes reversed/already-applied conflicts. No deployment performed.

## Fixed in reviewed copy

- Exclude `backups/`, `*.dump`, `.env.*` and frontend build artifacts from new distributed bundles/Docker context. **Previously committed files remain in Git history until explicitly purged.**
- Remove hardcoded demo-account passwords/users from default Keycloak realm import; create test accounts via Admin Console, preferably with disposable passwords. An already imported realm is not automatically updated by changing the JSON file.
- Test scripts read test usernames/passwords via environment variables instead of embedding credentials. Use dedicated non-production accounts.
- Frontend ignores late responses from stale product queries; disables duplicate payment retries; subscribes to Keycloak auth state changes.
- TypeScript polling unit tests run under Node 22 using explicit `--experimental-strip-types`.

## Blocking defects requiring staged implementation

1. **Database migration baseline.** Catalog and Order still use `EnsureCreatedAsync()`. `infra/postgres/repair-order-db-schema.sql` is only a development repair and does not constitute an EF migration. Existing DBs need a baseline + data-preserving migration plan, backup/restore test, and migration-history reconciliation. Never simply mark a migration applied before comparing the actual schema.
2. **Payment idempotency.** `OrderManager.PayAsync` rotates `PaymentRef` each time. An older payment attempt may be captured but rejected by IPN. Introduce `PaymentAttempt` entity per payment URL, with unique reference, amount, expiry, gateway transaction ID and outcome; match all verified attempts; define double-payment/refund handling.
3. **Transactional Outbox.** `VnPayIpnHandler` changes `PENDING → PAID` before publishing `OrderPaidEvent`. Broker outage can strand paid orders; concurrent callbacks may publish duplicates. Write a durable outbox event in the same DB transaction as state change, publish asynchronously, and make consumer idempotent.
4. **VNPay validity.** Reject placeholder or invalid TMN code, validate signed callback merchant code/currency and add replay/duplicate/amount/tamper tests against test vectors. Do not test against production credentials. Return URL is not an IPN endpoint; backend IPN must be reachable by VNPay over publicly accessible HTTPS.
5. **RabbitMQ event consumers.** License Vault and Notification are scaffolds; there is no verified end-to-end key delivery or refund saga. Add durable queues/DLQs, retries and idempotent processing before claiming feature complete.
6. **Deployment.** No frontend service in Compose; Keycloak `start-dev` and unauthenticated gateway routes must not be considered production-safe. Review JWT audience and proxy forwarding.

## Local validation commands (from repo root)

```bash
node --experimental-strip-types --test frontend/tests/*.test.ts
bash -n scripts/*.sh
# installed tooling + network required:
cd frontend && npm ci && npm run build
cd .. && dotnet build DigitalLicenseMarketplace.slnx
docker compose config --quiet  # requires valid .env
```

### Testing authentication scripts (never commit credentials)

```bash
read -rp 'Admin username: ' TEST_ADMIN_USER
read -rsp 'Admin password: ' TEST_ADMIN_PASSWORD; echo
read -rp 'Customer username: ' TEST_CUSTOMER_USER
read -rsp 'Customer password: ' TEST_CUSTOMER_PASSWORD; echo
export TEST_ADMIN_USER TEST_ADMIN_PASSWORD TEST_CUSTOMER_USER TEST_CUSTOMER_PASSWORD
bash scripts/test-auth.sh
unset TEST_ADMIN_PASSWORD TEST_CUSTOMER_PASSWORD
```

## Rollout gates

1. Create a branch; compare diff and scan for secrets; ensure changes do not overwrite `.env`/DB.
2. Pass shell syntax and frontend polling tests, then frontend typecheck/build and full .NET build.
3. Apply schema migrations against a copy of DB; inspect migration SQL; test restore.
4. Test Keycloak login + Catalog GET + Order create and view, negative 401/403, and duplicate callbacks.
5. In Sandbox, test success/failure/timeout and broker outage; confirm **exactly one** license per paid order.
6. Deploy incrementally, inspect logs/health, and keep a rollback plan.
