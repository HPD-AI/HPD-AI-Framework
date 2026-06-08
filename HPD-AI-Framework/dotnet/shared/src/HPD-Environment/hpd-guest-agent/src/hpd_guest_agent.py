#!/usr/bin/env python3
import argparse
import base64
import datetime
import json
import os
import selectors
import shutil
import socket
import subprocess
import sys
import time
import uuid

DEFAULT_PROTOCOL_VERSION = "1.0"
DEFAULT_AGENT_VERSION = "0.1.0"
DEFAULT_PORT = 7777
MAX_FRAME_BYTES = 1048576
MAX_TCP_PROXY_BYTES = 1048576

AF_VSOCK = getattr(socket, "AF_VSOCK", 40)
VMADDR_CID_ANY = getattr(socket, "VMADDR_CID_ANY", 0xFFFFFFFF)


def capabilities():
    return {
        "ProcessStart": True,
        "ProcessStdin": True,
        "ProcessSignal": True,
        "ProcessStop": True,
        "ProcessReadOutput": True,
        "Pty": False,
        "ProcessResize": False,
        "ProjectionMount": True,
        "ProjectionObserve": False,
        "ProjectionSync": False,
        "ProjectionFinalize": False,
        "ProjectionPromote": False,
        "NetworkStatus": True,
        "AuthorityProjection": True,
        "AuthorityRevocation": True,
        "EngineStatus": True,
        "EngineProvisioning": False,
        "Limitations": [
            "readiness-only guest agent; process, projection, authority, and engine execution are implemented by later guest payloads"
        ],
    }


