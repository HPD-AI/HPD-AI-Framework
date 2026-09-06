"""Run ProcessTerminal against a Unix PTY and validate captured output with the terminal oracle.

Run with: python3 run.py. This checks the real console I/O path; it does not certify
behavior in the terminal applications named by the environment profiles.
"""
import os, pty, fcntl, termios, struct, subprocess, select, time
from pathlib import Path
root = Path(__file__).resolve().parent
build = subprocess.run(['dotnet', 'build', str(root / 'HPD-TUI.TerminalSmoke.csproj'), '-v', 'quiet'], capture_output=True, text=True)
if build.returncode:
    print(build.stdout + build.stderr)
    raise SystemExit(build.returncode)
import tempfile
scratch = tempfile.TemporaryDirectory(prefix='hpd-terminal-smoke-')
for profile in ['xterm', 'iTerm.app', 'WezTerm', 'ghostty']:
    master, slave = pty.openpty()
    fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack('HHHH',6,24,0,0))
    env = dict(os.environ, TERM='xterm-256color', TERM_PROGRAM=profile)
    command=['dotnet',str(root / 'bin/Debug/net10.0/HPD-TUI.TerminalSmoke.dll')]
    proc=subprocess.Popen(command,stdin=slave,stdout=slave,stderr=slave,env=env,start_new_session=True)
    os.close(slave)
    output=bytearray()
    deadline=time.monotonic()+15
    while time.monotonic()<deadline:
        if select.select([master],[],[],0.1)[0]:
            try: data=os.read(master,65536)
            except OSError: break
            if not data: break
            output.extend(data)
        elif proc.poll() is not None: break
    if proc.poll() is None: proc.kill()
    status=proc.wait()
    os.close(master)
    target=str(Path(scratch.name) / f'{profile}.ansi')
    Path(target).write_bytes(output)
    if status: raise RuntimeError(f'{profile}: exit {status}; captured output at {target}')
    check=subprocess.run(command+[target],capture_output=True,text=True)
    if check.returncode: raise RuntimeError(check.stderr)
    print(profile+': '+check.stdout.strip())
