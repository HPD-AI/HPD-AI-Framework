#!/usr/bin/env python3
import os
import pathlib
import subprocess
import sys

root = pathlib.Path(__file__).resolve().parents[2]
host = root / "src/HPD.Payments.Extensions.OutOfProcess.Host/bin/Release/net10.0/osx-arm64/publish/HPD.Payments.Extensions.OutOfProcess.Host"
if not host.is_file():
    raise SystemExit("native out-of-process host artifact is missing")
environment = os.environ.copy()
environment["HPD_PAYMENTS_PRODUCTION_HOST_PATH"] = str(host)
command = ["/usr/local/share/dotnet/dotnet", "run", "--project",
    "test/HPD.Payments.Extensions.OutOfProcess.Host.Tests/HPD.Payments.Extensions.OutOfProcess.Host.Tests.csproj",
    "-c", "Release", "-r", "osx-arm64", "--no-build", "--no-restore"]
sys.exit(subprocess.run(command, cwd=root, env=environment, check=False).returncode)
