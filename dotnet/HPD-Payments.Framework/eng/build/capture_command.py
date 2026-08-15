#!/usr/bin/env python3
import datetime,hashlib,json,os,pathlib,subprocess,sys,time
command_id,cwd,output,*argv=sys.argv[1:]
path=pathlib.Path(output)
path.parent.mkdir(parents=True,exist_ok=True)
if path.exists() or pathlib.Path(str(path)+'.meta.json').exists():
    raise SystemExit(f'append-only output exists: {path}')
started=datetime.datetime.now(datetime.timezone.utc)
begin=time.monotonic()
with path.open('xb') as stream:
    process=subprocess.Popen(argv,cwd=cwd,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    while True:
        chunk=process.stdout.read(65536)
        if not chunk: break
        stream.write(chunk)
    exit_code=process.wait()
ended=datetime.datetime.now(datetime.timezone.utc)
raw=path.read_bytes()
meta={'schemaVersion':'hpd.payments.bootstrap-evidence.v1','commandId':command_id,'cwd':cwd,'argv':argv,'startedUtc':started.isoformat(),'endedUtc':ended.isoformat(),'durationSeconds':round(time.monotonic()-begin,6),'exitCode':exit_code,'output':path.name,'bytes':len(raw),'sha256':hashlib.sha256(raw).hexdigest(),'cleanup':'none'}
pathlib.Path(str(path)+'.meta.json').write_text(json.dumps(meta,indent=2)+'\n',encoding='utf-8',newline='\n')
raise SystemExit(exit_code)