class GuestAgent:
    def __init__(self, agent_version, protocol_version, guest_boot_id=None):
        self.agent_version = agent_version
        self.protocol_version = protocol_version
        self.guest_boot_id = guest_boot_id or self._default_boot_id()
        self.guest_boot_generation = 1
        self.guest_agent_generation = 1
        self.processes = {}

    def _default_boot_id(self):
        try:
            with open("/proc/sys/kernel/random/boot_id", "r", encoding="utf-8") as boot_id:
                value = boot_id.read().strip()
                if value:
                    return value
        except OSError:
            pass
        return "guest-boot-" + str(uuid.uuid4())

    def response_base(self, request, operation):
        return {
            "ProtocolVersion": self.protocol_version,
            "MessageType": 1,
            "Operation": operation,
            "RequestId": request.get("RequestId"),
            "CausationId": request.get("RequestId"),
            "SequenceNumber": int(request.get("SequenceNumber", 0)) + 1,
            "Timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "HostId": request.get("HostId"),
            "GuestBootId": self.guest_boot_id,
            "GuestBootGeneration": self.guest_boot_generation,
            "GuestAgentGeneration": self.guest_agent_generation,
            "ResponseStatus": 0,
        }

    def error(self, request, operation, code, message, retryable=False):
        payload = self.response_base(request, operation)
        payload["ResponseStatus"] = 2
        payload["Error"] = {
            "Code": code,
            "Message": message,
            "Operation": str(operation),
            "Retryable": retryable,
            "FailedPhase": "GuestAgent",
            "Severity": 4,
        }
        return payload

    def handle(self, request):
        operation = request.get("Operation")
        if operation == "hello":
            operation = 0
        elif operation == "ready":
            operation = 2

        if operation == 0:
            payload = self.response_base(request, 0)
            payload["Hello"] = {
                "AgentName": "hpd-guest-agent",
                "AgentVersion": self.agent_version,
                "ProtocolVersion": self.protocol_version,
                "GuestBootId": self.guest_boot_id,
                "GuestBootGeneration": self.guest_boot_generation,
                "GuestAgentGeneration": self.guest_agent_generation,
                "Hostname": socket.gethostname(),
                "RuntimeUser": str(os.getuid()) if hasattr(os, "getuid") else None,
                "ProtocolCompatible": request.get("ProtocolVersion", self.protocol_version) == self.protocol_version,
                "Capabilities": capabilities(),
            }
            return payload

        if operation == 2:
            payload = self.response_base(request, 2)
            payload["Ready"] = {
                "IsReady": True,
                "GuestBootId": self.guest_boot_id,
                "GuestBootGeneration": self.guest_boot_generation,
                "GuestAgentGeneration": self.guest_agent_generation,
                "Conditions": [],
                "Diagnostics": [],
            }
            return payload

        if operation == 22:
            return self.process_start(request)

        if operation == 27:
            return self.process_wait(request)

        if operation == 28:
            return self.process_read_output(request)

        if operation == 29:
            return self.network_status(request)

        if operation in (44, 45, 46):
            return self.authority_binding(request, operation)

        if operation == 49:
            return self.tcp_proxy(request)

        return self.error(request, operation if isinstance(operation, int) else 0, "AppleVirtualization.GuestAgentUnsupportedOperation", "Unsupported guest-agent operation.", retryable=False)

    def process_start(self, request):
        start = request.get("ProcessStartRequest") or {}
        process_id = str(start.get("ProcessId") or request.get("ProcessId") or ("guest-process-" + str(uuid.uuid4())))
        command = start.get("Command") or {}
        file_name = command.get("FileName")
        if not file_name:
            return self.error(request, 22, "AppleVirtualization.GuestAgentProcessCommandMissing", "ProcessStartRequest.Command.FileName is required.", retryable=False)

        arguments = command.get("Arguments") or []
        if not isinstance(arguments, list) or not all(isinstance(argument, str) for argument in arguments):
            return self.error(request, 22, "AppleVirtualization.GuestAgentProcessArgumentsInvalid", "ProcessStartRequest.Command.Arguments must be a string array.", retryable=False)

        working_directory = command.get("WorkingDirectory") or None
        isolation = start.get("Isolation") or {}
        sandbox_plan = start.get("SandboxPlan") or {}
        effective_isolation = self.effective_isolation(isolation, sandbox_plan)
        environment_result = self.prepare_process_environment(command, effective_isolation)
        if "error" in environment_result:
            return self.error(
                request,
                22,
                environment_result["code"],
                environment_result["message"],
                retryable=False)
        environment = environment_result["environment"]
        launch_result = self.prepare_process_launch(file_name, arguments, working_directory, effective_isolation)
        if "error" in launch_result:
            return self.error(
                request,
                22,
                launch_result["code"],
                launch_result["message"],
                retryable=False)

        try:
            popen = subprocess.Popen(
                launch_result["argv"],
                cwd=launch_result["cwd"],
                env=environment,
                stdin=subprocess.PIPE,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE)
        except Exception as exc:
            return self.error(request, 22, "AppleVirtualization.GuestAgentProcessStartFailed", "Failed to start guest process: " + str(exc), retryable=False)

        self.processes[process_id] = {
            "popen": popen,
            "started_at": self.timestamp(),
            "stdout": b"",
            "stderr": b"",
        }
        payload = self.response_base(request, 22)
        payload["ProcessStatusResponse"] = {
            "ProcessId": process_id,
            "ProcessPhase": 3,
            "IoState": 1,
            "ProviderProcessId": "guest-" + process_id,
            "SystemProcessId": popen.pid,
            "Conditions": [],
        }
        return payload

    def is_isolation_required(self, isolation):
        if not isinstance(isolation, dict):
            return False

        mode = isolation.get("Mode")
        if isinstance(mode, str):
            return mode.lower() == "isolated"
        if isinstance(mode, (int, float)):
            return int(mode) == 2
        return False

    def effective_isolation(self, isolation, sandbox_plan):
        if isinstance(sandbox_plan, dict):
            plan = self.case_dict(sandbox_plan.get("Plan"))
            if plan:
                return {
                    "Mode": 2,
                    "Filesystem": plan.get("Filesystem") or {},
                    "Network": plan.get("Network") or {},
                    "UnixSockets": plan.get("UnixSockets") or {},
                    "Environment": plan.get("Environment") or {},
                    "TlsTrust": plan.get("Tls") or {},
                    "Interactive": plan.get("Interactive") or {},
                    "AuthorityBindings": plan.get("AuthorityBindings") or [],
                }

        return isolation if isinstance(isolation, dict) else {}

    def prepare_process_environment(self, command, isolation):
        requested_environment = command.get("Environment") or {}
        if not isinstance(requested_environment, dict):
            requested_environment = {}

        if not self.is_isolation_required(isolation):
            environment = os.environ.copy()
            for key, value in requested_environment.items():
                if isinstance(key, str) and value is not None:
                    environment[key] = str(value)
            return {"environment": environment}

        unsupported = self.unsupported_isolation_features(isolation)
        if unsupported:
            return {
                "error": True,
                "code": "AppleVirtualization.GuestAgentProcessIsolationUnsupported",
                "message": "ProcessIsolationPolicy.Mode=Isolated requested unsupported guest-side features: " + ", ".join(unsupported) + ".",
            }

        policy = self.case_dict(isolation.get("Environment"))
        allowed = policy.get("AllowedVariables") or []
        if not isinstance(allowed, list):
            allowed = []
        allowed_names = {str(name) for name in allowed if isinstance(name, str)}
        strip_unlisted = bool(policy.get("StripUnlistedVariables", True))

        environment = {} if strip_unlisted else os.environ.copy()
        for key, value in requested_environment.items():
            if not isinstance(key, str) or value is None:
                continue
            if not strip_unlisted or key in allowed_names:
                environment[key] = str(value)

        injected = policy.get("InjectedVariables") or {}
        if isinstance(injected, dict):
            for key, value in injected.items():
                if isinstance(key, str) and value is not None:
                    environment[key] = str(value)

        return {"environment": environment}

    def prepare_process_launch(self, file_name, arguments, working_directory, isolation):
        if not self.is_isolation_required(isolation):
            return {"argv": [file_name, *arguments], "cwd": working_directory}

        filesystem = self.case_dict(isolation.get("Filesystem"))
        rules = filesystem.get("Rules") or []
        if not isinstance(rules, list) or not rules:
            return {"argv": [file_name, *arguments], "cwd": working_directory}

        bwrap_path = shutil.which("bwrap")
        if not bwrap_path:
            return {
                "error": True,
                "code": "AppleVirtualization.GuestAgentProcessIsolationUnavailable",
                "message": "ProcessIsolationPolicy requested guest-side filesystem isolation, but bubblewrap (bwrap) is not installed in the guest.",
            }

        mount_plan = self.filesystem_mount_plan(filesystem)
        if "error" in mount_plan:
            return mount_plan

        if not mount_plan["mounts"]:
            return {"argv": [file_name, *arguments], "cwd": working_directory}

        argv = [
            bwrap_path,
            "--new-session",
            "--die-with-parent",
            "--ro-bind",
            "/",
            "/",
            "--tmpfs",
            "/tmp",
        ]

        for mount in mount_plan["mounts"]:
            kind = mount["kind"]
            if kind == "bind":
                argv.extend(["--bind", mount["source"], mount["destination"]])
            elif kind == "ro-bind":
                argv.extend(["--ro-bind", mount["source"], mount["destination"]])
            elif kind == "tmpfs":
                argv.extend(["--tmpfs", mount["destination"]])

        argv.extend([
            "--dev",
            "/dev",
            "--unshare-pid",
            "--unshare-uts",
            "--proc",
            "/proc",
        ])

        if working_directory:
            argv.extend(["--chdir", working_directory])

        argv.extend(["--", file_name, *arguments])
        return {"argv": argv, "cwd": None}

    def filesystem_mount_plan(self, filesystem):
        rules = filesystem.get("Rules") or []
        writable_mounts = []
        deny_read_mounts = []
        allow_read_mounts = []
        deny_write_mounts = []
        dangerous = self.case_dict(filesystem.get("DangerousPaths"))

        for rule in rules:
            kind = self.path_rule_kind(rule)
            path = self.path_rule_path(rule)
            if kind is None or path is None:
                continue

            if kind == 0:
                mount = self.read_allow_mount(path)
                if mount is not None:
                    allow_read_mounts.append(mount)
            elif kind == 1:
                deny_read_mounts.extend(self.read_deny_mounts(path))
            elif kind == 2:
                result = self.write_allow_mount(path)
                if "error" in result:
                    return result
                if result.get("mount") is not None:
                    writable_mounts.append(result["mount"])
            elif kind == 3:
                deny_write_mounts.extend(self.write_deny_mounts(path, writable_mounts))

        for denied in self.host_path_values(dangerous.get("AdditionalDeniedReads")):
            deny_read_mounts.extend(self.read_deny_mounts(denied))
        for denied in self.host_path_values(dangerous.get("AdditionalDeniedWrites")):
            deny_write_mounts.extend(self.write_deny_mounts(denied, writable_mounts))

        mounts = []
        seen = set()
        self.add_unique_mounts(mounts, seen, writable_mounts)
        self.add_unique_mounts(mounts, seen, deny_read_mounts)
        self.add_unique_mounts(mounts, seen, writable_mounts)
        self.add_unique_mounts(mounts, seen, allow_read_mounts)
        self.add_unique_mounts(mounts, seen, deny_write_mounts)
        return {"mounts": mounts}

    def write_allow_mount(self, path):
        if not os.path.exists(path):
            return {
                "error": True,
                "code": "AppleVirtualization.GuestAgentProcessIsolationUnsupported",
                "message": "ProcessIsolationPolicy requested writable path that does not exist in the guest: " + path + ".",
            }
        if path.startswith("/dev/"):
            return {"mount": None}
        return {"mount": {"kind": "bind", "source": path, "destination": path}}

    def read_deny_mounts(self, path):
        if self.is_root_path(path):
            mounts = []
            for name in os.listdir("/"):
                child = self.normalize_path(os.path.join("/", name))
                if child in ("/dev", "/proc", "/sys"):
                    continue
                mounts.extend(self.read_deny_mounts(child))
            return mounts

        if os.path.isdir(path):
            return [{"kind": "tmpfs", "source": None, "destination": path}]
        if os.path.isfile(path):
            return [{"kind": "ro-bind", "source": "/dev/null", "destination": path}]
        return []

    def read_allow_mount(self, path):
        if not os.path.exists(path):
            return None
        return {"kind": "ro-bind", "source": path, "destination": path}

    def write_deny_mounts(self, path, writable_mounts):
        writable_roots = [mount["destination"] for mount in writable_mounts]
        if not any(self.is_within_path(path, root) for root in writable_roots):
            return []

        if os.path.exists(path):
            return [{"kind": "ro-bind", "source": path, "destination": path}]

        first_missing = self.first_missing_component(path)
        if first_missing is None:
            return []

        if self.same_path(first_missing, path):
            return [{"kind": "ro-bind", "source": "/dev/null", "destination": path}]

        return [{"kind": "tmpfs", "source": None, "destination": first_missing}]

    def path_rule_kind(self, rule):
        if not isinstance(rule, dict):
            return None

        return self.enum_value(
            rule.get("Kind"),
            {
                "allowread": 0,
                "denyread": 1,
                "allowwrite": 2,
                "denywrite": 3,
            })

    def path_rule_path(self, rule):
        if not isinstance(rule, dict):
            return None
        path = self.case_dict(rule.get("Path"))
        value = path.get("Value")
        if not isinstance(value, str) or not value:
            return None
        return self.normalize_path(value)

    def host_path_values(self, values):
        if not isinstance(values, list):
            return []
        paths = []
        for value in values:
            path = self.case_dict(value)
            raw = path.get("Value")
            if isinstance(raw, str) and raw:
                paths.append(self.normalize_path(raw))
        return paths

    def add_unique_mounts(self, mounts, seen, candidates):
        for mount in candidates:
            key = (mount["kind"], mount.get("source"), mount["destination"])
            if key not in seen:
                seen.add(key)
                mounts.append(mount)

    def normalize_path(self, value):
        return os.path.abspath(os.path.expandvars(os.path.expanduser(value)))

    def is_root_path(self, path):
        return self.same_path(path, os.path.abspath(os.sep))

    def same_path(self, left, right):
        return os.path.normcase(os.path.abspath(left)) == os.path.normcase(os.path.abspath(right))

    def is_within_path(self, path, root):
        normalized_path = os.path.normcase(os.path.abspath(path))
        normalized_root = os.path.normcase(os.path.abspath(root))
        return normalized_path == normalized_root or normalized_path.startswith(normalized_root.rstrip(os.sep) + os.sep)

    def first_missing_component(self, path):
        parts = [part for part in os.path.abspath(path).split(os.sep) if part]
        current = os.sep
        for part in parts:
            current = os.path.join(current, part)
            if not os.path.exists(current):
                return current
        return None

    def unsupported_isolation_features(self, isolation):
        unsupported = []

        filesystem = self.case_dict(isolation.get("Filesystem"))
        rules = filesystem.get("Rules") or []
        dangerous = self.case_dict(filesystem.get("DangerousPaths"))
        if isinstance(rules, list) and rules:
            unsupported.extend(self.unsupported_filesystem_rules(rules))
        if bool(dangerous.get("ProtectSensitiveDefaults", False)):
            unsupported.append("filesystem.dangerous-paths")

        network = self.case_dict(isolation.get("Network"))
        network_mode = self.enum_value(network.get("Mode"), {"blocked": 0, "filtered": 1, "unrestricted": 2})
        if network_mode is not None and network_mode != 2:
            unsupported.append("network.egress")

        unix_sockets = self.case_dict(isolation.get("UnixSockets"))
        allowed_sockets = unix_sockets.get("AllowedSockets") or []
        if bool(unix_sockets.get("AllowAll", False)) or (isinstance(allowed_sockets, list) and allowed_sockets):
            unsupported.append("unix-sockets")

        tls = self.case_dict(isolation.get("TlsTrust"))
        tls_mode = self.enum_value(tls.get("Mode"), {"none": 0, "ephemeralauthority": 1, "ephemeralproviderauthority": 2, "authoritybinding": 3})
        if (tls_mode is not None and tls_mode != 0) or bool(tls.get("InjectTrustEnvironmentVariables", False)):
            unsupported.append("tls-trust")

        interactive = self.case_dict(isolation.get("Interactive"))
        mach_lookups = interactive.get("AllowedMachLookups") or []
        if bool(interactive.get("AllowPty", False)) or bool(interactive.get("AllowLocalBinding", False)) or (isinstance(mach_lookups, list) and mach_lookups):
            unsupported.append("interactive")

        bindings = isolation.get("AuthorityBindings") or []
        if isinstance(bindings, list) and bindings:
            unsupported.append("authority-bindings")

        return unsupported

    def unsupported_filesystem_rules(self, rules):
        unsupported = []
        for rule in rules:
            if not isinstance(rule, dict):
                unsupported.append("filesystem.rule.invalid")
                continue

            kind = self.enum_value(
                rule.get("Kind"),
                {
                    "allowread": 0,
                    "denyread": 1,
                    "allowwrite": 2,
                    "denywrite": 3,
                })
            if kind not in (0, 1, 2, 3):
                unsupported.append("filesystem.rule." + str(rule.get("Kind")))
                continue

            pattern_kind = self.enum_value(
                rule.get("PatternKind"),
                {
                    "literal": 0,
                    "literalorsubpath": 1,
                    "glob": 2,
                    "providervalidate": 3,
                })
            if pattern_kind is not None and pattern_kind not in (0, 1):
                unsupported.append("filesystem.rule.pattern")

            path = self.case_dict(rule.get("Path"))
            value = path.get("Value")
            if not isinstance(value, str) or not value:
                unsupported.append("filesystem.rule.path")

        return unsupported

    def case_dict(self, value):
        if not isinstance(value, dict):
            return {}
        return {str(key): item for key, item in value.items()}

    def enum_value(self, value, names):
        if isinstance(value, str):
            return names.get(value.replace("_", "").replace("-", "").lower())
        if isinstance(value, (int, float)):
            return int(value)
        return None

    def process_wait(self, request):
        lifecycle = request.get("ProcessLifecycleRequest") or {}
        process_id = str(lifecycle.get("ProcessId") or request.get("ProcessId") or "")
        state = self.processes.get(process_id)
        if state is None:
            return self.error(request, 27, "AppleVirtualization.GuestAgentProcessMissing", "Guest process was not found.", retryable=False)

        timeout_ms = lifecycle.get("TimeoutMilliseconds")
        timeout_seconds = None
        if isinstance(timeout_ms, (int, float)) and timeout_ms > 0:
            timeout_seconds = timeout_ms / 1000.0

        popen = state["popen"]
        try:
            stdout, stderr = popen.communicate(timeout=timeout_seconds)
        except subprocess.TimeoutExpired:
            return self.error(request, 27, "AppleVirtualization.GuestAgentProcessWaitTimeout", "Timed out waiting for guest process.", retryable=True)

        state["stdout"] = stdout or b""
        state["stderr"] = stderr or b""
        exited_at = self.timestamp()
        payload = self.response_base(request, 27)
        payload["ProcessStatusResponse"] = {
            "ProcessId": process_id,
            "ProcessPhase": 6,
            "IoState": 4,
            "ProviderProcessId": "guest-" + process_id,
            "SystemProcessId": popen.pid,
            "Result": self.process_result(process_id, popen, state, exited_at),
            "Conditions": [],
        }
        return payload

    def process_read_output(self, request):
        lifecycle = request.get("ProcessLifecycleRequest") or {}
        process_id = str(lifecycle.get("ProcessId") or request.get("ProcessId") or "")
        state = self.processes.get(process_id)
        if state is None:
            return self.error(request, 28, "AppleVirtualization.GuestAgentProcessMissing", "Guest process was not found.", retryable=False)

        payload = self.response_base(request, 28)
        payload["ProcessStatusResponse"] = {
            "ProcessId": process_id,
            "ProcessPhase": 3 if state["popen"].poll() is None else 6,
            "IoState": 1 if state["popen"].poll() is None else 4,
            "ProviderProcessId": "guest-" + process_id,
            "SystemProcessId": state["popen"].pid,
            "Conditions": [],
        }
        return payload

    def process_result(self, process_id, popen, state, exited_at):
        stdout = state.get("stdout", b"")
        stderr = state.get("stderr", b"")
        return {
            "ProcessId": {"Value": process_id},
            "SystemProcessId": popen.pid,
            "ProviderProcessId": "guest-" + process_id,
            "ExitCode": popen.returncode,
            "CompletionKind": 1,
            "StartedAt": state.get("started_at"),
            "ExitedAt": exited_at,
            "Duration": "00:00:00",
            "Output": {
                "Stdout": self.stream_output(stdout),
                "Stderr": self.stream_output(stderr),
                "MergedStandardError": False,
                "OutputDrainTimedOut": False,
                "OutputDrainTimeout": "00:00:02",
            },
            "Violations": [],
            "Diagnostics": [],
        }

    def stream_output(self, value):
        return {
            "CapturedBytes": base64.b64encode(value).decode("ascii"),
            "BytesObserved": len(value),
            "BytesCaptured": len(value),
            "BytesDiscarded": 0,
            "Truncated": False,
        }

    def network_status(self, request):
        status = request.get("NetworkStatusRequest") or {}
        max_interfaces = self.positive_int(status.get("MaxInterfaces"), 16)
        max_routes = self.positive_int(status.get("MaxRoutes"), 64)
        max_listeners = self.positive_int(status.get("MaxListeners"), 128)
        include_routes = bool(status.get("IncludeRoutes", True))
        include_listeners = bool(status.get("IncludeListeners", True))

        interfaces = self.network_interfaces()
        routes = self.network_routes() if include_routes else []
        listeners = self.network_listeners() if include_listeners else []

        payload = self.response_base(request, 29)
        payload["NetworkStatus"] = {
            "HostId": status.get("HostId") or request.get("HostId"),
            "UnitId": status.get("UnitId"),
            "GuestAgentReady": True,
            "Interfaces": interfaces[:max_interfaces],
            "Routes": routes[:max_routes],
            "Listeners": listeners[:max_listeners],
            "InterfacesTruncated": len(interfaces) > max_interfaces,
            "RoutesTruncated": len(routes) > max_routes,
            "ListenersTruncated": len(listeners) > max_listeners,
            "Generation": {
                "GuestBootId": self.guest_boot_id,
                "GuestBootGeneration": self.guest_boot_generation,
                "GuestAgentGeneration": self.guest_agent_generation,
            },
            "Limitations": [],
            "Conditions": [],
        }
        return payload

    def tcp_proxy(self, request):
        proxy = request.get("TcpProxyRequest") or {}
        target_address = str(proxy.get("TargetAddress") or "127.0.0.1")
        target_port = self.positive_int(proxy.get("TargetPort"), 0)
        request_bytes = proxy.get("RequestBytes") or ""
        if target_port <= 0:
            return self.error(request, 49, "AppleVirtualization.GuestAgentTcpProxyPortInvalid", "TcpProxyRequest.TargetPort is required.", retryable=False)

        try:
            outbound = base64.b64decode(request_bytes, validate=True)
        except Exception:
            return self.error(request, 49, "AppleVirtualization.GuestAgentTcpProxyRequestInvalid", "TcpProxyRequest.RequestBytes must be base64.", retryable=False)

        if len(outbound) > MAX_TCP_PROXY_BYTES:
            return self.error(request, 49, "AppleVirtualization.GuestAgentTcpProxyRequestTooLarge", "TcpProxyRequest.RequestBytes exceeds the bounded proxy limit.", retryable=False)

        try:
            response = self.tcp_proxy_roundtrip(target_address, target_port, outbound)
        except OSError as exc:
            if target_address not in ("127.0.0.1", "localhost"):
                try:
                    target_address = "127.0.0.1"
                    response = self.tcp_proxy_roundtrip(target_address, target_port, outbound)
                except OSError as fallback_exc:
                    return self.error(request, 49, "AppleVirtualization.GuestAgentTcpProxyFailed", "Guest TCP proxy failed: " + str(fallback_exc), retryable=True)
            else:
                return self.error(request, 49, "AppleVirtualization.GuestAgentTcpProxyFailed", "Guest TCP proxy failed: " + str(exc), retryable=True)

        payload = self.response_base(request, 49)
        payload["TcpProxyResponse"] = {
            "TargetAddress": target_address,
            "TargetPort": target_port,
            "ResponseBytes": base64.b64encode(response).decode("ascii"),
            "BytesObserved": len(response),
            "Truncated": len(response) >= MAX_TCP_PROXY_BYTES,
        }
        return payload

    def tcp_proxy_roundtrip(self, target_address, target_port, outbound):
        with socket.create_connection((target_address, target_port), timeout=3) as target:
            target.settimeout(3)
            if outbound:
                target.sendall(outbound)
            chunks = []
            total = 0
            while total < MAX_TCP_PROXY_BYTES:
                try:
                    chunk = target.recv(min(65536, MAX_TCP_PROXY_BYTES - total))
                except socket.timeout:
                    break
                if not chunk:
                    break
                chunks.append(chunk)
                total += len(chunk)
        return b"".join(chunks)

    def network_interfaces(self):
        data = self.json_command(["ip", "-j", "address", "show"])
        interfaces = []
        if not isinstance(data, list):
            return self.hostname_interfaces()

        for item in data:
            if not isinstance(item, dict):
                continue
            name = str(item.get("ifname") or "")
            if not name or name == "lo":
                continue
            addresses = []
            for addr in item.get("addr_info") or []:
                if not isinstance(addr, dict):
                    continue
                family = addr.get("family")
                local = addr.get("local")
                if family not in ("inet", "inet6") or not isinstance(local, str):
                    continue
                value = self.ip_address_value(local)
                if value is None:
                    continue
                addresses.append({
                    "Address": value,
                    "PrefixLength": int(addr.get("prefixlen") or 0),
                    "Kind": 3,
                    "IsPrimary": len(addresses) == 0 and family == "inet",
                })
            if not addresses:
                continue
            if not any(address.get("IsPrimary") for address in addresses):
                addresses[0]["IsPrimary"] = True
            interfaces.append({
                "Name": name,
                "Mtu": int(item.get("mtu") or 0) or None,
                "IsUp": "UP" in (item.get("flags") or []),
                "Addresses": addresses,
            })
        return interfaces

    def hostname_interfaces(self):
        try:
            completed = subprocess.run(["hostname", "-I"], check=False, capture_output=True, text=True, timeout=2)
        except Exception:
            return []
        if completed.returncode != 0:
            return []

        addresses = []
        for item in completed.stdout.split():
            value = self.ip_address_value(item)
            if value is None:
                continue
            addresses.append({
                "Address": value,
                "PrefixLength": 32 if value["Family"] == 0 else 128,
                "Kind": 3,
                "IsPrimary": len(addresses) == 0 and value["Family"] == 0,
            })
        if not addresses:
            return []
        if not any(address.get("IsPrimary") for address in addresses):
            addresses[0]["IsPrimary"] = True

        return [{
            "Name": "default",
            "Mtu": None,
            "IsUp": True,
            "Addresses": addresses,
        }]

    def network_routes(self):
        data = self.json_command(["ip", "-j", "route", "show", "default"])
        routes = []
        if not isinstance(data, list):
            return routes

        for item in data:
            if not isinstance(item, dict):
                continue
            gateway = self.ip_address_value(str(item.get("gateway"))) if item.get("gateway") else None
            routes.append({
                "Gateway": gateway,
                "InterfaceName": item.get("dev"),
                "IsDefault": item.get("dst") == "default" or "dst" not in item,
            })
        return routes

    def network_listeners(self):
        data = self.json_command(["ss", "-H", "-ltn"])
        if data is not None:
            return []

        try:
            completed = subprocess.run(["ss", "-H", "-ltn"], check=False, capture_output=True, text=True, timeout=2)
        except Exception:
            return []

        listeners = []
        for line in completed.stdout.splitlines():
            parts = line.split()
            if len(parts) < 4:
                continue
            local = parts[3]
            if ":" not in local:
                continue
            address, port_text = local.rsplit(":", 1)
            try:
                port = int(port_text)
            except ValueError:
                continue
            value = self.ip_address_value(address.strip("[]")) if address not in ("*", "0.0.0.0", "[::]") else None
            listeners.append({
                "Name": "guest-tcp-listener-" + str(port),
                "Transport": 0,
                "Address": value,
                "Port": {"Value": port},
                "GuestVisibleOnly": True,
                "HpdPublished": False,
            })
        return listeners

    def json_command(self, command):
        for candidate in self.command_candidates(command):
            try:
                completed = subprocess.run(candidate, check=False, capture_output=True, text=True, timeout=2)
            except Exception:
                continue
            if completed.returncode != 0 or not completed.stdout.strip():
                continue
            try:
                return json.loads(completed.stdout)
            except json.JSONDecodeError:
                return None
        return None

    def command_candidates(self, command):
        if not command:
            return []
        executable = command[0]
        candidates = []
        resolved = shutil.which(executable)
        if resolved:
            candidates.append([resolved] + command[1:])
        candidates.append(command)
        if "/" not in executable:
            for prefix in ("/usr/sbin", "/sbin", "/usr/bin", "/bin"):
                candidates.append([os.path.join(prefix, executable)] + command[1:])

        unique = []
        seen = set()
        for candidate in candidates:
            key = tuple(candidate)
            if key in seen:
                continue
            seen.add(key)
            unique.append(candidate)
        return unique

    def ip_address_value(self, address):
        try:
            packed = socket.inet_pton(socket.AF_INET, address)
            return {"Family": 0, "HighBits": 0, "LowBits": int.from_bytes(packed, "big")}
        except OSError:
            pass
        try:
            packed = socket.inet_pton(socket.AF_INET6, address)
            return {
                "Family": 1,
                "HighBits": int.from_bytes(packed[:8], "big"),
                "LowBits": int.from_bytes(packed[8:], "big"),
            }
        except OSError:
            return None

    def positive_int(self, value, default):
        try:
            parsed = int(value)
            return parsed if parsed >= 0 else default
        except (TypeError, ValueError):
            return default

    def timestamp(self):
        return datetime.datetime.now(datetime.timezone.utc).isoformat().replace("+00:00", "Z")

    def authority_binding(self, request, operation):
        binding = request.get("AuthorityBindingRequest") or {}
        binding_id = str(binding.get("BindingId") or "unknown")
        source = binding.get("Source") or {}
        target = binding.get("Target") or {}
        projection = binding.get("Projection") or {}
        target_socket = self.socket_path(projection.get("TargetSocketPath"))
        source_socket = self.authority_source_socket(binding, source, projection)
        audit_correlation_id = str(binding.get("AuditCorrelationId") or ("authority-" + binding_id))

        if not target_socket or not target_socket.startswith("/"):
            return self.error(request, operation, "AppleVirtualization.GuestAgentAuthorityTargetSocketInvalid", "Authority socket projection requires an absolute target socket path.", retryable=False)

        if operation == 44:
            if not os.path.exists(source_socket):
                return self.error(request, operation, "AppleVirtualization.GuestAgentAuthoritySourceSocketMissing", "Guest engine source socket is missing.", retryable=True)
            try:
                os.makedirs(os.path.dirname(target_socket), mode=0o755, exist_ok=True)
                if os.path.lexists(target_socket):
                    if os.path.islink(target_socket):
                        os.unlink(target_socket)
                    elif target_socket == source_socket:
                        pass
                    else:
                        return self.error(request, operation, "AppleVirtualization.GuestAgentAuthorityTargetExists", "Authority target socket path already exists and is not a managed projection.", retryable=False)
                if not os.path.lexists(target_socket):
                    os.symlink(source_socket, target_socket)
            except OSError as exc:
                return self.error(request, operation, "AppleVirtualization.GuestAgentAuthorityBindFailed", "Failed to project authority socket: " + str(exc), retryable=False)
            return self.authority_response(
                request,
                operation,
                binding,
                source,
                target,
                projection,
                binding_id,
                target_socket,
                source_socket,
                phase=2,
                revocation_status=1,
                audit_kind=0,
                audit_correlation_id=audit_correlation_id,
                conditions=[
                    self.condition(
                        "AppleVirtualization.GuestAgentAuthorityProjected",
                        "Projected",
                        "Authority socket projection is present and managed by the guest agent.",
                    )
                ],
                revocation_evidence=[self.socket_evidence(6, target_socket, True, "Projected authority socket is present.")],
            )

        if operation == 45:
            observation = self.authority_projection_observation(source_socket, target_socket)
            return self.authority_response(
                request,
                operation,
                binding,
                source,
                target,
                projection,
                binding_id,
                target_socket,
                source_socket,
                phase=observation["phase"],
                revocation_status=observation["revocation_status"],
                audit_kind=7 if observation["phase"] == 3 else 3,
                audit_correlation_id=audit_correlation_id,
                conditions=observation["conditions"],
                revocation_evidence=observation["evidence"],
            )

        if operation == 46:
            before = self.authority_projection_observation(source_socket, target_socket)
            try:
                if os.path.islink(target_socket):
                    os.unlink(target_socket)
            except OSError as exc:
                return self.error(request, operation, "AppleVirtualization.GuestAgentAuthorityRevokeFailed", "Failed to revoke authority socket projection: " + str(exc), retryable=False)
            after = self.authority_projection_observation(source_socket, target_socket)
            evidence = []
            evidence.extend(before["evidence"])
            evidence.append(self.socket_evidence(3, target_socket, not os.path.lexists(target_socket), "Projected authority socket is absent after revoke."))
            return self.authority_response(
                request,
                operation,
                binding,
                source,
                target,
                projection,
                binding_id,
                target_socket,
                source_socket,
                phase=5,
                revocation_status=2 if not os.path.lexists(target_socket) else 3,
                audit_kind=5,
                audit_correlation_id=audit_correlation_id,
                conditions=[
                    self.condition(
                        "AppleVirtualization.GuestAgentAuthorityRevoked",
                        "Revoked",
                        "Authority socket projection was removed by the guest agent.",
                    )
                    if not os.path.lexists(target_socket)
                    else self.condition(
                        "AppleVirtualization.GuestAgentAuthorityRevokeIncomplete",
                        "ProjectionStillPresent",
                        "Authority socket projection still exists after revoke.",
                        severity=4,
                    )
                ],
                revocation_evidence=evidence if evidence else after["evidence"],
            )

        return self.error(request, operation, "AppleVirtualization.GuestAgentUnsupportedOperation", "Unsupported authority operation.", retryable=False)

    def authority_response(
        self,
        request,
        operation,
        binding,
        source,
        target,
        projection,
        binding_id,
        target_socket,
        source_socket,
        phase,
        revocation_status,
        audit_kind,
        audit_correlation_id,
        conditions,
        revocation_evidence,
    ):
        source_kind = self.int_value(source.get("Kind"), 1)
        target_kind = self.int_value(target.get("Kind"), 0)
        projection_kind = self.int_value(projection.get("Kind"), 0)
        direction = self.int_value(binding.get("Direction"), 3)
        authority_class = self.int_value(binding.get("EffectiveAuthorityClass"), 4)
        redaction = self.int_value(binding.get("Redaction"), 2)
        timestamp = self.timestamp()
        payload = self.response_base(request, operation)
        payload["AuthorityBindingResponse"] = {
            "BindingId": binding_id,
            "BindingPhase": phase,
            "BoundAuthority": {
                "BindingId": binding_id,
                "SourceKind": source_kind,
                "ProjectionKind": projection_kind,
                "Direction": direction,
                "EffectiveAuthorityClass": authority_class,
                "Redaction": redaction,
                "TargetSocketPath": {"Value": target_socket},
                "BoundAt": timestamp,
                "RotationGeneration": 0,
                "RevocationStatus": revocation_status,
                "AuditCorrelationId": audit_correlation_id,
                "SensitiveEndpointKind": self.int_value(source.get("SensitiveEndpointKind"), 1),
            },
            "RevocationStatus": revocation_status,
            "RevocationEvidence": revocation_evidence,
            "AuditEvents": [
                {
                    "Kind": audit_kind,
                    "SourceKind": source_kind,
                    "TargetKind": target_kind,
                    "Timestamp": timestamp,
                    "CorrelationId": audit_correlation_id,
                }
            ],
            "AuditEventsTruncated": False,
            "Limitations": [],
            "Conditions": conditions,
            "Diagnostics": [],
        }
        return payload

    def authority_source_socket(self, binding, source, projection):
        source_name = str(source.get("RedactedDisplayName") or binding.get("AuditLabel") or projection.get("TargetSocketPath") or "").lower()
        target_socket = self.socket_path(projection.get("TargetSocketPath")) or ""
        if "containerd" in source_name or "containerd" in target_socket:
            return os.environ.get("HPD_GUEST_AGENT_CONTAINERD_SOCKET", "/run/containerd/containerd.sock")
        if "podman" in source_name or "podman" in target_socket:
            if "rootful" in source_name or "rootful" in target_socket:
                return os.environ.get("HPD_GUEST_AGENT_PODMAN_SOCKET", "/run/podman/podman.sock")
            return os.environ.get("HPD_GUEST_AGENT_PODMAN_SOCKET", "/run/user/1000/podman/podman.sock")
        if "buildkit" in source_name or "buildkit" in target_socket:
            if "rootful" in source_name or "rootful" in target_socket:
                return os.environ.get("HPD_GUEST_AGENT_BUILDKIT_SOCKET", "/run/buildkit/buildkitd.sock")
            return os.environ.get("HPD_GUEST_AGENT_BUILDKIT_SOCKET", "/run/user/1000/buildkit-default/buildkitd.sock")
        return os.environ.get("HPD_GUEST_AGENT_ENGINE_SOCKET", "/var/run/docker.sock")

    def authority_projection_observation(self, source_socket, target_socket):
        conditions = []
        evidence = []
        if not os.path.exists(source_socket):
            conditions.append(self.condition(
                "AppleVirtualization.GuestAgentAuthoritySourceMissing",
                "SourceMissing",
                "Guest authority source socket is missing.",
                severity=3,
            ))
            evidence.append(self.socket_evidence(3, target_socket, not os.path.lexists(target_socket), "Guest authority source socket is missing."))
            return {"phase": 3, "revocation_status": 1, "conditions": conditions, "evidence": evidence}

        if not os.path.lexists(target_socket):
            conditions.append(self.condition(
                "AppleVirtualization.GuestAgentAuthorityTargetMissing",
                "TargetMissing",
                "Projected authority socket path is missing.",
                severity=3,
            ))
            evidence.append(self.socket_evidence(3, target_socket, True, "Projected authority socket path is absent."))
            return {"phase": 3, "revocation_status": 2, "conditions": conditions, "evidence": evidence}

        if not os.path.islink(target_socket):
            conditions.append(self.condition(
                "AppleVirtualization.GuestAgentAuthorityTargetUnmanaged",
                "TargetUnmanaged",
                "Projected authority socket path exists but is not a guest-agent managed symlink.",
                severity=4,
            ))
            evidence.append(self.socket_evidence(6, target_socket, True, "Projected authority socket path exists but is unmanaged."))
            return {"phase": 3, "revocation_status": 3, "conditions": conditions, "evidence": evidence}

        try:
            actual_target = os.readlink(target_socket)
        except OSError as exc:
            conditions.append(self.condition(
                "AppleVirtualization.GuestAgentAuthorityTargetUnreadable",
                "TargetUnreadable",
                "Projected authority socket symlink could not be read: " + str(exc),
                severity=4,
            ))
            evidence.append(self.socket_evidence(6, target_socket, True, "Projected authority socket symlink could not be read."))
            return {"phase": 3, "revocation_status": 3, "conditions": conditions, "evidence": evidence}

        if actual_target != source_socket:
            conditions.append(self.condition(
                "AppleVirtualization.GuestAgentAuthorityWrongTarget",
                "WrongTarget",
                "Projected authority socket points at a different guest source.",
                severity=4,
            ))
            evidence.append(self.socket_evidence(6, target_socket, True, "Projected authority socket points at an unexpected source."))
            return {"phase": 3, "revocation_status": 3, "conditions": conditions, "evidence": evidence}

        conditions.append(self.condition(
            "AppleVirtualization.GuestAgentAuthorityProjected",
            "Projected",
            "Authority socket projection is present and managed by the guest agent.",
        ))
        evidence.append(self.socket_evidence(6, target_socket, True, "Projected authority socket is present."))
        return {"phase": 2, "revocation_status": 1, "conditions": conditions, "evidence": evidence}

    def socket_evidence(self, kind, target_socket, observed, detail):
        return {
            "EvidenceProtocolVersion": "v1",
            "Kind": kind,
            "Observed": observed,
            "GuestSocketPath": {"Value": target_socket},
            "Detail": detail,
            "ObservedAt": self.timestamp(),
        }

    def condition(self, condition_type, reason, message, severity=2):
        return {
            "Type": condition_type,
            "Status": 2,
            "Reason": reason,
            "Message": message,
            "LastTransitionAt": self.timestamp(),
            "ObservedGeneration": {"Value": self.guest_agent_generation},
            "Severity": severity,
        }

    def socket_path(self, value):
        if isinstance(value, dict):
            value = value.get("Value")
        if isinstance(value, str):
            return value
        return None

    def int_value(self, value, default):
        if isinstance(value, bool):
            return int(value)
        if isinstance(value, int):
            return value
        if isinstance(value, str):
            try:
                return int(value)
            except ValueError:
                return default
        return default


