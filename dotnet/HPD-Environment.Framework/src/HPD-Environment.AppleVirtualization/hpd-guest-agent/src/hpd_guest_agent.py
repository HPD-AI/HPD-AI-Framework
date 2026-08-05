#!/usr/bin/env python3
import argparse
import base64
import datetime
import fcntl
import hashlib
import json
import os
import signal
import shutil
import socket
import stat
import subprocess
import struct
import sys
import threading
import time
import uuid

DEFAULT_PROTOCOL_VERSION = "1.0"
DEFAULT_AGENT_VERSION = "0.1.0"
DEFAULT_PORT = 7777
MAX_FRAME_BYTES = 1048576
FS_IOC_FSGETXATTR = 0x801C581F
FS_XFLAG_PROJINHERIT = 0x00000200

AF_VSOCK = getattr(socket, "AF_VSOCK", 40)
VMADDR_CID_ANY = getattr(socket, "VMADDR_CID_ANY", 0xFFFFFFFF)


def capabilities():
    return {
        "HostShutdown": True,
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
    def __init__(self, agent_version, protocol_version, guest_boot_id=None,
                 host_shutdown_executor=None):
        self.agent_version = agent_version
        self.protocol_version = protocol_version
        self.guest_boot_id = guest_boot_id or self._default_boot_id()
        self.state_root = os.environ.get("HPD_GUEST_AGENT_STATE_DIR", "/run/hpd")
        self.guest_boot_generation = self._boot_generation(self.guest_boot_id)
        self.guest_agent_generation, self.engine_generations = self._load_generation_state()
        self.processes = {}
        self.processes_lock = threading.Lock()
        self.host_shutdown_executor = (
            host_shutdown_executor or self._request_poweroff)

    def get_process(self, process_id):
        with self.processes_lock:
            return self.processes.get(process_id)

    def add_process(self, process_id, state):
        with self.processes_lock:
            if process_id in self.processes:
                return False
            self.processes[process_id] = state
            return True

    def shutdown(self):
        with self.processes_lock:
            states = list(self.processes.values())
        for state in states:
            popen = state.get("popen")
            if popen is None:
                continue
            try:
                if popen.poll() is None:
                    popen.terminate()
            except Exception:
                pass

    def _default_boot_id(self):
        try:
            with open("/proc/sys/kernel/random/boot_id", "r", encoding="utf-8") as boot_id:
                value = boot_id.read().strip()
                if value:
                    return value
        except OSError:
            pass
        return "guest-boot-" + str(uuid.uuid4())

    @staticmethod
    def _boot_generation(boot_id):
        digest = hashlib.sha256(boot_id.encode("utf-8")).digest()
        return int.from_bytes(digest[:8], "big") or 1

    def _load_generation_state(self):
        state = {}
        path = os.path.join(self.state_root, "generation-state.json")
        try:
            with open(path, "r", encoding="utf-8") as stream:
                loaded = json.load(stream)
                if isinstance(loaded, dict) and loaded.get("GuestBootId") == self.guest_boot_id:
                    state = loaded
        except (OSError, ValueError, TypeError):
            pass

        agent_generation = max(0, self.int_value(state.get("GuestAgentGeneration"), 0)) + 1
        engines = state.get("Engines") if isinstance(state.get("Engines"), dict) else {}
        self._save_generation_state(agent_generation, engines)
        return agent_generation, engines

    def _save_generation_state(self, agent_generation=None, engines=None):
        payload = {
            "GuestBootId": self.guest_boot_id,
            "GuestBootGeneration": self.guest_boot_generation,
            "GuestAgentGeneration": agent_generation if agent_generation is not None else self.guest_agent_generation,
            "Engines": engines if engines is not None else self.engine_generations,
        }
        try:
            os.makedirs(self.state_root, mode=0o700, exist_ok=True)
            path = os.path.join(self.state_root, "generation-state.json")
            temporary = path + ".tmp"
            with open(temporary, "w", encoding="utf-8") as stream:
                json.dump(payload, stream, sort_keys=True, separators=(",", ":"))
            os.replace(temporary, path)
        except OSError:
            pass

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
            "ProviderGeneration": request.get("ProviderGeneration"),
            "HostStartGeneration": request.get("HostStartGeneration"),
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
            clock_request = request.get("ClockReconciliationRequest") or {}
            host_utc_ms = clock_request.get("HostUtcUnixMilliseconds")
            if host_utc_ms is not None:
                try:
                    host_utc_ms = int(host_utc_ms)
                    maximum_skew_ms = max(
                        0,
                        int(clock_request.get(
                            "MaximumClockSkewMilliseconds",
                            5000)),
                    )
                    observed_ms = int(time.time() * 1000)
                    corrected = False
                    if abs(observed_ms - host_utc_ms) > maximum_skew_ms:
                        if not bool(clock_request.get("CorrectGuestClock")):
                            return self.error(
                                request,
                                2,
                                "Environment.Lifecycle.GuestClockSkewExceeded",
                                "Guest realtime clock exceeds the accepted host-wake skew bound.",
                                retryable=True,
                            )
                        time.clock_settime(
                            time.CLOCK_REALTIME,
                            host_utc_ms / 1000.0,
                        )
                        corrected = True
                        observed_ms = int(time.time() * 1000)
                    if abs(observed_ms - host_utc_ms) > maximum_skew_ms:
                        return self.error(
                            request,
                            2,
                            "Environment.Lifecycle.GuestClockCorrectionFailed",
                            "Guest realtime clock could not be verified after correction.",
                            retryable=True,
                        )
                except (AttributeError, OSError, TypeError, ValueError) as exc:
                    return self.error(
                        request,
                        2,
                        "Environment.Lifecycle.GuestClockCorrectionFailed",
                        "Guest realtime clock reconciliation failed: " + str(exc),
                        retryable=True,
                    )
            try:
                runtime_filesystem_uuid = self.verified_storage_identity(
                    "runtime")
                app_data_filesystem_uuid = self.verified_storage_identity(
                    "app-data")
                storage_ready = True
            except OSError:
                runtime_filesystem_uuid = None
                app_data_filesystem_uuid = None
                storage_ready = False
            payload = self.response_base(request, 2)
            payload["Ready"] = {
                "IsReady": storage_ready,
                "GuestBootId": self.guest_boot_id,
                "GuestBootGeneration": self.guest_boot_generation,
                "GuestAgentGeneration": self.guest_agent_generation,
                "RuntimeFilesystemUuid": runtime_filesystem_uuid,
                "AppDataFilesystemUuid": app_data_filesystem_uuid,
                "Conditions": [],
                "Diagnostics": [],
            }
            if host_utc_ms is not None:
                payload["Ready"]["ClockReconciliation"] = {
                    "HostUtcUnixMilliseconds": host_utc_ms,
                    "ObservedGuestUtcUnixMilliseconds": observed_ms,
                    "MaximumClockSkewMilliseconds": maximum_skew_ms,
                    "Corrected": corrected,
                    "Verified": True,
                }
            return payload

        if operation == 22:
            return self.process_start(request)

        if operation == 23:
            return self.process_status(request)

        if operation == 24:
            return self.process_stdin(request)

        if operation == 25:
            return self.process_signal(request)

        if operation == 26:
            return self.process_stop(request)

        if operation == 27:
            return self.process_wait(request)

        if operation == 28:
            return self.process_read_output(request)

        if operation == 29:
            return self.network_status(request)

        if operation in (44, 45, 46):
            return self.authority_binding(request, operation)

        if operation == 47:
            return self.engine_status(request)

        if operation == 50:
            return self.storage(request)

        if operation == 52:
            return self.host_shutdown(request)

        return self.error(request, operation if isinstance(operation, int) else 0, "AppleVirtualization.GuestAgentUnsupportedOperation", "Unsupported guest-agent operation.", retryable=False)

    def host_shutdown(self, request):
        shutdown_request = request.get("HostShutdownRequest")
        if not isinstance(shutdown_request, dict):
            return self.error(
                request,
                52,
                "AppleVirtualization.GuestAgentHostShutdownRequestMissing",
                "HostShutdownRequest is required.",
                retryable=False,
            )

        host_id = shutdown_request.get("HostId")
        provider_generation = self.int_value(
            shutdown_request.get("ProviderGeneration"), 0)
        host_start_generation = self.int_value(
            shutdown_request.get("HostStartGeneration"), 0)
        if (not isinstance(host_id, str) or not host_id.strip() or
                host_id != request.get("HostId") or
                provider_generation <= 0 or
                provider_generation != self.int_value(
                    request.get("ProviderGeneration"), 0) or
                host_start_generation <= 0 or
                host_start_generation != self.int_value(
                    request.get("HostStartGeneration"), 0)):
            return self.error(
                request,
                52,
                "AppleVirtualization.GuestAgentHostShutdownIdentityInvalid",
                "Host shutdown requires matching host and positive provider generations.",
                retryable=False,
            )

        payload = self.response_base(request, 52)
        payload["ResponseStatus"] = 1
        payload["HostShutdownResponse"] = {
            "Accepted": True,
            "HostId": host_id,
            "ProviderGeneration": provider_generation,
            "HostStartGeneration": host_start_generation,
        }
        return payload

    @staticmethod
    def _request_poweroff():
        GuestAgent._write_shutdown_diagnostic(
            "HPDOS_GUEST_SHUTDOWN: root-cgroup OpenRC shutdown requested")
        try:
            os.sync()
            return subprocess.Popen(
                [
                    "/bin/sh",
                    "-c",
                    "printf '%s\\n' \"$$\" > /sys/fs/cgroup/cgroup.procs; "
                    "exec /sbin/openrc shutdown",
                ],
                stdin=subprocess.DEVNULL,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                close_fds=True,
                start_new_session=True,
            )
        except Exception as exc:
            GuestAgent._write_shutdown_diagnostic(
                "HPDOS_GUEST_SHUTDOWN_FAILED: " + repr(exc))
            raise

    @staticmethod
    def _write_shutdown_diagnostic(message):
        encoded = (message + "\n").encode("utf-8", errors="replace")
        try:
            with open("/dev/hvc0", "ab", buffering=0) as console:
                console.write(encoded)
        except OSError:
            print(message, flush=True)

    def storage(self, request):
        storage_request = request.get("StorageRequest") or {}
        host_id = storage_request.get("HostId") or request.get("HostId")
        provider_generation = self.int_value(
            storage_request.get(
                "ProviderGeneration",
                request.get("ProviderGeneration"),
            ),
            0,
        )
        host_start_generation = self.int_value(
            storage_request.get("HostStartGeneration"),
            0,
        )
        action = self.int_value(storage_request.get("Action"), -1)
        storage_class = storage_request.get("StorageClass")
        logical_id = storage_request.get("LogicalVolumeId")
        owner_scope_id = storage_request.get("OwnerScopeId")
        owner_resource_id = storage_request.get("OwnerResourceId")
        declaration_id = storage_request.get("DeclarationId")
        compatibility_domain = storage_request.get(
            "CompatibilityDomain")
        volume_generation = self.int_value(
            storage_request.get("VolumeGeneration"),
            0,
        )
        maximum_bytes = self.int_value(
            self.case_dict(storage_request.get("MaximumBytes")).get("Value"),
            0,
        )
        if action == 0 and storage_class == "runtime-disposable":
            root = os.environ.get(
                "HPD_GUEST_RUNTIME_ROOT",
                "/var/lib/hpdos/runtime",
            )
        else:
            root = os.environ.get(
                "HPD_GUEST_APP_DATA_ROOT",
                "/var/lib/hpdos/app-data",
            )
        volumes_root = os.path.join(root, "volumes")
        quota_mode = os.environ.get(
            "HPD_GUEST_STORAGE_QUOTA_MODE",
            "ext4-project",
        )
        filesystem_mode = (
            "ext4"
            if (action == 0 and
                storage_class == "runtime-disposable" and
                quota_mode != "directory-test")
            else quota_mode
        )

        if not host_id or provider_generation <= 0:
            return self.error(
                request,
                50,
                "AppleVirtualization.StorageGenerationMissing",
                "Storage operations require a host identity and positive provider generation.",
                retryable=False,
            )
        if action not in range(0, 12):
            return self.error(
                request,
                50,
                "AppleVirtualization.StorageActionInvalid",
                "Storage action is invalid.",
                retryable=False,
            )
        if action == 0 and storage_class not in (
                "app-durable", "runtime-disposable"):
            return self.error(
                request,
                50,
                "AppleVirtualization.StorageClassInvalid",
                "Pool measurement requires app-durable or runtime-disposable storage class.",
                retryable=False,
            )
        if action != 0 and not self.safe_storage_component(logical_id):
            return self.error(
                request,
                50,
                "AppleVirtualization.StorageIdentityInvalid",
                "LogicalVolumeId must be one bounded safe path component.",
                retryable=False,
            )
        if action != 0 and maximum_bytes <= 0:
            return self.error(
                request,
                50,
                "AppleVirtualization.StorageQuotaInvalid",
                "Durable-volume operations require a positive MaximumBytes hard quota.",
                retryable=False,
            )
        operation_id = storage_request.get("OperationId")
        if action >= 5 and not self.safe_storage_component(operation_id):
            return self.error(
                request,
                50,
                "AppleVirtualization.StorageOperationIdentityInvalid",
                "Backup and restore operations require one bounded safe operation identity.",
                retryable=False,
            )
        ownership = {
            "OwnerScopeId": owner_scope_id,
            "OwnerResourceId": owner_resource_id,
            "DeclarationId": declaration_id,
            "CompatibilityDomain": compatibility_domain,
            "VolumeGeneration": volume_generation,
            "ProviderGeneration": provider_generation,
        }
        if action != 0 and (
                any(not self.safe_storage_identity(value)
                    for key, value in ownership.items()
                    if key not in ("VolumeGeneration",
                                   "ProviderGeneration")) or
                volume_generation <= 0):
            return self.error(
                request,
                50,
                "AppleVirtualization.StorageOwnershipInvalid",
                "Durable-volume operations require complete bounded ownership and positive volume/provider generations.",
                retryable=False,
            )
        if quota_mode not in ("ext4-project", "directory-test"):
            return self.error(
                request,
                50,
                "AppleVirtualization.StorageQuotaModeInvalid",
                "The configured durable-volume quota mode is unsupported.",
                retryable=False,
            )

        try:
            if os.path.lexists(root) and os.path.islink(root):
                raise OSError("storage root must not be a symbolic link")
            if (os.path.lexists(volumes_root)
                    and os.path.islink(volumes_root)):
                raise OSError(
                    "storage volumes root must not be a symbolic link")
            if action != 0:
                os.makedirs(volumes_root, mode=0o700, exist_ok=True)
            if os.path.islink(root) or (
                    action != 0 and os.path.islink(volumes_root)):
                raise OSError("storage roots must not be symbolic links")
            path = (
                None
                if action == 0
                else os.path.join(volumes_root, logical_id)
            )
            if (path is not None and os.path.lexists(path)
                    and os.path.islink(path)):
                raise OSError(
                    "durable volume must not be a symbolic link")
            if action == 2:
                ownership = self.recover_verified_restore_ownership(
                    root,
                    volumes_root,
                    logical_id,
                    maximum_bytes,
                    ownership,
                )
            if action >= 5:
                return self.storage_transfer(
                    request,
                    storage_request,
                    action,
                    root,
                    volumes_root,
                    path,
                    logical_id,
                    maximum_bytes,
                    quota_mode,
                    ownership,
                    operation_id,
                )
            if action == 1:
                os.makedirs(path, mode=0o700, exist_ok=True)
                if os.path.islink(path):
                    raise OSError("durable volume must not be a symbolic link")
                project_id = self.ensure_volume_quota(
                    root,
                    volumes_root,
                    logical_id,
                    path,
                    maximum_bytes,
                    quota_mode,
                    ownership,
                )
            elif action in (2, 3):
                project_id = self.verify_volume_quota(
                    root,
                    volumes_root,
                    logical_id,
                    path,
                    maximum_bytes,
                    quota_mode,
                    ownership,
                )
            elif action == 4 and os.path.lexists(path):
                if os.path.islink(path):
                    raise OSError("durable volume must not be a symbolic link")
                project_id = self.verify_volume_quota(
                    root,
                    volumes_root,
                    logical_id,
                    path,
                    maximum_bytes,
                    quota_mode,
                    ownership,
                )
                tombstone = os.path.join(
                    volumes_root,
                    ".erase-" + str(uuid.uuid4()),
                )
                os.replace(path, tombstone)
                self.fsync_directory(volumes_root)
                shutil.rmtree(tombstone)
                self.fsync_directory(volumes_root)
                self.remove_volume_quota(
                    root,
                    volumes_root,
                    logical_id,
                    project_id,
                    quota_mode,
                )
            else:
                project_id = None

            exists = path is not None and os.path.isdir(path)
            logical_bytes, allocated_bytes = (
                self.measure_storage_tree(path) if exists else (0, 0)
            )
            if (exists and maximum_bytes > 0 and
                    logical_bytes > maximum_bytes):
                raise OSError(
                    "durable-volume content exceeds its hard quota")
            filesystem = os.statvfs(root)
            filesystem_identity = self.storage_filesystem_identity(
                root,
                filesystem_mode,
            )
            payload = self.response_base(request, 50)
            payload["StorageResponse"] = {
                "HostId": host_id,
                "ProviderGeneration": provider_generation,
                "HostStartGeneration": host_start_generation,
                "Action": action,
                "LogicalVolumeId": logical_id,
                "Exists": exists,
                "Attached": action == 1 and exists,
                "EffectiveRuntimePath": path if exists else None,
                "FilesystemIdentity": (
                    filesystem_identity
                    if project_id is None
                    else filesystem_identity + ":project:" + str(project_id)
                ),
                "VolumeGeneration": ownership.get("VolumeGeneration"),
                "LogicalCapacityBytes": {
                    "Value": filesystem.f_blocks * filesystem.f_frsize
                },
                "PhysicalAllocatedBytes": {
                    "Value": allocated_bytes
                },
                "UsedBytes": {"Value": logical_bytes},
                "AvailableBytes": {
                    "Value": filesystem.f_bavail * filesystem.f_frsize
                },
                "MeasurementConfidence": 1,
                "Conditions": [],
                "Diagnostics": [],
            }
            return payload
        except OSError as error:
            return self.error(
                request,
                50,
                "AppleVirtualization.StorageOperationFailed",
                "Guest App-data storage operation failed: " + str(error),
                retryable=False,
            )

    def storage_transfer(
            self,
            request,
            storage_request,
            action,
            root,
            volumes_root,
            volume_path,
            logical_id,
            maximum_bytes,
            quota_mode,
            ownership,
            operation_id):
        operations_root = os.path.join(
            os.environ.get(
                "HPD_GUEST_OPERATION_TEMP_ROOT",
                "/var/lib/hpdos/runtime/operation-temporary"),
            "storage")
        if os.path.lexists(operations_root) and os.path.islink(operations_root):
            raise OSError("storage operation root must not be a symbolic link")
        os.makedirs(operations_root, mode=0o700, exist_ok=True)
        operation_names = os.listdir(operations_root)
        if len(operation_names) > 1024:
            raise OSError("storage operation count exceeds its bound")
        for name in operation_names:
            entry = os.path.join(operations_root, name)
            if (not self.safe_storage_component(name) or
                    os.path.islink(entry) or
                    not os.path.isdir(entry)):
                raise OSError(
                    "storage operation root contains an invalid identity")
        operation_root = os.path.join(operations_root, operation_id)
        restore_journal_root = os.path.join(
            volumes_root, ".hpd-restore-operations")
        if (os.path.lexists(restore_journal_root) and
                os.path.islink(restore_journal_root)):
            raise OSError("restore journal root must not be a symbolic link")
        os.makedirs(restore_journal_root, mode=0o700, exist_ok=True)
        state_path = (
            os.path.join(operation_root, "state.json")
            if action <= 7
            else os.path.join(restore_journal_root, operation_id + ".json"))
        payload_path = os.path.join(operation_root, "payload.bin")
        identity = {
            "OperationId": operation_id,
            "LogicalVolumeId": logical_id,
            **ownership,
        }

        if action == 5:
            self.verify_volume_quota(
                root, volumes_root, logical_id, volume_path,
                maximum_bytes, quota_mode, ownership)
            if os.path.isdir(operation_root):
                state = self.load_storage_operation(state_path)
                self.require_storage_operation(state, "backup", identity)
            else:
                os.mkdir(operation_root, mode=0o700)
                try:
                    evidence = self.capture_storage_payload(
                        volume_path, payload_path, maximum_bytes)
                    state = {
                        "Schema": "hpd.guest.storage-operation/v1",
                        "Kind": "backup",
                        "Checkpoint": "ready",
                        **identity,
                        **evidence,
                    }
                    self.save_storage_operation(state_path, state)
                    self.fsync_directory(operation_root)
                    self.fsync_directory(operations_root)
                except BaseException:
                    shutil.rmtree(operation_root, ignore_errors=True)
                    raise
            return self.storage_transfer_response(
                request, storage_request, action, logical_id,
                operation_id, state, completed=True)

        if action == 6:
            state = self.load_storage_operation(state_path)
            self.require_storage_operation(state, "backup", identity)
            offset = self.int_value(storage_request.get("Offset"), -1)
            maximum_chunk = self.int_value(
                storage_request.get("MaximumChunkBytes"), 0)
            if (offset < 0 or maximum_chunk <= 0 or
                    maximum_chunk > 43008 or
                    offset >= state["EncodedPayloadBytes"]):
                raise OSError("backup chunk offset or size is invalid")
            with open(payload_path, "rb") as stream:
                stream.seek(offset)
                chunk = stream.read(maximum_chunk)
            if not chunk:
                raise OSError("backup chunk content is missing")
            return self.storage_transfer_response(
                request, storage_request, action, logical_id,
                operation_id, state, offset=offset,
                chunk=chunk,
                completed=(offset + len(chunk) ==
                           state["EncodedPayloadBytes"]))

        if action == 7:
            if not os.path.isdir(operation_root):
                return self.storage_transfer_response(
                    request,
                    storage_request,
                    action,
                    logical_id,
                    operation_id,
                    {
                        "Checkpoint": "cleaned",
                        **identity,
                    },
                    completed=True)
            state = self.load_storage_operation(state_path)
            self.require_storage_operation(state, "backup", identity)
            shutil.rmtree(operation_root)
            self.fsync_directory(operations_root)
            return self.storage_transfer_response(
                request, storage_request, action, logical_id,
                operation_id, state, completed=True)

        expected_digest = storage_request.get("ExpectedContentSha256")
        expected_logical = self.int_value(
            storage_request.get("ExpectedLogicalBytes"), -1)
        if action == 8:
            if (not self.valid_sha256(expected_digest) or
                    expected_logical < 0 or
                    expected_logical > maximum_bytes):
                raise OSError("restore content evidence is invalid")
            self.verify_volume_quota(
                root, volumes_root, logical_id, volume_path,
                maximum_bytes, quota_mode, ownership)
            if os.path.exists(state_path):
                state = self.load_storage_operation(state_path)
                self.require_storage_operation(state, "restore", identity)
                if (state.get("ExpectedContentSha256") != expected_digest or
                        state.get("ExpectedLogicalBytes") != expected_logical):
                    raise OSError("restore operation evidence conflicts")
            else:
                os.mkdir(operation_root, mode=0o700)
                descriptor = os.open(
                    payload_path,
                    os.O_WRONLY | os.O_CREAT | os.O_EXCL,
                    0o600)
                os.close(descriptor)
                state = {
                    "Schema": "hpd.guest.storage-operation/v1",
                    "Kind": "restore",
                    "Checkpoint": "receiving",
                    **identity,
                    "ExpectedContentSha256": expected_digest,
                    "ExpectedLogicalBytes": expected_logical,
                    "ReceivedBytes": 0,
                }
                self.save_storage_operation(state_path, state)
                self.fsync_directory(operation_root)
                self.fsync_directory(operations_root)
                self.fsync_directory(restore_journal_root)
            return self.storage_transfer_response(
                request, storage_request, action, logical_id,
                operation_id, state)

        state = self.load_storage_operation(state_path)
        self.require_storage_operation(state, "restore", identity)
        if action == 9:
            if state.get("Checkpoint") != "receiving":
                raise OSError("restore no longer accepts payload chunks")
            offset = self.int_value(storage_request.get("Offset"), -1)
            if offset != state.get("ReceivedBytes"):
                raise OSError("restore chunk offset is not sequential")
            chunk_text = storage_request.get("ChunkBase64")
            try:
                chunk = base64.b64decode(
                    chunk_text,
                    validate=True)
            except (ValueError, TypeError) as error:
                raise OSError("restore chunk is not canonical Base64") from error
            if (not chunk or len(chunk) > 43008 or
                    base64.b64encode(chunk).decode("ascii") != chunk_text):
                raise OSError("restore chunk violates its byte bound")
            with open(payload_path, "r+b", buffering=0) as stream:
                stream.seek(offset)
                stream.write(chunk)
                stream.flush()
                os.fsync(stream.fileno())
            state["ReceivedBytes"] = offset + len(chunk)
            self.save_storage_operation(state_path, state)
            return self.storage_transfer_response(
                request, storage_request, action, logical_id,
                operation_id, state, offset=offset,
                completed=False)

        if action == 10:
            state = self.commit_storage_restore(
                storage_request,
                state,
                state_path,
                payload_path,
                operation_root,
                volumes_root,
                volume_path,
                logical_id,
                maximum_bytes,
                quota_mode,
                ownership)
            return self.storage_transfer_response(
                request, storage_request, action, logical_id,
                operation_id, state, completed=True)

        if action == 11:
            checkpoint = state.get("Checkpoint")
            staging = os.path.join(volumes_root, ".restore-" + operation_id)
            previous = os.path.join(volumes_root, ".previous-" + operation_id)
            if checkpoint in ("receiving", "staged"):
                if os.path.isdir(staging):
                    shutil.rmtree(staging)
                shutil.rmtree(operation_root, ignore_errors=True)
                os.unlink(state_path)
            elif checkpoint == "previous-moved":
                if os.path.isdir(staging):
                    shutil.rmtree(staging)
                if not os.path.isdir(volume_path) and os.path.isdir(previous):
                    os.replace(previous, volume_path)
                shutil.rmtree(operation_root, ignore_errors=True)
                os.unlink(state_path)
            elif checkpoint == "verified":
                if os.path.isdir(previous):
                    shutil.rmtree(previous)
                shutil.rmtree(operation_root, ignore_errors=True)
                os.unlink(state_path)
            elif checkpoint == "selected":
                raise OSError(
                    "selected restore remains retained until recovery verifies its outcome")
            else:
                raise OSError("restore checkpoint is invalid")
            self.fsync_directory(volumes_root)
            self.fsync_directory(operations_root)
            self.fsync_directory(restore_journal_root)
            return self.storage_transfer_response(
                request, storage_request, action, logical_id,
                operation_id, state, completed=True)
        raise OSError("storage transfer action is invalid")

    def recover_verified_restore_ownership(
            self,
            root,
            volumes_root,
            logical_id,
            maximum_bytes,
            requested):
        quota_state = self.load_volume_quota_state(volumes_root)
        entry = quota_state.get(logical_id)
        project_id = self.volume_project_id(logical_id)
        if self.volume_quota_entry_matches(
                entry, project_id, maximum_bytes, requested):
            return requested
        if not isinstance(entry, dict):
            return requested
        candidate = dict(requested)
        candidate["VolumeGeneration"] = (
            requested["VolumeGeneration"] + 1)
        if not self.volume_quota_entry_matches(
                entry, project_id, maximum_bytes, candidate):
            return requested
        journal_root = os.path.join(
            volumes_root, ".hpd-restore-operations")
        if not os.path.isdir(journal_root) or os.path.islink(journal_root):
            return requested
        matched = 0
        names = os.listdir(journal_root)
        if len(names) > 1024:
            raise OSError("restore journal count exceeds its bound")
        for name in names:
            if (not name.endswith(".json") or
                    not self.safe_storage_component(name[:-5])):
                raise OSError("storage operation root contains an invalid identity")
            state = self.load_storage_operation(os.path.join(journal_root, name))
            if (state.get("Kind") == "restore" and
                    state.get("Checkpoint") == "verified" and
                    state.get("LogicalVolumeId") == logical_id and
                    state.get("VolumeGeneration") ==
                    requested["VolumeGeneration"] and
                    state.get("RestoredVolumeGeneration") ==
                    candidate["VolumeGeneration"] and
                    all(state.get(key) == value
                        for key, value in requested.items()
                        if key != "VolumeGeneration")):
                matched += 1
        if matched != 1:
            raise OSError(
                "advanced durable-volume generation lacks one exact verified restore journal")
        return candidate

    def commit_storage_restore(
            self,
            storage_request,
            state,
            state_path,
            payload_path,
            operation_root,
            volumes_root,
            volume_path,
            logical_id,
            maximum_bytes,
            quota_mode,
            ownership):
        checkpoint = state.get("Checkpoint")
        expected_encoded = self.int_value(
            storage_request.get("ExpectedEncodedPayloadBytes"), -1)
        expected_entries = self.int_value(
            storage_request.get("ExpectedEntryCount"), -1)
        if checkpoint == "receiving":
            if expected_encoded < 0:
                expected_encoded = state.get("ReceivedBytes", -1)
            if expected_entries < 0:
                raise OSError("restore entry count is missing before selection")
            if (state.get("ReceivedBytes") != expected_encoded or
                    expected_entries > 1000000):
                raise OSError("restore payload length or entry count is invalid")
            staging = os.path.join(
                volumes_root,
                ".restore-" + state["OperationId"])
            if os.path.lexists(staging):
                raise OSError("restore staging identity already exists")
            os.mkdir(staging, mode=0o700)
            try:
                if quota_mode == "ext4-project":
                    self.storage_filesystem_identity(
                        os.path.dirname(volumes_root),
                        quota_mode)
                    self.assign_project_identity(
                        staging,
                        self.volume_project_id(logical_id))
                evidence = self.restore_storage_payload(
                    payload_path,
                    staging,
                    expected_entries,
                    maximum_bytes)
                if (evidence["ContentSha256"] !=
                        state["ExpectedContentSha256"] or
                        evidence["LogicalBytes"] !=
                        state["ExpectedLogicalBytes"]):
                    raise OSError("restore payload postconditions do not match")
                state.update(evidence)
                state["EncodedPayloadBytes"] = expected_encoded
                state["EntryCount"] = expected_entries
                state["Checkpoint"] = "staged"
                self.save_storage_operation(state_path, state)
            except BaseException:
                shutil.rmtree(staging, ignore_errors=True)
                raise
            checkpoint = "staged"
        staging = os.path.join(volumes_root, ".restore-" + state["OperationId"])
        previous = os.path.join(volumes_root, ".previous-" + state["OperationId"])
        if checkpoint == "staged":
            if os.path.lexists(previous):
                raise OSError("restore previous-generation identity already exists")
            os.replace(volume_path, previous)
            self.fsync_directory(volumes_root)
            state["Checkpoint"] = "previous-moved"
            self.save_storage_operation(state_path, state)
            checkpoint = "previous-moved"
        if checkpoint == "previous-moved":
            if not os.path.isdir(staging) or os.path.isdir(volume_path):
                raise OSError("restore selection preconditions are ambiguous")
            os.replace(staging, volume_path)
            self.fsync_directory(volumes_root)
            state["Checkpoint"] = "selected"
            self.save_storage_operation(state_path, state)
            checkpoint = "selected"
        if checkpoint == "selected":
            if quota_mode == "ext4-project":
                self.storage_filesystem_identity(
                    os.path.dirname(volumes_root),
                    quota_mode)
                self.verify_project_identity(
                    volume_path,
                    self.volume_project_id(logical_id))
            evidence = self.measure_canonical_storage_tree(
                volume_path, maximum_bytes)
            if (evidence["ContentSha256"] !=
                    state["ExpectedContentSha256"] or
                    evidence["LogicalBytes"] !=
                    state["ExpectedLogicalBytes"]):
                raise OSError("selected restore content cannot be verified")
            quota_state = self.load_volume_quota_state(volumes_root)
            entry = quota_state.get(logical_id)
            if not self.volume_quota_entry_matches(
                    entry,
                    self.volume_project_id(logical_id),
                    maximum_bytes,
                    ownership):
                raise OSError("restore target quota ownership changed")
            new_ownership = dict(ownership)
            new_ownership["VolumeGeneration"] = ownership["VolumeGeneration"] + 1
            quota_state[logical_id] = {
                "ProjectId": self.volume_project_id(logical_id),
                "MaximumBytes": maximum_bytes,
                **new_ownership,
            }
            self.save_volume_quota_state(volumes_root, quota_state)
            state["RestoredVolumeGeneration"] = new_ownership["VolumeGeneration"]
            state["Checkpoint"] = "verified"
            self.save_storage_operation(state_path, state)
        if state.get("Checkpoint") != "verified":
            raise OSError("restore did not reach its verified checkpoint")
        return state

    @staticmethod
    def storage_transfer_response(
            request,
            storage_request,
            action,
            logical_id,
            operation_id,
            state,
            offset=None,
            chunk=None,
            completed=False):
        payload = {
            "HostId": storage_request.get("HostId"),
            "ProviderGeneration": storage_request.get("ProviderGeneration"),
            "HostStartGeneration": storage_request.get("HostStartGeneration", 0),
            "Action": action,
            "LogicalVolumeId": logical_id,
            "OperationId": operation_id,
            "Offset": offset,
            "ChunkBase64": (
                base64.b64encode(chunk).decode("ascii")
                if chunk is not None else None),
            "Completed": completed,
            "EncodedPayloadBytes": state.get("EncodedPayloadBytes"),
            "LogicalBytes": state.get("LogicalBytes"),
            "EntryCount": state.get("EntryCount"),
            "ContentSha256": state.get("ContentSha256") or state.get("ExpectedContentSha256"),
            "VolumeGeneration": (
                state.get("RestoredVolumeGeneration") or
                state.get("VolumeGeneration")),
            "Exists": True,
            "Attached": False,
            "MeasurementConfidence": 1,
            "Conditions": [],
            "Diagnostics": [],
        }
        response = {
            "ProtocolVersion": request.get("ProtocolVersion"),
            "MessageType": 1,
            "Operation": 50,
            "RequestId": request.get("RequestId"),
            "CausationId": request.get("RequestId"),
            "SequenceNumber": int(request.get("SequenceNumber", 0)) + 1,
            "Timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "HostId": request.get("HostId"),
            "ProviderGeneration": request.get("ProviderGeneration"),
            "HostStartGeneration": request.get("HostStartGeneration"),
        }
        response["StorageResponse"] = payload
        return response

    @staticmethod
    def valid_sha256(value):
        return (isinstance(value, str) and len(value) == 64 and
                all(character in "0123456789abcdef" for character in value))

    @staticmethod
    def save_storage_operation(path, state):
        encoded = json.dumps(
            state, sort_keys=True, separators=(",", ":")).encode("utf-8")
        if len(encoded) > 65536:
            raise OSError("storage operation state exceeds its byte bound")
        temporary = path + ".tmp-" + str(uuid.uuid4())
        descriptor = os.open(
            temporary,
            os.O_WRONLY | os.O_CREAT | os.O_EXCL,
            0o600)
        try:
            with os.fdopen(descriptor, "wb") as stream:
                stream.write(encoded)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, path)
            GuestAgent.fsync_directory(os.path.dirname(path))
        except BaseException:
            try:
                os.unlink(temporary)
            except OSError:
                pass
            raise

    @staticmethod
    def load_storage_operation(path):
        if os.path.islink(path):
            raise OSError("storage operation state must not be a symbolic link")
        with open(path, "rb") as stream:
            content = stream.read(65537)
        if len(content) > 65536:
            raise OSError("storage operation state exceeds its byte bound")
        try:
            state = json.loads(content.decode("utf-8", errors="strict"))
        except (UnicodeDecodeError, ValueError, TypeError) as error:
            raise OSError("storage operation state is malformed") from error
        if (not isinstance(state, dict) or
                state.get("Schema") != "hpd.guest.storage-operation/v1"):
            raise OSError("storage operation state schema is invalid")
        return state

    @staticmethod
    def require_storage_operation(state, kind, identity):
        if state.get("Kind") != kind or any(
                state.get(key) != value for key, value in identity.items()):
            raise OSError("storage operation identity conflicts")

    def capture_storage_payload(self, root, destination, maximum_bytes):
        entries = self.canonical_storage_entries(root, maximum_bytes)
        digest = hashlib.sha256()
        logical = 0
        descriptor = os.open(
            destination,
            os.O_WRONLY | os.O_CREAT | os.O_EXCL,
            0o600)
        with os.fdopen(descriptor, "wb") as output:
            for kind, relative, full_path, length in entries:
                path_bytes = relative.encode("utf-8")
                digest.update(struct.pack("<BIQ", kind, len(path_bytes), length))
                digest.update(path_bytes)
                output.write(struct.pack("<BI", kind, len(path_bytes)))
                output.write(path_bytes)
                output.write(struct.pack("<Q", length))
                if kind == 2:
                    before = os.stat(full_path, follow_symlinks=False)
                    descriptor = os.open(
                        full_path,
                        os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
                    opened = os.fstat(descriptor)
                    if ((opened.st_dev, opened.st_ino, opened.st_size) !=
                            (before.st_dev, before.st_ino, before.st_size) or
                            opened.st_nlink != 1 or
                            not stat.S_ISREG(opened.st_mode)):
                        os.close(descriptor)
                        raise OSError(
                            "backup source identity changed before capture")
                    with os.fdopen(descriptor, "rb") as source:
                        remaining = length
                        while remaining:
                            chunk = source.read(min(65536, remaining))
                            if not chunk:
                                raise OSError("backup source changed during capture")
                            output.write(chunk)
                            digest.update(chunk)
                            logical += len(chunk)
                            remaining -= len(chunk)
                        if source.read(1):
                            raise OSError("backup source grew during capture")
                    after = os.stat(full_path, follow_symlinks=False)
                    if ((before.st_dev, before.st_ino, before.st_size,
                         before.st_mtime_ns) !=
                            (after.st_dev, after.st_ino, after.st_size,
                             after.st_mtime_ns)):
                        raise OSError("backup source changed during capture")
            output.flush()
            os.fsync(output.fileno())
        return {
            "EncodedPayloadBytes": os.path.getsize(destination),
            "LogicalBytes": logical,
            "EntryCount": len(entries),
            "ContentSha256": digest.hexdigest(),
        }

    def canonical_storage_entries(self, root, maximum_bytes):
        entries = []
        logical = 0
        for current, directories, files in os.walk(
                root, topdown=True, followlinks=False):
            directories.sort()
            files.sort()
            for name in directories:
                full = os.path.join(current, name)
                if os.path.islink(full):
                    raise OSError("symbolic links are not valid durable data")
                relative = os.path.relpath(full, root).replace(os.sep, "/")
                self.validate_storage_relative_path(relative)
                entries.append((1, relative, full, 0))
            for name in files:
                full = os.path.join(current, name)
                if os.path.islink(full):
                    raise OSError("symbolic links are not valid durable data")
                status = os.stat(full, follow_symlinks=False)
                if (not stat.S_ISREG(status.st_mode) or
                        status.st_nlink != 1):
                    raise OSError("unsupported durable data entry")
                relative = os.path.relpath(full, root).replace(os.sep, "/")
                self.validate_storage_relative_path(relative)
                logical += status.st_size
                if logical > maximum_bytes:
                    raise OSError("durable data exceeds its accepted maximum")
                entries.append((2, relative, full, status.st_size))
            if len(entries) > 1000000:
                raise OSError("durable data contains too many entries")
        entries.sort(key=lambda entry: entry[1].encode("utf-8"))
        return entries

    @staticmethod
    def validate_storage_relative_path(relative):
        encoded = relative.encode("utf-8", errors="strict")
        parts = relative.split("/")
        if (not encoded or len(encoded) > 1024 or relative.startswith("/") or
                any(part in ("", ".", "..") for part in parts) or
                "\\" in relative or "\x00" in relative):
            raise OSError("durable data path is invalid")

    def restore_storage_payload(
            self, payload_path, destination, entry_count, maximum_bytes):
        digest = hashlib.sha256()
        logical = 0
        previous = None
        with open(payload_path, "rb") as source:
            for _ in range(entry_count):
                header = source.read(5)
                if len(header) != 5:
                    raise OSError("restore payload ended before an entry header")
                kind, path_length = struct.unpack("<BI", header)
                if kind not in (1, 2) or path_length <= 0 or path_length > 1024:
                    raise OSError("restore entry header is invalid")
                path_bytes = source.read(path_length)
                length_bytes = source.read(8)
                if len(path_bytes) != path_length or len(length_bytes) != 8:
                    raise OSError("restore entry header is truncated")
                try:
                    relative = path_bytes.decode("utf-8", errors="strict")
                except UnicodeDecodeError as error:
                    raise OSError("restore path is not strict UTF-8") from error
                self.validate_storage_relative_path(relative)
                if previous is not None and previous >= path_bytes:
                    raise OSError("restore entries are not canonical and unique")
                previous = path_bytes
                length = struct.unpack("<Q", length_bytes)[0]
                if kind == 1 and length != 0:
                    raise OSError("restore directory length is invalid")
                digest.update(struct.pack("<BIQ", kind, path_length, length))
                digest.update(path_bytes)
                target = os.path.abspath(os.path.join(
                    destination, *relative.split("/")))
                if os.path.commonpath((destination, target)) != destination:
                    raise OSError("restore path escapes staging")
                if kind == 1:
                    os.makedirs(target, mode=0o700, exist_ok=False)
                    continue
                os.makedirs(os.path.dirname(target), mode=0o700, exist_ok=True)
                logical += length
                if logical > maximum_bytes:
                    raise OSError("restore content exceeds its accepted maximum")
                descriptor = os.open(
                    target,
                    os.O_WRONLY | os.O_CREAT | os.O_EXCL,
                    0o600)
                with os.fdopen(descriptor, "wb") as output:
                    remaining = length
                    while remaining:
                        chunk = source.read(min(65536, remaining))
                        if not chunk:
                            raise OSError("restore file content is truncated")
                        output.write(chunk)
                        digest.update(chunk)
                        remaining -= len(chunk)
                    output.flush()
                    os.fsync(output.fileno())
            if source.read(1):
                raise OSError("restore payload has trailing content")
        self.fsync_directory(destination)
        return {
            "LogicalBytes": logical,
            "EntryCount": entry_count,
            "ContentSha256": digest.hexdigest(),
        }

    def measure_canonical_storage_tree(self, root, maximum_bytes):
        entries = self.canonical_storage_entries(root, maximum_bytes)
        digest = hashlib.sha256()
        logical = 0
        for kind, relative, full_path, length in entries:
            path_bytes = relative.encode("utf-8")
            digest.update(struct.pack("<BIQ", kind, len(path_bytes), length))
            digest.update(path_bytes)
            if kind == 2:
                descriptor = os.open(
                    full_path,
                    os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
                opened = os.fstat(descriptor)
                if (not stat.S_ISREG(opened.st_mode) or
                        opened.st_nlink != 1 or
                        opened.st_size != length):
                    os.close(descriptor)
                    raise OSError(
                        "selected restore file identity is invalid")
                with os.fdopen(descriptor, "rb") as source:
                    remaining = length
                    while remaining:
                        chunk = source.read(min(65536, remaining))
                        if not chunk:
                            raise OSError("selected restore content is truncated")
                        digest.update(chunk)
                        logical += len(chunk)
                        remaining -= len(chunk)
                    if source.read(1):
                        raise OSError("selected restore content grew")
        return {
            "LogicalBytes": logical,
            "EntryCount": len(entries),
            "ContentSha256": digest.hexdigest(),
        }

    @staticmethod
    def safe_storage_component(value):
        if not isinstance(value, str) or not value or len(value) > 128:
            return False
        return all(
            character.isalnum() or character in ("-", "_", ".")
            for character in value
        ) and value not in (".", "..")

    @staticmethod
    def safe_storage_identity(value):
        if not isinstance(value, str) or not value or len(value) > 256:
            return False
        return all(
            0x21 <= ord(character) <= 0x7e
            for character in value
        )

    @staticmethod
    def volume_quota_entry_matches(
            entry,
            project_id,
            maximum_bytes,
            ownership):
        if not isinstance(entry, dict):
            return False
        expected = {
            "ProjectId": project_id,
            "MaximumBytes": maximum_bytes,
            **ownership,
        }
        return (
            set(entry.keys()) == set(expected.keys()) and
            all(entry.get(key) == value
                for key, value in expected.items())
        )

    @staticmethod
    def storage_filesystem_identity(root, quota_mode):
        test_identity = os.environ.get(
            "HPD_GUEST_APP_DATA_FILESYSTEM_ID",
        )
        if quota_mode == "directory-test":
            if not test_identity:
                return "guest-app-data:test:" + str(os.stat(root).st_dev)
            return test_identity

        source, filesystem_type, options = (
            GuestAgent.storage_mount_identity(root))
        if filesystem_type != "ext4":
            raise OSError(
                "App-data root is not one verified ext4 mount")
        if (quota_mode == "ext4-project" and
                "prjquota" not in options and "pquota" not in options):
            raise OSError(
                "App-data filesystem does not enforce project quotas; "
                "observed filesystem options: " +
                ",".join(sorted(options)))
        identity = subprocess.run(
            ["blkid", "-s", "UUID", "-o", "value", source],
            check=True,
            capture_output=True,
            text=True,
            timeout=3,
        ).stdout.strip().lower()
        if (not identity or len(identity) > 64 or
                any(character not in "0123456789abcdef-"
                    for character in identity)):
            raise OSError(
                "App-data filesystem UUID is missing or invalid")
        prefix = "guest-runtime:" if quota_mode == "ext4" else "guest-app-data:"
        return prefix + identity

    @staticmethod
    def storage_mount_identity(root, mountinfo=None):
        target = os.path.realpath(root)
        if mountinfo is None:
            with open(
                    "/proc/self/mountinfo",
                    "r",
                    encoding="utf-8") as stream:
                mountinfo = stream.read()
        matches = []
        for line in mountinfo.splitlines():
            fields = line.split()
            try:
                separator = fields.index("-")
            except ValueError:
                continue
            if len(fields) <= separator + 3 or len(fields) < 6:
                continue
            mount_point = GuestAgent.decode_mountinfo_path(fields[4])
            if os.path.realpath(mount_point) != target:
                continue
            matches.append((
                GuestAgent.decode_mountinfo_path(fields[separator + 2]),
                fields[separator + 1],
                set(fields[separator + 3].split(",")),
            ))
        if len(matches) != 1:
            raise OSError(
                "App-data root is not one verified filesystem mount")
        return matches[0]

    @staticmethod
    def decode_mountinfo_path(value):
        for encoded, decoded in (
                ("\\040", " "),
                ("\\011", "\t"),
                ("\\012", "\n"),
                ("\\134", "\\")):
            value = value.replace(encoded, decoded)
        return value

    def verified_storage_identity(self, role):
        persisted = self.persisted_storage_identity(role)
        if not persisted:
            raise OSError(
                "guest storage identity is not initialized")
        if role == "app-data":
            live = self.storage_filesystem_identity(
                os.environ.get(
                    "HPD_GUEST_APP_DATA_ROOT",
                    "/var/lib/hpdos/app-data",
                ),
                "ext4-project",
            ).removeprefix("guest-app-data:")
        elif role == "runtime":
            root = os.environ.get(
                "HPD_GUEST_RUNTIME_ROOT",
                "/var/lib/hpdos/runtime",
            )
            completed = subprocess.run(
                [
                    "findmnt",
                    "-n",
                    "-o",
                    "SOURCE,FSTYPE",
                    "--target",
                    root,
                ],
                check=True,
                capture_output=True,
                text=True,
                timeout=3,
            )
            fields = completed.stdout.strip().split()
            if len(fields) != 2 or fields[1] != "ext4":
                raise OSError(
                    "runtime root is not one verified ext4 mount")
            live = subprocess.run(
                [
                    "blkid",
                    "-s",
                    "UUID",
                    "-o",
                    "value",
                    fields[0],
                ],
                check=True,
                capture_output=True,
                text=True,
                timeout=3,
            ).stdout.strip().lower()
            live = self.valid_filesystem_uuid(live)
        else:
            raise OSError("unsupported guest storage identity role")
        if live != persisted:
            raise OSError(
                "live guest storage identity does not match persisted identity")
        return live

    @staticmethod
    def persisted_storage_identity(role):
        override = os.environ.get(
            "HPD_GUEST_" +
            role.upper().replace("-", "_") +
            "_FILESYSTEM_UUID",
        )
        if override:
            return GuestAgent.valid_filesystem_uuid(override)
        path = "/etc/hpdos/storage-identities"
        if not os.path.exists(path):
            return None
        if os.path.islink(path):
            raise OSError(
                "guest storage identity record must not be a symbolic link")
        with open(path, "rb") as stream:
            content = stream.read(4097)
        if len(content) > 4096:
            raise OSError(
                "guest storage identity record exceeds its byte bound")
        try:
            text = content.decode("utf-8", errors="strict")
        except UnicodeDecodeError as error:
            raise OSError(
                "guest storage identity record is not strict UTF-8") from error
        entries = {}
        for line in text.splitlines():
            parts = line.split("=", 1)
            if len(parts) != 2 or parts[0] in entries:
                raise OSError(
                    "guest storage identity record is malformed")
            entries[parts[0]] = GuestAgent.valid_filesystem_uuid(
                parts[1])
        return entries.get(role)

    @staticmethod
    def valid_filesystem_uuid(value):
        normalized = value.strip().lower()
        if (not normalized or len(normalized) > 64 or
                any(character not in "0123456789abcdef-"
                    for character in normalized)):
            raise OSError(
                "guest storage filesystem UUID is invalid")
        return normalized

    def ensure_volume_quota(
            self,
            root,
            volumes_root,
            logical_id,
            path,
            maximum_bytes,
            quota_mode,
            ownership):
        state = self.load_volume_quota_state(volumes_root)
        entry = state.get(logical_id)
        project_id = self.volume_project_id(logical_id)
        for other_id, other in state.items():
            if (other_id != logical_id and
                    self.int_value(other.get("ProjectId"), 0) == project_id):
                raise OSError(
                    "durable-volume project identifier collision")
        if entry is not None:
            if not self.volume_quota_entry_matches(
                    entry, project_id, maximum_bytes, ownership):
                raise OSError(
                    "durable-volume quota metadata conflicts with the accepted specification")
        if quota_mode == "ext4-project":
            self.storage_filesystem_identity(root, quota_mode)
            self.assign_project_identity(path, project_id)
            hard_blocks = (maximum_bytes + 1023) // 1024
            self.run_quota_command(
                [
                    "setquota",
                    "-P",
                    str(project_id),
                    str(hard_blocks),
                    str(hard_blocks),
                    "0",
                    "0",
                    root,
                ])
        state[logical_id] = {
            "ProjectId": project_id,
            "MaximumBytes": maximum_bytes,
            **ownership,
        }
        self.save_volume_quota_state(volumes_root, state)
        return project_id

    def assign_project_identity(self, path, project_id):
        self.run_quota_command(
            ["setproject", "-P", str(project_id), path])
        self.run_quota_command(
            ["chattr", "+P", "-p", str(project_id), path])
        self.verify_project_identity(path, project_id)

    def verify_volume_quota(
            self,
            root,
            volumes_root,
            logical_id,
            path,
            maximum_bytes,
            quota_mode,
            ownership):
        if not os.path.isdir(path):
            return None
        state = self.load_volume_quota_state(volumes_root)
        entry = state.get(logical_id)
        if not isinstance(entry, dict):
            raise OSError(
                "durable-volume quota metadata is missing")
        project_id = self.volume_project_id(logical_id)
        if not self.volume_quota_entry_matches(
                entry, project_id, maximum_bytes, ownership):
            raise OSError(
                "durable-volume quota metadata does not match the accepted specification")
        if quota_mode == "ext4-project":
            self.storage_filesystem_identity(root, quota_mode)
            self.verify_project_identity(path, project_id)
        return project_id

    def remove_volume_quota(
            self,
            root,
            volumes_root,
            logical_id,
            project_id,
            quota_mode):
        state = self.load_volume_quota_state(volumes_root)
        entry = state.get(logical_id)
        if not isinstance(entry, dict):
            raise OSError(
                "durable-volume quota metadata disappeared during erase")
        if quota_mode == "ext4-project":
            self.run_quota_command(
                [
                    "setquota",
                    "-P",
                    str(project_id),
                    "0",
                    "0",
                    "0",
                    "0",
                    root,
                ])
        del state[logical_id]
        self.save_volume_quota_state(volumes_root, state)

    @staticmethod
    def volume_project_id(logical_id):
        digest = hashlib.sha256(
            logical_id.encode("utf-8")).digest()
        return 10000 + (
            int.from_bytes(digest[:4], "big") % 2147473647)

    @staticmethod
    def run_quota_command(command):
        completed = subprocess.run(
            command,
            check=False,
            capture_output=True,
            text=True,
            timeout=10,
            env={
                "PATH":
                    "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin",
                "LC_ALL": "C",
            },
        )
        if completed.returncode != 0:
            message = completed.stderr.strip()
            raise OSError(
                command[0] + " failed" +
                (": " + message[:512] if message else ""))

    @staticmethod
    def verify_project_identity(path, expected_project_id):
        descriptor = os.open(
            path,
            os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0),
        )
        try:
            payload = bytearray(28)
            fcntl.ioctl(
                descriptor,
                FS_IOC_FSGETXATTR,
                payload,
                True,
            )
            values = struct.unpack("IIIII8s", payload)
            flags = values[0]
            project_id = values[3]
            if project_id != expected_project_id:
                raise OSError(
                    "durable-volume project identifier is incorrect")
            if flags & FS_XFLAG_PROJINHERIT == 0:
                raise OSError(
                    "durable-volume project inheritance is disabled")
        finally:
            os.close(descriptor)

    @staticmethod
    def load_volume_quota_state(volumes_root):
        path = os.path.join(
            volumes_root,
            ".hpd-volume-quotas-v1.json",
        )
        if not os.path.exists(path):
            return {}
        if os.path.islink(path):
            raise OSError(
                "durable-volume quota state must not be a symbolic link")
        with open(path, "rb") as stream:
            content = stream.read(65537)
        if len(content) > 65536:
            raise OSError(
                "durable-volume quota state exceeds its byte bound")
        try:
            decoded = json.loads(content.decode("utf-8", errors="strict"))
        except (UnicodeDecodeError, ValueError, TypeError) as error:
            raise OSError(
                "durable-volume quota state is malformed") from error
        if (not isinstance(decoded, dict) or
                decoded.get("Schema") !=
                "hpd.guest.volume-quotas/v1" or
                not isinstance(decoded.get("Volumes"), dict)):
            raise OSError(
                "durable-volume quota state schema is invalid")
        return decoded["Volumes"]

    @staticmethod
    def save_volume_quota_state(volumes_root, volumes):
        path = os.path.join(
            volumes_root,
            ".hpd-volume-quotas-v1.json",
        )
        temporary = path + ".tmp-" + str(uuid.uuid4())
        payload = {
            "Schema": "hpd.guest.volume-quotas/v1",
            "Volumes": volumes,
        }
        encoded = json.dumps(
            payload,
            sort_keys=True,
            separators=(",", ":"),
        ).encode("utf-8")
        if len(encoded) > 65536:
            raise OSError(
                "durable-volume quota state exceeds its byte bound")
        descriptor = os.open(
            temporary,
            os.O_WRONLY | os.O_CREAT | os.O_EXCL,
            0o600,
        )
        try:
            with os.fdopen(descriptor, "wb") as stream:
                stream.write(encoded)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, path)
            GuestAgent.fsync_directory(volumes_root)
        except BaseException:
            try:
                os.unlink(temporary)
            except OSError:
                pass
            raise

    @staticmethod
    def measure_storage_tree(root):
        logical = 0
        allocated = 0
        for current, directories, files in os.walk(
            root,
            topdown=True,
            followlinks=False,
        ):
            for name in directories:
                if os.path.islink(os.path.join(current, name)):
                    raise OSError(
                        "symbolic links are not valid durable data")
            for name in files:
                path = os.path.join(current, name)
                if os.path.islink(path):
                    raise OSError("symbolic links are not valid durable data")
                status = os.stat(path, follow_symlinks=False)
                logical += status.st_size
                allocated += status.st_blocks * 512
        return logical, allocated

    @staticmethod
    def fsync_directory(path):
        descriptor = os.open(path, os.O_RDONLY)
        try:
            os.fsync(descriptor)
        finally:
            os.close(descriptor)

    def engine_status(self, request):
        status_request = request.get("EngineStatusRequest") or {}
        host_id = status_request.get("HostId") or request.get("HostId")
        engine_id = status_request.get("EngineId") or "engine-docker"
        provider_generation = self.int_value(
            status_request.get("ProviderGeneration", request.get("ProviderGeneration")),
            0,
        )
        host_start_generation = self.int_value(status_request.get("HostStartGeneration"), 0)
        kind = self.int_value(status_request.get("Kind"), 0)
        api = self.int_value(status_request.get("Api"), 0)
        authority_mode = self.int_value(status_request.get("AuthorityMode"), 1)
        image_store = self.int_value(status_request.get("ImageStore"), 2)
        workload_adoption = self.int_value(status_request.get("WorkloadAdoption"), 0)
        socket_path = self.engine_socket_path(api, authority_mode)
        engine_name = self.engine_name(kind, api)

        if not host_id or provider_generation <= 0:
            return self.error(
                request,
                47,
                "AppleVirtualization.EngineGenerationMissing",
                "Engine status requires a host identity and positive provider generation.",
                retryable=False,
            )

        probe = self.probe_engine(api, socket_path)
        ready = probe["state"] == "ready"
        socket_exists = probe["socket_exists"]
        unsupported = probe["state"] == "unsupported"
        observation_state = 4 if ready else (7 if unsupported else (5 if socket_exists else 1))
        engine_phase = 3 if ready else (5 if unsupported else (4 if socket_exists else 0))
        resource_phase = 3 if ready else (6 if unsupported else (4 if socket_exists else 1))
        message = probe["message"]
        diagnostic_code = {
            "missing": "AppleVirtualization.EngineSocketMissing",
            "unavailable": "AppleVirtualization.EngineUnavailable",
            "malformed": "AppleVirtualization.EngineProbeMalformedResponse",
            "timeout": "AppleVirtualization.EngineProbeTimeout",
            "unsupported": "AppleVirtualization.EngineApiUnsupported",
        }.get(probe["state"], "AppleVirtualization.EngineProbeFailed")
        diagnostics = [] if ready else [{
            "Severity": 3 if not unsupported else 2,
            "Code": diagnostic_code,
            "Message": message,
            "TargetPath": socket_path,
        }]
        engine_generation = self.observe_engine_generation(engine_id, socket_path, probe)
        endpoints = [{
            "Name": engine_name,
            "Api": api,
            "Transport": 2,
            "SocketPath": {"Value": socket_path},
            "AuthorityMode": authority_mode,
            "GuestVisibleOnly": True,
            "SensitivePolicy": {
                "Kind": 1,
                "AuthorityClass": 4 if authority_mode == 1 else 5,
            },
        }] if ready else []
        condition = self.condition(
            "AppleVirtualization.EngineObserved",
            "Ready" if ready else "NotReady",
            message,
            severity=2 if ready else 3,
        )
        engine_status = {
            "HostId": host_id,
            "EngineId": engine_id,
            "ObservationState": observation_state,
            "Kind": kind,
            "Api": api,
            "AuthorityMode": authority_mode,
            "ImageStore": image_store,
            "WorkloadAdoption": workload_adoption,
            "EnginePhase": engine_phase,
            "Phase": resource_phase,
            "Installed": socket_exists,
            "Running": ready,
            "Ready": ready,
            "Version": probe.get("version"),
            "Status": message,
            "Endpoints": endpoints,
            "Containers": [],
            "EndpointsTruncated": False,
            "ContainersTruncated": False,
            "DiagnosticsTruncated": False,
            "Conditions": [condition],
            "Diagnostics": diagnostics,
        }
        guest_status = dict(engine_status)
        guest_status["Generation"] = {
            "ProviderGeneration": provider_generation,
            "HostStartGeneration": host_start_generation,
            "GuestBootId": self.guest_boot_id,
            "GuestBootGeneration": self.guest_boot_generation,
            "GuestAgentGeneration": self.guest_agent_generation,
            "EngineGeneration": engine_generation,
        }
        payload = self.response_base(request, 47)
        payload["EngineStatusResponse"] = dict(engine_status)
        payload["EngineStatusResponse"]["GuestAgentReady"] = True
        payload["EngineStatusResponse"]["GuestEngineStatus"] = guest_status
        return payload

    @staticmethod
    def engine_name(kind, api):
        if api == 1:
            return "Podman"
        if api == 2:
            return "containerd"
        if api == 4:
            return "BuildKit"
        if api == 0:
            return "Docker-compatible"
        return "engine API " + str(api)

    def probe_engine(self, api, socket_path):
        if not os.path.exists(socket_path):
            return {
                "state": "missing",
                "socket_exists": False,
                "message": self.engine_name(0, api) + " socket is not present.",
            }
        if api == 0:
            return self.http_engine_probe(socket_path, "/_ping", "Docker-compatible", b"OK")
        if api == 1:
            return self.http_engine_probe(socket_path, "/libpod/_ping", "Podman", b"OK")
        if api == 2:
            return self.grpc_cli_probe(
                socket_path,
                "containerd",
                ["ctr", "--address", socket_path, "version"],
            )
        if api == 4:
            return self.grpc_cli_probe(
                socket_path,
                "BuildKit",
                ["buildctl", "--addr", "unix://" + socket_path, "debug", "info"],
            )
        return {
            "state": "unsupported",
            "socket_exists": True,
            "message": "Engine API " + str(api) + " has no supported readiness probe.",
        }

    def http_engine_probe(self, socket_path, path, engine_name, expected_body):
        try:
            with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as client:
                client.settimeout(2.0)
                client.connect(socket_path)
                request = (
                    "GET " + path + " HTTP/1.1\r\n"
                    "Host: localhost\r\n"
                    "Connection: close\r\n\r\n"
                ).encode("ascii")
                client.sendall(request)
                response = self.read_bounded_http_response(client)
        except socket.timeout:
            return {
                "state": "timeout",
                "socket_exists": True,
                "message": engine_name + " API readiness probe timed out.",
            }
        except OSError as error:
            return {
                "state": "unavailable",
                "socket_exists": True,
                "message": engine_name + " API is unavailable: " + str(error),
            }

        header, separator, body = response.partition(b"\r\n\r\n")
        status_line = header.split(b"\r\n", 1)[0] if header else b""
        parts = status_line.split()
        headers = {}
        for line in header.split(b"\r\n")[1:]:
            name, delimiter, value = line.partition(b":")
            if delimiter:
                headers[name.strip().lower()] = value.strip().lower()
        if b"chunked" in headers.get(b"transfer-encoding", b""):
            body = self.decode_chunked_http_body(body)
        elif headers.get(b"content-length") is not None:
            try:
                content_length = int(headers[b"content-length"])
                if content_length < 0 or content_length > 65536 or len(body) < content_length:
                    body = None
                else:
                    body = body[:content_length]
            except ValueError:
                body = None
        if separator and len(parts) >= 2 and parts[1] == b"200" and body is not None and body.strip() == expected_body:
            return {
                "state": "ready",
                "socket_exists": True,
                "message": engine_name + " API is ready.",
            }
        return {
            "state": "malformed",
            "socket_exists": True,
            "message": engine_name + " API returned a malformed or unhealthy readiness response.",
        }

    @staticmethod
    def read_bounded_http_response(client, maximum_bytes=65536):
        response = bytearray()
        while len(response) < maximum_bytes:
            chunk = client.recv(min(4096, maximum_bytes - len(response)))
            if not chunk:
                break
            response.extend(chunk)
            header, separator, body = bytes(response).partition(b"\r\n\r\n")
            if not separator:
                continue

            headers = {}
            for line in header.split(b"\r\n")[1:]:
                name, delimiter, value = line.partition(b":")
                if delimiter:
                    headers[name.strip().lower()] = value.strip().lower()
            content_length = headers.get(b"content-length")
            if content_length is not None:
                try:
                    if len(body) >= int(content_length):
                        break
                except ValueError:
                    break
            elif b"chunked" in headers.get(b"transfer-encoding", b""):
                if GuestAgent.chunked_http_body_complete(body):
                    break
        return bytes(response)

    @staticmethod
    def chunked_http_body_complete(body):
        offset = 0
        while True:
            line_end = body.find(b"\r\n", offset)
            if line_end < 0:
                return False
            size_text = body[offset:line_end].split(b";", 1)[0].strip()
            try:
                size = int(size_text, 16)
            except ValueError:
                return True
            offset = line_end + 2
            if size == 0:
                return len(body) >= offset + 2
            if len(body) < offset + size + 2:
                return False
            if body[offset + size:offset + size + 2] != b"\r\n":
                return True
            offset += size + 2

    @staticmethod
    def decode_chunked_http_body(body):
        output = bytearray()
        offset = 0
        while True:
            line_end = body.find(b"\r\n", offset)
            if line_end < 0:
                return None
            try:
                size = int(body[offset:line_end].split(b";", 1)[0].strip(), 16)
            except ValueError:
                return None
            offset = line_end + 2
            if size == 0:
                return bytes(output)
            if len(body) < offset + size + 2:
                return None
            output.extend(body[offset:offset + size])
            if body[offset + size:offset + size + 2] != b"\r\n":
                return None
            offset += size + 2

    def grpc_cli_probe(self, socket_path, engine_name, command):
        try:
            result = subprocess.run(
                command,
                check=False,
                capture_output=True,
                text=True,
                timeout=2.0,
            )
        except subprocess.TimeoutExpired:
            return {
                "state": "timeout",
                "socket_exists": True,
                "message": engine_name + " gRPC readiness probe timed out.",
            }
        except FileNotFoundError:
            return {
                "state": "unsupported",
                "socket_exists": True,
                "message": engine_name + " gRPC probe client is not installed in the guest.",
            }
        except OSError as error:
            return {
                "state": "unavailable",
                "socket_exists": True,
                "message": engine_name + " gRPC API is unavailable: " + str(error),
            }

        output = (result.stdout or "").strip()
        if result.returncode == 0 and output:
            return {
                "state": "ready",
                "socket_exists": True,
                "message": engine_name + " gRPC API is ready.",
                "version": output[:512],
            }
        if result.returncode == 0:
            return {
                "state": "malformed",
                "socket_exists": True,
                "message": engine_name + " gRPC API returned an empty observation.",
            }
        detail = ((result.stderr or "") or output).strip()
        return {
            "state": "unavailable",
            "socket_exists": True,
            "message": engine_name + " gRPC API is unavailable" + (": " + detail[:256] if detail else "."),
        }

    def observe_engine_generation(self, engine_id, socket_path, probe):
        previous = self.engine_generations.get(engine_id)
        identity = None
        try:
            stat = os.stat(socket_path)
            identity = "{}:{}:{}".format(stat.st_dev, stat.st_ino, stat.st_ctime_ns)
        except OSError:
            pass

        generation = self.int_value(previous.get("Generation"), 0) if isinstance(previous, dict) else 0
        previous_identity = previous.get("Identity") if isinstance(previous, dict) else None
        if identity is not None and identity != previous_identity:
            generation += 1
        if generation == 0 and probe["socket_exists"]:
            generation = 1
        self.engine_generations[engine_id] = {
            "Generation": generation,
            "Identity": identity,
        }
        self._save_generation_state()
        return generation

    def engine_socket_path(self, api, authority_mode):
        if api == 2:
            return os.environ.get("HPD_GUEST_AGENT_CONTAINERD_SOCKET", "/run/containerd/containerd.sock")
        if api == 1:
            default = "/run/podman/podman.sock" if authority_mode == 1 else "/run/user/1000/podman/podman.sock"
            return os.environ.get("HPD_GUEST_AGENT_PODMAN_SOCKET", default)
        if api == 4:
            default = "/run/buildkit/buildkitd.sock" if authority_mode == 1 else "/run/user/1000/buildkit-default/buildkitd.sock"
            return os.environ.get("HPD_GUEST_AGENT_BUILDKIT_SOCKET", default)
        default = "/var/run/docker.sock" if authority_mode == 1 else "/run/user/1000/docker.sock"
        return os.environ.get("HPD_GUEST_AGENT_ENGINE_SOCKET", default)

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
        io_spec = start.get("Io") or {}
        merge_standard_error = bool(io_spec.get("MergeStandardError", False))
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
                stderr=subprocess.STDOUT if merge_standard_error else subprocess.PIPE)
        except Exception as exc:
            return self.error(request, 22, "AppleVirtualization.GuestAgentProcessStartFailed", "Failed to start guest process: " + str(exc), retryable=False)

        state = {
            "popen": popen,
            "started_at": self.timestamp(),
            "stdout": bytearray(),
            "stderr": bytearray(),
            "output_accounting": self.process_output_accounting(start),
            "merged_standard_error": merge_standard_error,
            "output_lock": threading.Lock(),
            "output_chunks": [],
            "output_replay_bytes": {0: 0, 1: 0},
            "output_replay_limit": self.process_replay_limit(start),
            "output_history_truncated": False,
            "output_sequence": 0,
            "output_readers": [],
            "output_readers_complete": False,
            "output_drain_timeout_seconds": self.process_output_drain_timeout(start),
            "output_drain_timed_out": False,
            "result": None,
            "finalization_lock": threading.Lock(),
            "finalization_count": 0,
        }
        if not self.add_process(process_id, state):
            try:
                popen.terminate()
            except Exception:
                pass
            return self.error(
                request,
                22,
                "AppleVirtualization.GuestAgentProcessAlreadyExists",
                "A guest process with this identity already exists.",
                retryable=False)
        self.start_process_output_readers(state)
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

    def start_process_output_readers(self, state):
        popen = state["popen"]
        streams = [(getattr(popen, "stdout", None), 0)]
        if not state.get("merged_standard_error", False):
            streams.append((getattr(popen, "stderr", None), 1))
        streams = [(pipe, stream) for pipe, stream in streams if pipe is not None and hasattr(pipe, "read")]
        if not streams:
            state["output_readers_complete"] = True
            return

        remaining = {"count": len(streams)}

        def read_stream(pipe, stream):
            try:
                while True:
                    chunk = pipe.read(4096)
                    if not chunk:
                        break
                    with state["output_lock"]:
                        accounting = state["output_accounting"][stream]
                        accounting["observed"] += len(chunk)
                        remaining_capture = max(0, accounting["limit"] - len(accounting["captured"]))
                        if accounting["capture"] and remaining_capture:
                            accounting["captured"].extend(chunk[:remaining_capture])
                        state["output_sequence"] += 1
                        state["output_chunks"].append({
                            "ProcessId": "",
                            "Stream": stream,
                            "Sequence": state["output_sequence"],
                            "ObservedAt": self.timestamp(),
                            "_bytes": bytes(chunk),
                            "Flags": 0,
                        })
                        state["output_replay_bytes"][stream] += len(chunk)
                        self.prune_process_output_replay(state, stream)
            finally:
                with state["output_lock"]:
                    remaining["count"] -= 1
                    if remaining["count"] == 0:
                        state["output_readers_complete"] = True

        for pipe, stream in streams:
            reader = threading.Thread(target=read_stream, args=(pipe, stream), daemon=True)
            state["output_readers"].append(reader)
            reader.start()

    def process_status(self, request):
        status_request = request.get("ProcessStatusRequest") or {}
        process_id = str(status_request.get("ProcessId") or request.get("ProcessId") or "")
        state = self.get_process(process_id)
        if state is None:
            return self.error(request, 23, "AppleVirtualization.GuestAgentProcessMissing", "Guest process was not found.", retryable=False)
        return self.process_status_payload(request, process_id, state, 23)

    def process_stdin(self, request):
        stdin_request = request.get("ProcessStdinRequest") or {}
        process_id = str(stdin_request.get("ProcessId") or request.get("ProcessId") or "")
        state = self.get_process(process_id)
        if state is None:
            return self.error(request, 24, "AppleVirtualization.GuestAgentProcessMissing", "Guest process was not found.", retryable=False)
        pipe = getattr(state["popen"], "stdin", None)
        try:
            encoded = stdin_request.get("Bytes") or ""
            value = base64.b64decode(encoded) if encoded else b""
            if value and pipe is not None:
                pipe.write(value)
                pipe.flush()
            if stdin_request.get("CloseAfterWrite") and pipe is not None:
                pipe.close()
        except (OSError, ValueError, TypeError) as exc:
            return self.error(request, 24, "AppleVirtualization.GuestAgentProcessStdinFailed", str(exc), retryable=False)
        return self.process_status_payload(request, process_id, state, 24)

    def process_signal(self, request):
        signal_request = request.get("ProcessSignalRequest") or {}
        process_id = str(signal_request.get("ProcessId") or request.get("ProcessId") or "")
        state = self.get_process(process_id)
        if state is None:
            return self.error(request, 25, "AppleVirtualization.GuestAgentProcessMissing", "Guest process was not found.", retryable=False)
        signal_name = self.case_dict(signal_request.get("Signal")).get("Name") or "SIGTERM"
        signal_value = getattr(signal, str(signal_name), None)
        if not isinstance(signal_value, int):
            return self.error(request, 25, "AppleVirtualization.GuestAgentProcessSignalUnsupported", "Unsupported process signal.", retryable=False)
        state["popen"].send_signal(signal_value)
        return self.process_status_payload(request, process_id, state, 25)

    def process_stop(self, request):
        stop_request = request.get("ProcessStopRequest") or {}
        process_id = str(stop_request.get("ProcessId") or request.get("ProcessId") or "")
        state = self.get_process(process_id)
        if state is None:
            return self.error(request, 26, "AppleVirtualization.GuestAgentProcessMissing", "Guest process was not found.", retryable=False)
        popen = state["popen"]
        if popen.poll() is None:
            popen.terminate()
        return self.process_status_payload(request, process_id, state, 26)

    def process_status_payload(self, request, process_id, state, operation):
        popen = state["popen"]
        return_code = popen.poll()
        if return_code is not None and (
                state.get("result") is None or
                (state.get("output_drain_timed_out") and state.get("output_readers_complete"))):
            self.finalize_process_output(process_id, state)
        payload = self.response_base(request, operation)
        status = {
            "ProcessId": process_id,
            "ProcessPhase": 3 if return_code is None else 6,
            "IoState": 1 if return_code is None else 4,
            "ProviderProcessId": "guest-" + process_id,
            "SystemProcessId": popen.pid,
            "Conditions": [],
        }
        if state.get("result") is not None:
            status["Result"] = state["result"]
        payload["ProcessStatusResponse"] = status
        return payload

    def finalize_process_output(self, process_id, state):
        finalization_lock = state.setdefault("finalization_lock", threading.Lock())
        with finalization_lock:
            if state.get("result") is not None and not (
                    state.get("output_drain_timed_out") and
                    state.get("output_readers_complete")):
                return state["result"]

            deadline = time.monotonic() + state.get("output_drain_timeout_seconds", 2.0)
            for reader in state.get("output_readers", []):
                reader.join(timeout=max(0.0, deadline - time.monotonic()))
            readers_complete = bool(state.get("output_readers_complete", False))
            state["output_drain_timed_out"] = not readers_complete
            if not state.get("output_readers") and not state.get("legacy_output_collected", False):
                stdout, stderr = state["popen"].communicate(timeout=0)
                state["stdout"] = bytearray(stdout or b"")
                state["stderr"] = bytearray(
                    b"" if state.get("merged_standard_error", False) else (stderr or b""))
                self.capture_legacy_process_output(state, 0, stdout or b"")
                if not state.get("merged_standard_error", False):
                    self.capture_legacy_process_output(state, 1, stderr or b"")
                state["legacy_output_collected"] = True
            with state["output_lock"]:
                if readers_complete and state["output_chunks"]:
                    state["output_chunks"][-1]["Flags"] |= 1
                    state["output_chunks"][-1]["ProcessId"] = process_id
            exited_at = self.timestamp()
            state["result"] = self.process_result(
                process_id, state["popen"], state, exited_at)
            state["finalization_count"] = state.get("finalization_count", 0) + 1
            for pipe_name in (("stdin", "stdout", "stderr") if readers_complete else ("stdin",)):
                pipe = getattr(state["popen"], pipe_name, None)
                if pipe is not None and hasattr(pipe, "close"):
                    try:
                        pipe.close()
                    except Exception:
                        pass
            return state["result"]

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
        state = self.get_process(process_id)
        if state is None:
            return self.error(request, 27, "AppleVirtualization.GuestAgentProcessMissing", "Guest process was not found.", retryable=False)

        timeout_ms = lifecycle.get("TimeoutMilliseconds")
        timeout_seconds = None
        if isinstance(timeout_ms, (int, float)) and timeout_ms > 0:
            timeout_seconds = timeout_ms / 1000.0

        popen = state["popen"]
        try:
            if state.get("output_readers"):
                popen.wait(timeout=timeout_seconds)
            else:
                stdout, stderr = popen.communicate(timeout=timeout_seconds)
                state["stdout"] = bytearray(stdout or b"")
                state["stderr"] = bytearray(
                    b"" if state.get("merged_standard_error", False) else (stderr or b""))
                self.capture_legacy_process_output(state, 0, stdout or b"")
                if not state.get("merged_standard_error", False):
                    self.capture_legacy_process_output(state, 1, stderr or b"")
                state["legacy_output_collected"] = True
        except subprocess.TimeoutExpired:
            return self.error(request, 27, "AppleVirtualization.GuestAgentProcessWaitTimeout", "Timed out waiting for guest process.", retryable=True)

        result = state.get("result")
        if result is None or (
                state.get("output_drain_timed_out") and state.get("output_readers_complete")):
            result = self.finalize_process_output(process_id, state)
        payload = self.response_base(request, 27)
        payload["ProcessStatusResponse"] = {
            "ProcessId": process_id,
            "ProcessPhase": 6,
            "IoState": 4,
            "ProviderProcessId": "guest-" + process_id,
            "SystemProcessId": popen.pid,
            "Result": result,
            "Conditions": [],
        }
        return payload

    def process_read_output(self, request):
        lifecycle = request.get("ProcessLifecycleRequest") or {}
        process_id = str(lifecycle.get("ProcessId") or request.get("ProcessId") or "")
        state = self.get_process(process_id)
        if state is None:
            return self.error(request, 28, "AppleVirtualization.GuestAgentProcessMissing", "Guest process was not found.", retryable=False)

        payload = self.process_status_payload(request, process_id, state, 28)
        after_sequence = lifecycle.get("AfterOutputSequence")
        after_sequence = int(after_sequence) if isinstance(after_sequence, (int, float)) else 0
        with state["output_lock"]:
            retained = state["output_chunks"]
            if retained:
                earliest = retained[0]["Sequence"]
                if after_sequence >= earliest:
                    acknowledged = [item for item in retained if item["Sequence"] <= after_sequence]
                    for item in acknowledged:
                        stream = item["Stream"]
                        state["output_replay_bytes"][stream] -= len(item.get("_bytes", b""))
                    state["output_chunks"] = [
                        item for item in retained if item["Sequence"] > after_sequence]
                    retained = state["output_chunks"]
                gap = after_sequence < earliest - 1
            else:
                gap = state.get("output_history_truncated", False)
            chunk = next(
                (item.copy() for item in retained
                 if item["Sequence"] > after_sequence),
                None)
        if chunk is not None:
            chunk["ProcessId"] = process_id
            chunk["Bytes"] = base64.b64encode(chunk.pop("_bytes", b"")).decode("ascii")
            if gap:
                chunk["Flags"] |= 2
            payload["ProcessOutputEvent"] = chunk
        return payload

    def process_result(self, process_id, popen, state, exited_at):
        stdout = state["output_accounting"][0]
        stderr = state["output_accounting"][1]
        merged_standard_error = state.get("merged_standard_error", False)
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
                "Stderr": self.stream_output(
                    {"capture": True, "captured": bytearray(), "observed": 0, "limit": 0}
                    if merged_standard_error else stderr),
                "MergedStandardError": merged_standard_error,
                "OutputDrainTimedOut": state.get("output_drain_timed_out", False),
                "OutputDrainTimeout": self.format_duration(
                    state.get("output_drain_timeout_seconds", 2.0)),
            },
            "Violations": [],
            "Diagnostics": [],
        }

    def stream_output(self, accounting):
        value = bytes(accounting.get("captured", b""))
        observed = int(accounting.get("observed", len(value)))
        capture = bool(accounting.get("capture", True))
        return {
            "CapturedBytes": base64.b64encode(value).decode("ascii"),
            "BytesObserved": observed,
            "BytesCaptured": len(value),
            "BytesDiscarded": max(0, observed - len(value)),
            "Truncated": capture and observed > len(value),
        }

    def process_output_accounting(self, start_request):
        io = self.case_dict(start_request.get("Io"))
        return {
            0: self.process_stream_accounting(io.get("StandardOutput")),
            1: self.process_stream_accounting(io.get("StandardError")),
        }

    def capture_legacy_process_output(self, state, stream, value):
        accounting = state["output_accounting"][stream]
        accounting["observed"] += len(value)
        if accounting["capture"]:
            remaining = max(0, accounting["limit"] - len(accounting["captured"]))
            accounting["captured"].extend(value[:remaining])

    def process_stream_accounting(self, value):
        spec = self.case_dict(value)
        capture = bool(spec.get("Capture", True))
        limit = spec.get("MaxCapturedBytes")
        limit = int(limit) if isinstance(limit, (int, float)) and limit >= 0 else 65536
        return {"capture": capture, "limit": limit if capture else 0,
                "captured": bytearray(), "observed": 0}

    def process_replay_limit(self, start_request):
        policy = self.case_dict(self.case_dict(start_request.get("Io")).get("LogPolicy"))
        value = policy.get("MaxRetainedBytesPerStream")
        return int(value) if isinstance(value, (int, float)) and value >= 0 else 65536

    def prune_process_output_replay(self, state, stream):
        limit = state["output_replay_limit"]
        while state["output_replay_bytes"][stream] > limit:
            index = next(
                (index for index, item in enumerate(state["output_chunks"])
                 if item["Stream"] == stream),
                None)
            if index is None:
                break
            oldest = state["output_chunks"][index]
            value = oldest.get("_bytes", b"")
            excess = state["output_replay_bytes"][stream] - limit
            if limit > 0 and excess < len(value):
                oldest["_bytes"] = value[excess:]
                oldest["Flags"] |= 2
                state["output_replay_bytes"][stream] -= excess
            else:
                state["output_chunks"].pop(index)
                state["output_replay_bytes"][stream] -= len(value)
            state["output_history_truncated"] = True

    def process_output_drain_timeout(self, start_request):
        value = self.case_dict(start_request.get("Policy")).get("OutputDrainTimeout")
        if isinstance(value, (int, float)):
            return max(0.0, float(value) / 1000.0)
        if isinstance(value, str):
            try:
                parts = value.split(":")
                if len(parts) == 3:
                    return max(0.0, int(parts[0]) * 3600 + int(parts[1]) * 60 + float(parts[2]))
            except (TypeError, ValueError):
                pass
        return 2.0

    def format_duration(self, seconds):
        return "00:00:{:06.3f}".format(max(0.0, float(seconds)))

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

    def tcp_tunnel(self, request, reader, writer):
        tunnel = request.get("TcpTunnelRequest") or {}
        target_address = str(tunnel.get("TargetAddress") or "")
        target_port = self.positive_int(tunnel.get("TargetPort"), 0)
        if not target_address or target_port <= 0:
            write_frame(
                writer,
                self.error(
                    request,
                    51,
                    "AppleVirtualization.GuestAgentTcpTunnelTargetInvalid",
                    "TcpTunnelRequest requires a target address and port.",
                    retryable=False))
            return
        try:
            target = socket.create_connection(
                (target_address, target_port),
                timeout=5)
        except OSError as exc:
            write_frame(
                writer,
                self.error(
                    request,
                    51,
                    "AppleVirtualization.GuestAgentTcpTunnelFailed",
                    "Guest TCP tunnel failed: " + str(exc),
                    retryable=True))
            return

        payload = self.response_base(request, 51)
        payload["TcpTunnelReady"] = {
            "TargetAddress": target_address,
            "TargetPort": target_port,
        }
        write_frame(writer, payload)

        def copy_to_target():
            try:
                while True:
                    chunk = reader.read(65536)
                    if not chunk:
                        break
                    target.sendall(chunk)
            except (BrokenPipeError, ConnectionError, OSError):
                pass
            finally:
                try:
                    target.shutdown(socket.SHUT_WR)
                except OSError:
                    pass

        upstream = threading.Thread(target=copy_to_target, daemon=True)
        upstream.start()
        try:
            while True:
                chunk = target.recv(65536)
                if not chunk:
                    break
                writer.write(chunk)
                writer.flush()
        except (BrokenPipeError, ConnectionError, OSError):
            pass
        finally:
            try:
                target.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
            target.close()
            upstream.join(timeout=1.0)

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
            try:
                if os.path.islink(target_socket):
                    os.unlink(target_socket)
            except OSError as exc:
                return self.error(request, operation, "AppleVirtualization.GuestAgentAuthorityRevokeFailed", "Failed to revoke authority socket projection: " + str(exc), retryable=False)
            evidence = [self.socket_evidence(
                3,
                target_socket,
                not os.path.lexists(target_socket),
                "Projected authority socket is absent after revoke.")]
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
                revocation_evidence=evidence,
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
        if request.get("Operation") == 51:
            agent.tcp_tunnel(request, reader, writer)
            return
        response = agent.handle(request)
        write_frame(writer, response)
        if (request.get("Operation") == 52 and
                response.get("ResponseStatus") == 1 and
                (response.get("HostShutdownResponse") or {}).get("Accepted") is True):
            agent.host_shutdown_executor()
            return


