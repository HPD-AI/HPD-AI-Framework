#!/usr/bin/env python3
import hashlib,json,pathlib,sys
p=pathlib.Path(sys.argv[1]); raw=p.read_bytes(); data=json.loads(raw)
required={'id','enabled','cwd','argv','prerequisites','timeoutSeconds','outputs','cleanup','acceptedExitCodes','proofClass'}
ids=[]
for c in data['commands']:
    missing=required-set(c)
    if missing: raise SystemExit(f"{c.get('id')}: missing {sorted(missing)}")
    if not isinstance(c['argv'],list) or not c['argv'] or any(not isinstance(x,str) for x in c['argv']): raise SystemExit(f"{c['id']}: invalid argv")
    ids.append(c['id'])
if len(ids)!=len(set(ids)): raise SystemExit('duplicate command id')
proof=next(c for c in data['commands'] if c['id']=='test-conformance-proof')
if proof['enabled'] is not True: raise SystemExit('test-conformance-proof must be activated')
if len(proof['prerequisites'])!=4 or 'run-aot-conformance' not in proof['prerequisites']: raise SystemExit('activation prerequisites drift')
canonical=json.dumps(data,sort_keys=True,separators=(',',':')).encode()
print(f"commands={len(ids)} schema={data['schemaVersion']} rawSha256={hashlib.sha256(raw).hexdigest()} canonicalSha256={hashlib.sha256(canonical).hexdigest()} proofEnabled={proof['enabled']}")
