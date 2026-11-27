#!/usr/bin/env python3
"""Development-only catalog metadata import. Never purchases or activates products."""
import argparse
import csv
import getpass
import json
import subprocess
import time
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parent.parent
API = 'http://localhost:8000/api/catalog/steam-import'
AUTH = 'http://localhost:8081/realms/marketplace/protocol/openid-connect/token'

def existing():
    sql = '''SELECT "Slug" FROM public.products WHERE "Slug" LIKE 'steam-%';'''
    result = subprocess.check_output(['docker','compose','exec','-T','postgres','sh','-c',
        'psql -X -At -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d catalog_db'],
        input=sql, text=True, cwd=ROOT)
    return set(result.splitlines())

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true', help='Create inactive catalog products')
    parser.add_argument('--limit', type=int, default=60)
    args = parser.parse_args()
    if not 1 <= args.limit <= 60:
        parser.error('--limit must be 1..60')
    with (ROOT/'datasets/demo/steam-games.csv').open(newline='') as f:
        rows = list(csv.DictReader(f))
    assert len(rows) == 60 and len({r['app_id'] for r in rows}) == 60
    rows = rows[:args.limit]
    known = existing()
    print('MODE:', 'IMPORT INACTIVE' if args.apply else 'PLAN (NO WRITES)')
    if not args.apply:
        for r in rows:
            print('SKIP' if 'steam-'+r['app_id'] in known else 'PLAN', r['app_id'], r['group'], r['demo_price_vnd'])
        return 0
    if input('Development only. Type TEST: ') != 'TEST':
        return 1
    password = getpass.getpass('Catalog Manager password: ')
    def login():
        data = urlencode(dict(grant_type='password',client_id='marketplace-web',username='catalog_manager',password=password)).encode()
        with urlopen(Request(AUTH,data=data), timeout=20) as r:
            return json.load(r)['access_token']
    token = login()
    counts = dict(created=0, skipped=0, failed=0)
    for row in rows:
        app = row['app_id']; slug = 'steam-'+app
        if slug in known:
            counts['skipped'] += 1; print('[SKIP]', app, 'existing slug'); continue
        for attempt in range(2):
            try:
                data = json.dumps(dict(app=app,price=int(row['demo_price_vnd']))).encode()
                req = Request(API,data=data,headers={'Content-Type':'application/json','Authorization':'Bearer '+token},method='POST')
                with urlopen(req,timeout=90) as r:
                    status=r.status
                if status != 201: raise RuntimeError('Unexpected HTTP '+str(status))
                counts['created']+=1; print('[PASS]',app,'created inactive'); break
            except HTTPError as e:
                if e.code==401 and attempt==0:
                    token=login(); continue
                if e.code==409 and slug in existing():
                    counts['skipped']+=1; print('[SKIP]',app,'existing slug'); break
                counts['failed']+=1; print('[FAIL]',app,'HTTP',e.code); break
            except (URLError,TimeoutError,RuntimeError) as e:
                counts['failed']+=1; print('[FAIL]',app,type(e).__name__); break
        time.sleep(2)
    print('SUMMARY:',json.dumps(counts))
    return int(counts['failed']>0)

if __name__=='__main__':
    raise SystemExit(main())
