#!/usr/bin/env python3
"""Local DEMO only. Creates one simulated order, verifies durable import and replay.
No real supplier transaction, no license plaintext or token printed.
"""
import runpy
import subprocess
import time
import uuid
from pathlib import Path

legacy = runpy.run_path(str(Path(__file__).with_name('test-kinguin-mock-workflow.py')))
call, login, env, sql = (legacy[k] for k in ('call', 'login', 'env', 'sql'))
ROOT = '/licenses/supplier/kinguin'

def check(label, condition):
    if not condition:
        raise SystemExit('[FAIL] ' + label)
    print('[PASS] ' + label, flush=True)

def main():
    check('Dedicated demo mode enabled', env('Kinguin__Mode') == 'Mock' and env('Kinguin__DemoAutoImport').lower() == 'true')
    pid = subprocess.check_output(['docker','compose','exec','-T','postgres','psql','-X','-U','app','-d','catalog_db','-v','ON_ERROR_STOP=1','-Atc', '''SELECT "Id" FROM products WHERE "Slug" = 'steam-1091500' '''], text=True).strip()
    pid = str(uuid.UUID(pid))
    token = login('license_coordinator')
    code, quote = call(ROOT+'/'+pid, token)
    check('Exact game mapping and available mock stock', code == 200 and quote.get('supplierProductId') == 'mock-steam-1091500' and quote.get('demoAutoImport') is True and quote.get('quote',{}).get('textQty',0) >= 1)
    stock_before = quote['quote']['textQty']
    rid = str(uuid.uuid4())
    request = {'requestId':rid,'productId':pid,'quantity':1}
    print('Request ID:', rid, flush=True)
    check('Anonymous request rejected', call(ROOT+'/mock-orders/async',body=request)[0] == 401)
    code, result = call(ROOT+'/mock-orders/async',token,request)
    check('Asynchronous purchase accepted', code == 202 and result.get('charged') is False)
    for _ in range(90):
        code, result = call(ROOT+'/mock-orders/'+rid,token)
        if code == 200 and result.get('status') != 'MOCK_PENDING': break
        time.sleep(1)
    check('Order fulfilled with one imported key', result.get('status') == 'MOCK_FULFILLED' and result.get('imported') == 1)
    check('One linked import batch', sql(f'''SELECT count(*) FROM license_import_batches WHERE "Id" = '{rid}' AND "Method" = 'MOCK_API' AND "Imported" = 1 AND "ProductId" = '{pid}' ''') == '1')
    check('One available encrypted license', sql(f'''SELECT count(*) FROM license_keys WHERE "ImportBatchId" = '{rid}' AND "ProductId" = '{pid}' AND "Status" = 'AVAILABLE' AND "EncryptedKey" NOT LIKE '%DEMO-NOT-REDEEMABLE%' ''') == '1')
    check('Supplier key plaintext endpoint unavailable after import', call(ROOT+'/mock-orders/'+rid+'/demo-keys',token)[0] == 404)
    code, replay = call(ROOT+'/mock-orders/async',token,request)
    check('Same request replays original result', code == 200 and replay.get('replayed') is True and replay.get('imported') == 1)
    check('No duplicate license or batch', sql(f'''SELECT count(*) FROM license_keys WHERE "ImportBatchId" = '{rid}' ''') == '1' and sql(f'''SELECT count(*) FROM license_import_batches WHERE "Id" = '{rid}' ''') == '1')
    code, after = call(ROOT+'/'+pid,token)
    check('Supplier stock reduced exactly once', code == 200 and after['quote']['textQty'] == stock_before - 1)
    print('Completed. One DEMO key remains in the demo inventory; no cleanup performed.', flush=True)

if __name__ == '__main__': main()
