#!/usr/bin/env python3
import hashlib,pathlib
root=pathlib.Path('.').resolve()
files=sorted(root.glob('src/**/bin/Debug/net10.0/HPD.Payments.*.dll'))+sorted(root.glob('test/**/bin/Debug/net10.0/HPD.Payments.*.dll'))
for f in files:
    print(hashlib.sha256(f.read_bytes()).hexdigest(),f.relative_to(root).as_posix())
