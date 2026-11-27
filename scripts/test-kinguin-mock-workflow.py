#!/usr/bin/env python3
"""Development-only asynchronous supplier workflow. Never buys or pays anything."""
import getpass
import json
import os
import subprocess
import time
import uuid
from urllib.error import HTTPError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

API = os.getenv('API_URL', 'http://localhost:8000/api').rstrip('/')
AUTH = os.getenv('KEYCLOAK_URL', 'http://localhost:8081').rstrip('/') + '/realms/marketplace/protocol/openid-connect/token'
ROOT = '/licenses/supplier/kinguin'

def env(key):
    return subprocess.check_output(['docker','compose','exec','-T','license-vault','printenv',key], text=True).strip()

def call(path, token=None, body=None):
    headers = {'Accept':'application/json'}
    if token: headers['Authorization'] = 'Bearer ' + token
    if body is not None: headers['Content-Type'] = 'application/json'
    req = Request(API + path, data=json.dumps(body).encode() if body is not None else None, headers=headers)
    try:
        with urlopen(req, timeout=25) as r: return r.status, json.load(r)
    except HTTPError as e:
        try: return e.code, json.load(e)
        except ValueError: return e.code, {}

def login(user):
    data = urlencode({'grant_type':'password','client_id':'marketplace-web','username':user,'password':getpass.getpass(f'Password for {user}: ')}).encode()
    with urlopen(Request(AUTH,data=data),timeout=25) as r: return json.load(r)['access_token']

passed = failed = 0

def check(name, ok):
    global passed, failed
    passed += bool(ok); failed += not bool(ok)
    print(f'[{"PASS" if ok else "FAIL"}] {name}', flush=True)

def sql(statement):
    return subprocess.check_output(['docker','compose','exec','-T','postgres','sh','-c',
        'psql -X -U "$POSTGRES_USER" -d license_db -v ON_ERROR_STOP=1 -At'], input=statement, text=True).strip()

def wait_result(rid, token):
    for _ in range(90):
        status, result = call(ROOT + '/mock-orders/' + rid, token)
        if status == 200 and result.get('status') in ('MOCK_FULFILLED','MOCK_OUT_OF_STOCK'): return result
        time.sleep(1)
    raise RuntimeError('Timed out waiting for RabbitMQ workflow')

def main():
    if env('Kinguin__Mode') != 'Mock': raise SystemExit('STOP: Mock mode required')
    products = {env('Kinguin__ProductId'):env('Kinguin__LocalProductId')}
    for entry in env('Kinguin__ProductMappings').split(';'):
        if entry.strip():
            local, supplier = entry.split('=',1); products[supplier.strip()] = local.strip()
    product, empty = [str(uuid.UUID(products[x])) for x in ('demo-product-001','demo-product-003')]
    coordinator = login('license_coordinator'); customer = login('test-user1')
    path = ROOT + '/mock-orders/async'
    rid = str(uuid.uuid5(uuid.NAMESPACE_URL, 'dlm-kinguin-workflow-v1:' + product))
    badid = str(uuid.uuid5(uuid.NAMESPACE_URL, 'dlm-kinguin-workflow-empty-v1:' + empty))
    request = {'requestId':rid,'productId':product,'quantity':1}
    status, old = call(ROOT+'/mock-orders/'+rid, coordinator)
    existed = status == 200 and old.get('status') == 'MOCK_FULFILLED'
    _, quote = call(ROOT+'/'+product, coordinator)
    before = quote.get('quote',{}).get('textQty',0)
    if not existed and status == 404 and before < 1: raise SystemExit('STOP: fixture 001 has no stock; do not delete existing data automatically')
    check('Anonymous rejected',call(path,body=request)[0]==401)
    check('Customer forbidden',call(path,customer,request)[0]==403)
    status, result = call(path,coordinator,request)
    check('Durable simulated request accepted', status in (200,202) and result.get('simulated') is True and result.get('charged') is False)
    final = wait_result(rid,coordinator)
    check('RabbitMQ fulfillment completed',final.get('status')=='MOCK_FULFILLED')
    check('Mock cost correct',final.get('totalCost')==5.25)
    status, result=call(path,coordinator,request)
    check('Idempotent replay',status==200 and result.get('replayed') is True)
    check('Conflicting replay rejected',call(path,coordinator,dict(request,quantity=2))[0]==409)
    _, quote=call(ROOT+'/'+product,coordinator)
    after=quote.get('quote',{}).get('textQty',-1)
    check('Stock synchronized',after==before-(0 if existed else 1) or (old.get('status')=='MOCK_PENDING' and after in (before,before-1)))
    status, keys=call(ROOT+'/mock-orders/'+rid+'/demo-keys',coordinator)
    check('Owner demo delivery',status==200 and keys.get('simulated') is True and keys.get('keys')==[f'DEMO-NOT-REDEEMABLE-{uuid.UUID(rid).hex}-1'])
    check('Customer demo delivery forbidden',call(ROOT+'/mock-orders/'+rid+'/demo-keys',customer)[0]==403)
    status, _=call(path,coordinator,{'requestId':badid,'productId':empty,'quantity':1})
    check('Empty fixture request accepted',status in (200,202))
    check('Durable out-of-stock result',wait_result(badid,coordinator).get('status')=='MOCK_OUT_OF_STOCK')
    check('No demo key for failed order',call(ROOT+'/mock-orders/'+badid+'/demo-keys',coordinator)[0]==404)
    # UUIDs are locally generated; no secret or key ciphertext printed.
    check('No real allocation for mock IDs',sql(f'SELECT count(*) FROM allocation_results WHERE "OrderId" IN (\'{rid}\',\'{badid}\');')=='0')
    check('No real license assignment for mock IDs',sql(f'SELECT count(*) FROM license_keys WHERE "OrderId" IN (\'{rid}\',\'{badid}\');')=='0')
    check('Encrypted demo key persisted',sql(f'''SELECT count(*) FROM kinguin_mock_orders WHERE "Id" = '{rid}' AND "Status" = 'MOCK_FULFILLED' AND "EncryptedKeys" NOT LIKE '%DEMO-NOT-REDEEMABLE%' AND "EncryptedKeys" <> '[]';''')=='1')
    print(f'TOTAL: {passed+failed} | PASSED: {passed} | FAILED: {failed}')
    return 1 if failed else 0

if __name__=='__main__': raise SystemExit(main())
