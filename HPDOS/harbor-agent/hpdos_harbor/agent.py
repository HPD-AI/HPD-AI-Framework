"""
HPDOS Harbor Agent Adapter
--------------------------
Runs the HPDOS CLI agent (hpdos backend) inside the sandbox, drives it via its
HTTP API, and converts the SSE AgentEvent stream into an ATIF trajectory.

Usage (with harbor CLI):
    harbor run -d terminal-bench@2.0 \
        --agent-import-path hpdos_harbor.agent:HPDOSAgent \
        -m anthropic/claude-sonnet-4-6 \
        -n 4
"""

from __future__ import annotations

import json
import os
import shlex
import uuid
from pathlib import Path

from harbor.agents.installed.base import BaseInstalledAgent, ExecInput
from harbor.environments.base import BaseEnvironment
from harbor.models.agent.context import AgentContext

# Path to the HPDOS repo root (two levels up from this file: harbor-agent/hpdos_harbor/agent.py)
_REPO_ROOT = Path(__file__).parent.parent.parent

AGENT_NAME = "hpdos"
AGENT_VERSION = "0.1.0"
_DEFAULT_PORT = 5173


class HPDOSAgent(BaseInstalledAgent):
    """Harbor agent adapter for HPDOS."""

    SUPPORTS_ATIF: bool = True

    @staticmethod
    def name() -> str:
        return AGENT_NAME

    def version(self) -> str | None:
        return AGENT_VERSION

    @property
    def _install_agent_template_path(self) -> Path:
        return Path(__file__).parent / "install-hpdos.sh.j2"

    async def setup(self, environment: BaseEnvironment) -> None:
        # For Docker environments, HPDOS is already built into the image
        # For other environments, run the base setup which includes the install script
        await super().setup(environment)

    def create_run_agent_commands(self, instruction: str) -> list[ExecInput]:
        # Get configuration from environment variables or use defaults
        port = int(os.environ.get("HPDOS_PORT", str(_DEFAULT_PORT)))
        max_turns = int(os.environ.get("HPDOS_MAX_TURNS", "50"))
        provider_key = os.environ.get("HPDOS_PROVIDER_KEY", "anthropic")

        env: dict[str, str] = {}
        for key in (
            "ANTHROPIC_API_KEY",
            "OPENAI_API_KEY",
            "OPENROUTER_API_KEY",
            "GOOGLE_API_KEY",
        ):
            val = os.environ.get(key, "")
            if val:
                env[key] = val
        env["HPDOS_PROVIDER_KEY"] = provider_key

        model_id = self.model_name or ""
        if "/" in model_id:
            model_id = model_id.split("/", 1)[1]

        driver_script = _build_driver_script(
            instruction=instruction,
            port=port,
            max_turns=max_turns,
            provider_key=provider_key,
            model_id=model_id,
        )

        write_driver_cmd = (
            "mkdir -p /logs/agent && "
            "cat > /tmp/hpdos_driver.py << 'HPDOS_DRIVER_EOF'\n"
            + driver_script
            + "\nHPDOS_DRIVER_EOF"
        )

        run_cmd = (
            "export DOTNET_ROOT=/usr/local/dotnet && "
            "export PATH=\"/usr/local/dotnet:/usr/local/hpdos:/bin:/usr/local/bin:/usr/bin:$PATH\" && "
            "export LD_LIBRARY_PATH=\"/usr/local/hpdos:$LD_LIBRARY_PATH\" && "
            "/usr/local/hpdos/hpdos backend "
            "&>/logs/agent/backend.log & "
            "BACKEND_PID=$! && "
            # Wait up to 30s for backend log to contain the port
            "for i in $(seq 1 30); do "
            "  grep -o 'localhost:[0-9]*' /logs/agent/backend.log 2>/dev/null && break; "
            "  sleep 1; "
            "done && "
            "HPDOS_PORT=$(grep -o 'localhost:[0-9]*' /logs/agent/backend.log | head -1 | cut -d: -f2) && "
            "echo \"HPDOS port: $HPDOS_PORT\" && "
            # Run the driver with the actual port
            "python3 /tmp/hpdos_driver.py \"$HPDOS_PORT\" 2>&1 | tee /logs/agent/driver.log; "
            "DRIVER_EXIT=$?; "
            "kill $BACKEND_PID 2>/dev/null || true; "
            "exit $DRIVER_EXIT"
        )

        return [
            ExecInput(command=write_driver_cmd, env=env),
            ExecInput(command=run_cmd, env=env),
        ]

    def populate_context_post_run(self, context: AgentContext) -> None:
        trajectory_path = self.logs_dir / "trajectory.json"
        
        # If trajectory doesn't exist, create a fallback one
        if not trajectory_path.exists():
            print(f"[hpdos] No trajectory file at {trajectory_path}, creating fallback")
            # Generate a minimal valid trajectory as fallback
            fallback_trajectory = {
                "schema_version": "ATIF-v1.6",
                "session_id": "fallback-session",
                "agent": {
                    "name": "hpdos",
                    "version": "0.1.0",
                    "model_name": self.model_name or None,
                },
                "steps": [
                    {
                        "step_id": 1,
                        "source": "user",
                        "message": "Task not executed - backend failed to start",
                        "timestamp": __import__("datetime").datetime.now(__import__("datetime").timezone.utc).isoformat(),
                    }
                ],
                "final_metrics": {
                    "total_prompt_tokens": 0,
                    "total_completion_tokens": 0,
                    "total_cached_tokens": 0,
                    "total_cost_usd": 0.0,
                    "total_steps": 1,
                },
            }
            trajectory_path.write_text(json.dumps(fallback_trajectory, indent=2), encoding="utf-8")
            return

        try:
            data = json.loads(trajectory_path.read_text(encoding="utf-8"))
        except Exception as exc:
            print(f"[hpdos] Failed to parse trajectory: {exc}")
            return

        final = data.get("final_metrics") or {}
        context.n_input_tokens = final.get("total_prompt_tokens") or 0
        context.n_output_tokens = final.get("total_completion_tokens") or 0
        context.n_cache_tokens = final.get("total_cached_tokens") or 0
        context.cost_usd = final.get("total_cost_usd")


