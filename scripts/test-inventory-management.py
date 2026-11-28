#!/usr/bin/env python3
"""Read-only stock, cost, provenance and access checks. Run with local services up."""
import runpy
from pathlib import Path
from decimal import Decimal
from uuid import UUID

helper = runpy.run_path(str(Path(__file__).with_name('test-kinguin-mock-workflow.py')))
call, login, env, sql = (helper[k] for k in ('call', 'login', 'env', 'sql'))

def check(label, ok):
    if not ok:
        raise SystemExit('[FAIL] ' + label)
    print('[PASS] ' + label, flush=True)

def main():
    staff, customer = login('license_coordinator'), login('test-user1')
    path = '/licenses/inventory'
    check('Anonymous inventory rejected', call(path)[0] == 401)
    check('Customer inventory forbidden', call(path, customer)[0] == 403)
    code, rows = call(path, staff)
    check('Inventory available', code == 200 and isinstance(rows, list) and len(rows) > 0)
    demo = env('Kinguin__Mode') == 'Mock' and env('Kinguin__DemoAutoImport').lower() == 'true'
    condition = 'TRUE' if demo else '''(b."Method" IS DISTINCT FROM 'MOCK_API')'''
    # Compare all available keys in the database, not just the 1,000-row detail view.
    raw = sql(f'''SELECT COALESCE(sum(b."UnitCostVnd") FILTER (WHERE k."Status"='AVAILABLE'),0), count(b."UnitCostVnd") FILTER (WHERE k."Status"='AVAILABLE'), count(*) FILTER (WHERE k."Status"='AVAILABLE') FROM license_keys k LEFT JOIN license_import_batches b ON k."ImportBatchId"=b."Id" WHERE {condition};''')
    cost, known, available = raw.split('|')
    check('Available count matches database', sum(r['available'] for r in rows) == int(available))
    check('Known cost count matches database', sum(r['availableKnownCostKeys'] for r in rows) == int(known))
    check('Known inventory cost matches database', sum(Decimal(str(r['availableKnownCostVnd'])) for r in rows) == Decimal(cost))
    check('Unknown cost counts nonnegative', all(0 <= r['availableKnownCostKeys'] <= r['available'] for r in rows))
    product = str(UUID(rows[0]['productId']))
    code, details = call('/licenses/products/'+product, staff)
    check('License detail available with source metadata', code == 200 and isinstance(details, list) and all('importBatchId' in r and 'supplierName' in r and 'unitCostVnd' in r for r in details))
    check('No key plaintext or ciphertext in details', all(not {'licensekey','encryptedkey','keyfingerprint','key'} & {k.lower() for k in r} for r in details))
    history = '/licenses/replacement-history/'+product
    check('Anonymous replacement history rejected', call(history)[0] == 401)
    check('Customer replacement history forbidden', call(history, customer)[0] == 403)
    code, replacements = call(history, staff)
    check('Replacement history accessible', code == 200 and isinstance(replacements, list))
    print('TOTAL: 12 PASS | Read-only; no data changed.', flush=True)

if __name__ == '__main__': main()
