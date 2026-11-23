-- Run once per database (psql -v ON_ERROR_STOP=1 -d catalog_db/order_db).
-- All failures halt before baseline. This is a guard, not an automatic schema migration.
DO $preflight$
DECLARE db text := current_database(); problems text;
BEGIN
 IF db NOT IN ('catalog_db', 'order_db') THEN
  RAISE EXCEPTION 'Unexpected database: %', db;
 END IF;
 -- Precise re-evaluation (do not rely on textual message matching).
 WITH expected(table_name,col,typ,nullable) AS (
  VALUES
  ('products','Id','uuid',false),('products','Name','character varying(160)',false),('products','Slug','character varying(180)',false),('products','Description','character varying(2000)',false),('products','Category','character varying(80)',false),('products','Price','numeric(12,2)',false),('products','IsActive','boolean',false),('products','CreatedAtUtc','timestamp with time zone',false),('products','UpdatedAtUtc','timestamp with time zone',false),
  ('orders','Id','uuid',false),('orders','KeycloakUserId','character varying(64)',false),('orders','CustomerEmail','character varying(256)',true),('orders','TotalAmount','numeric(12,2)',false),('orders','Status','character varying(20)',false),('orders','CreatedAtUtc','timestamp with time zone',false),('orders','PaymentRef','character varying(64)',true),('orders','VnpTransactionNo','character varying(32)',true),('orders','VnpPayDate','character varying(14)',true),('orders','PaidAtUtc','timestamp with time zone',true),('orders','PaidEventSentAtUtc','timestamp with time zone',true),
  ('order_items','Id','uuid',false),('order_items','OrderId','uuid',false),('order_items','ProductId','uuid',false),('order_items','Price','numeric(12,2)',false)
 )
 SELECT string_agg(format('%s.%s expected %s nullable=%s; actual %s nullable=%s',e.table_name,e.col,e.typ,e.nullable,COALESCE(format_type(a.atttypid,a.atttypmod),'MISSING'),(NOT a.attnotnull)),E'\n')
 INTO problems
 FROM expected e
 LEFT JOIN pg_class c ON c.relname=e.table_name AND c.relnamespace='public'::regnamespace AND c.relkind IN ('r','p')
 LEFT JOIN pg_attribute a ON a.attrelid=c.oid AND a.attname=e.col AND a.attnum>0 AND NOT a.attisdropped
 WHERE ((db='catalog_db' AND e.table_name='products') OR (db='order_db' AND e.table_name IN ('orders','order_items')))
   AND (a.attname IS NULL OR format_type(a.atttypid,a.atttypmod)<>e.typ OR a.attnotnull=e.nullable);
 IF problems IS NOT NULL THEN RAISE EXCEPTION 'Schema drift found:%',E'\n'||problems; END IF;
 IF db='catalog_db' AND NOT EXISTS(SELECT 1 FROM pg_indexes WHERE schemaname='public' AND tablename='products' AND indexname='IX_products_Slug') THEN
  RAISE EXCEPTION 'Missing IX_products_Slug';
 END IF;
 IF db='order_db' AND (
  NOT EXISTS(SELECT 1 FROM pg_indexes WHERE schemaname='public' AND tablename='orders' AND indexname='IX_orders_KeycloakUserId') OR
  NOT EXISTS(SELECT 1 FROM pg_indexes WHERE schemaname='public' AND tablename='orders' AND indexname='IX_orders_PaymentRef') OR
  NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='FK_order_items_orders_OrderId')
 ) THEN RAISE EXCEPTION 'Order indexes/FK incomplete'; END IF;
 RAISE NOTICE 'PASS: % core columns and selected indexes match expected model. Review generated migration for remaining constraints/defaults.',db;
END
$preflight$;