# ---------------------------------------------------------------------------
# Inline driver script — executes inside the container as a Python process
# ---------------------------------------------------------------------------

def _build_driver_script(
    instruction: str,
    port: int,
    max_turns: int,
    provider_key: str,
    model_id: str,
) -> str:
    instr_json = json.dumps(instruction)
    provider_json = json.dumps(provider_key)
    model_json = json.dumps(model_id)

    return f"""\
#!/usr/bin/env python3
import json, sys, uuid, re
from datetime import datetime, timezone
from pathlib import Path
from urllib import request as urllib_request
import http.client, urllib.parse

# Port passed as argv[1], or fall back to parsing backend.log, or default
if len(sys.argv) > 1 and sys.argv[1].isdigit():
    _port = int(sys.argv[1])
else:
    _port = {port}
    _log = Path("/logs/agent/backend.log")
    if _log.exists():
        m = re.search(r"localhost:([0-9]+)", _log.read_text())
        if m:
            _port = int(m.group(1))

BASE_URL = f"http://localhost:{{_port}}"
LOGS_DIR = Path("/logs/agent")
LOGS_DIR.mkdir(parents=True, exist_ok=True)

INSTRUCTION = {instr_json}
PROVIDER_KEY = {provider_json}
MODEL_ID = {model_json}
MAX_TURNS = {max_turns}


def api(method, path, body=None):
    url = BASE_URL + path
    data = json.dumps(body).encode() if body is not None else None
    req = urllib_request.Request(
        url, data=data,
        headers={{"Content-Type": "application/json"}},
        method=method,
    )
    with urllib_request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read())


def stream_sse(path, body):
    parsed = urllib.parse.urlparse(BASE_URL)
    conn = http.client.HTTPConnection(parsed.hostname, parsed.port or 80, timeout=600)
    payload = json.dumps(body).encode()
    conn.request(
        "POST", path, body=payload,
        headers={{
            "Content-Type": "application/json",
            "Accept": "text/event-stream",
        }},
    )
    resp = conn.getresponse()
    if resp.status not in (200, 201):
        raise RuntimeError(f"Stream failed {{resp.status}}: {{resp.read().decode(errors='replace')}}")
    buf = b""
    while True:
        chunk = resp.read(4096)
        if not chunk:
            break
        buf += chunk
        while b"\\n\\n" in buf:
            block, buf = buf.split(b"\\n\\n", 1)
            for line in block.decode(errors="replace").splitlines():
                if line.startswith("data:"):
                    raw = line[5:].strip()
                    if raw and raw != "[DONE]":
                        try:
                            yield json.loads(raw)
                        except json.JSONDecodeError:
                            pass
    conn.close()


# Create session
session = api("POST", "/sessions", {{}})
session_id = session["id"]
branch_id = "main"
print(f"Session: {{session_id}}", flush=True)

run_config = {{}}
if PROVIDER_KEY:
    run_config["providerKey"] = PROVIDER_KEY
if MODEL_ID:
    run_config["modelId"] = MODEL_ID

stream_body = {{
    "messages": [{{"content": INSTRUCTION}}],
    "runConfig": run_config,
}}

steps = []
step_id = 0
current_text = []
final_text = []
pending_tool_calls = {{}}
raw_events = []

try:
    for event in stream_sse(f"/sessions/{{session_id}}/branches/{{branch_id}}/stream", stream_body):
        raw_events.append(event)
        etype = event.get("type", "")
        data = event.get("data") or {{}}

        if etype == "content:delta":
            fragment = data.get("content", "")
            if fragment:
                current_text.append(fragment)
                print(fragment, end="", flush=True)

        elif etype == "turn:message":
            role = data.get("role", "")
            if role == "assistant" and current_text:
                text = "".join(current_text)
                current_text = []
                step_id += 1
                steps.append({{
                    "step_id": step_id,
                    "timestamp": datetime.now(timezone.utc).isoformat(),
                    "source": "agent",
                    "message": text,
                    "model_name": MODEL_ID or None,
                }})
                final_text.append(text)
            elif role == "user":
                content = data.get("content") or []
                text = " ".join(
                    c.get("text", "") for c in content
                    if isinstance(c, dict) and c.get("type") == "text"
                ) or INSTRUCTION
                step_id += 1
                steps.append({{
                    "step_id": step_id,
                    "timestamp": datetime.now(timezone.utc).isoformat(),
                    "source": "user",
                    "message": text,
                }})

        elif etype == "turn:toolCall":
            call_id = data.get("callId", str(uuid.uuid4()))
            pending_tool_calls[call_id] = {{
                "call_id": call_id,
                "tool_name": data.get("toolName", ""),
                "arguments": data.get("arguments") or {{}},
                "timestamp": datetime.now(timezone.utc).isoformat(),
            }}

        elif etype == "turn:toolResult":
            call_id = data.get("callId", "")
            result_content = data.get("result") or []
            output_text = " ".join(
                c.get("text", "") for c in result_content
                if isinstance(c, dict) and c.get("type") == "text"
            )
            call_info = pending_tool_calls.pop(call_id, {{
                "call_id": call_id,
                "tool_name": "",
                "arguments": {{}},
                "timestamp": datetime.now(timezone.utc).isoformat(),
            }})
            step_id += 1
            steps.append({{
                "step_id": step_id,
                "timestamp": call_info["timestamp"],
                "source": "agent",
                "message": f"Executed {{call_info['tool_name']}}",
                "tool_calls": [{{
                    "tool_call_id": call_id,
                    "function_name": call_info["tool_name"],
                    "arguments": call_info["arguments"],
                }}],
                "observation": {{
                    "results": [{{
                        "source_call_id": call_id,
                        "content": output_text or None,
                    }}]
                }},
            }})

        elif etype == "turn:error":
            print(f"\\n[agent error] {{data.get('error', '')}}", file=sys.stderr)

except Exception as exc:
    print(f"\\n[driver error] {{exc}}", file=sys.stderr, flush=True)

# Flush any trailing streamed text
if current_text:
    text = "".join(current_text)
    step_id += 1
    steps.append({{
        "step_id": step_id,
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "source": "agent",
        "message": text,
        "model_name": MODEL_ID or None,
    }})
    final_text.append(text)

(LOGS_DIR / "output.txt").write_text("\\n\\n".join(final_text), encoding="utf-8")
(LOGS_DIR / "raw_events.jsonl").write_text(
    "\\n".join(json.dumps(e) for e in raw_events), encoding="utf-8"
)

# Build ATIF steps
atif_steps = []
for s in steps:
    atif_step = {{
        "step_id": s["step_id"],
        "timestamp": s.get("timestamp"),
        "source": s["source"],
        "message": s["message"],
    }}
    if s.get("model_name"):
        atif_step["model_name"] = s["model_name"]
    if s.get("tool_calls"):
        atif_step["tool_calls"] = s["tool_calls"]
    if s.get("observation"):
        atif_step["observation"] = s["observation"]
    atif_steps.append(atif_step)

if not atif_steps:
    atif_steps = [{{
        "step_id": 1,
        "source": "user",
        "message": INSTRUCTION,
        "timestamp": datetime.now(timezone.utc).isoformat(),
    }}]

trajectory = {{
    "schema_version": "ATIF-v1.6",
    "session_id": session_id,
    "agent": {{
        "name": "hpdos",
        "version": "0.1.0",
        "model_name": MODEL_ID or None,
    }},
    "steps": atif_steps,
    "final_metrics": {{
        "total_prompt_tokens": None,
        "total_completion_tokens": None,
        "total_cached_tokens": None,
        "total_cost_usd": None,
        "total_steps": len(atif_steps),
    }},
}}

traj_path = LOGS_DIR / "trajectory.json"
traj_path.write_text(json.dumps(trajectory, indent=2), encoding="utf-8")
print(f"\\nTrajectory -> {{traj_path}}", flush=True)
"""