def read_frame(fileobj):
    line = fileobj.readline(MAX_FRAME_BYTES + 1)
    if not line:
        return None
    if len(line) > MAX_FRAME_BYTES:
        raise ValueError("frame exceeds maximum size")
    return json.loads(line.decode("utf-8"))


def write_frame(fileobj, payload):
    encoded = json.dumps(payload, separators=(",", ":"), sort_keys=True).encode("utf-8") + b"\n"
    fileobj.write(encoded)
    fileobj.flush()


def serve_stream(agent, reader, writer):
    while True:
        request = read_frame(reader)
        if request is None:
            return
        write_frame(writer, agent.handle(request))


def serve_stdio(agent):
    serve_stream(agent, sys.stdin.buffer, sys.stdout.buffer)


def serve_vsock(agent, port):
    listener = socket.socket(AF_VSOCK, socket.SOCK_STREAM)
    listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    listener.bind((VMADDR_CID_ANY, port))
    listener.listen(16)

    selector = selectors.DefaultSelector()
    selector.register(listener, selectors.EVENT_READ)
    while True:
        for key, _ in selector.select(timeout=1.0):
            if key.fileobj is listener:
                connection, _ = listener.accept()
                with connection:
                    reader = connection.makefile("rb", buffering=0)
                    writer = connection.makefile("wb", buffering=0)
                    serve_stream(agent, reader, writer)


def main():
    parser = argparse.ArgumentParser(description="HPD Apple Virtualization guest agent")
    parser.add_argument("--port", type=int, default=int(os.environ.get("HPD_GUEST_AGENT_VSOCK_PORT", DEFAULT_PORT)))
    parser.add_argument("--agent-version", default=os.environ.get("HPD_GUEST_AGENT_VERSION", DEFAULT_AGENT_VERSION))
    parser.add_argument("--protocol-version", default=os.environ.get("HPD_GUEST_AGENT_PROTOCOL_VERSION", DEFAULT_PROTOCOL_VERSION))
    parser.add_argument("--stdio", action="store_true", help="serve stdin/stdout for local protocol tests")
    args = parser.parse_args()

    agent = GuestAgent(args.agent_version, args.protocol_version)
    if args.stdio:
        serve_stdio(agent)
    else:
        serve_vsock(agent, args.port)


if __name__ == "__main__":
    main()