def serve_stdio(agent):
    serve_stream(agent, sys.stdin.buffer, sys.stdout.buffer)


class ConcurrentConnectionServer:
    def __init__(self, agent, listener, max_workers=16):
        self.agent = agent
        self.listener = listener
        self.max_workers = max(1, int(max_workers))
        self.worker_slots = threading.BoundedSemaphore(self.max_workers)
        self.workers = set()
        self.workers_lock = threading.Lock()
        self.shutdown_requested = threading.Event()
        self.shutdown_lock = threading.Lock()
        self.agent_shutdown = False

    def serve_forever(self):
        self.listener.settimeout(1.0)
        while not self.shutdown_requested.is_set():
            try:
                connection, _ = self.listener.accept()
            except socket.timeout:
                continue
            except OSError:
                if self.shutdown_requested.is_set():
                    break
                raise

            if not self.worker_slots.acquire(blocking=False):
                connection.close()
                continue

            worker = threading.Thread(
                target=self._serve_connection,
                args=(connection,),
                daemon=False)
            with self.workers_lock:
                self.workers.add(worker)
            worker.start()

    def _serve_connection(self, connection):
        try:
            with connection:
                reader = connection.makefile("rb", buffering=0)
                writer = connection.makefile("wb", buffering=0)
                try:
                    serve_stream(self.agent, reader, writer)
                finally:
                    reader.close()
                    writer.close()
        except (BrokenPipeError, ConnectionError, OSError, ValueError, json.JSONDecodeError):
            pass
        finally:
            with self.workers_lock:
                self.workers.discard(threading.current_thread())
            self.worker_slots.release()

    def shutdown(self, timeout=5.0):
        self.shutdown_requested.set()
        try:
            self.listener.close()
        except OSError:
            pass
        with self.shutdown_lock:
            if not self.agent_shutdown:
                self.agent_shutdown = True
                self.agent.shutdown()

        deadline = time.monotonic() + max(0.0, timeout)
        while True:
            with self.workers_lock:
                workers = list(self.workers)
            if not workers:
                return True
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                return False
            for worker in workers:
                worker.join(timeout=min(remaining, 0.1))


def serve_vsock(agent, port):
    listener = socket.socket(AF_VSOCK, socket.SOCK_STREAM)
    listener.bind((VMADDR_CID_ANY, port))
    max_workers = max(
        1,
        int(os.environ.get("HPD_GUEST_AGENT_MAX_CONNECTIONS", "16")))
    listener.listen(max_workers)
    server = ConcurrentConnectionServer(agent, listener, max_workers)
    previous_handlers = {}

    def request_shutdown(_signum, _frame):
        server.shutdown()

    if threading.current_thread() is threading.main_thread():
        for signal_number in (signal.SIGINT, signal.SIGTERM):
            previous_handlers[signal_number] = signal.getsignal(signal_number)
            signal.signal(signal_number, request_shutdown)

    try:
        server.serve_forever()
    finally:
        server.shutdown()
        for signal_number, handler in previous_handlers.items():
            signal.signal(signal_number, handler)


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
