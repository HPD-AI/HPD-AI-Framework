import Foundation
@_spi(Testing) import HPDVZCore

final class RecordingIdleSleepActivities: HostIdleSleepActivityManaging {
    var began: [String] = []
    var ended: [String] = []
    func begin(operationId: String) { began.append(operationId) }
    func end(operationId: String) { ended.append(operationId) }
}

let sleepActivities = RecordingIdleSleepActivities()
let storageService = HelperService(
    adapter: FakeVirtualizationAdapter(),
    idleSleepActivities: sleepActivities)
_ = storageService.handle(HelperEnvelope(raw: [
    "ProtocolVersion": HelperProtocol.currentVersion,
    "Operation": Operation.storage.rawValue,
    "RequestId": "backup-begin-request",
    "SequenceNumber": 1,
    "StorageRequest": [
        "Action": 5,
        "OperationId": "backup-operation"
    ]
]))
precondition(
    sleepActivities.began == ["backup-operation"] &&
        sleepActivities.ended == ["backup-operation"],
    "A failed backup begin must not leak its bounded idle-sleep assertion.")
_ = storageService.handle(HelperEnvelope(raw: [
    "ProtocolVersion": HelperProtocol.currentVersion,
    "Operation": Operation.storage.rawValue,
    "RequestId": "erase-request",
    "SequenceNumber": 2,
    "StorageRequest": [
        "Action": 4,
        "OperationId": "erase-operation"
    ]
]))
precondition(
    sleepActivities.began.last == "erase-operation" &&
        sleepActivities.ended.last == "erase-operation",
    "A single-request irreversible storage mutation must release its idle-sleep assertion.")

let powerMonitor = HostPowerMonitor(
    registerSystemNotifications: false)
precondition(
    powerMonitor.snapshot().state == .active,
    "A new helper power monitor must begin active.")
powerMonitor.simulateSleepForTesting()
let sleepingPower = powerMonitor.snapshot()
precondition(
    sleepingPower.state == .sleeping &&
        sleepingPower.sleepGeneration == 1 &&
        sleepingPower.requiresWakeReconciliation,
    "Sleep must fence mutations and advance the sleep generation.")
powerMonitor.simulateWakeForTesting()
let wakePower = powerMonitor.snapshot()
precondition(
    wakePower.state == .wakeReconciliationRequired &&
        wakePower.wakeGeneration == 1 &&
        wakePower.requiresWakeReconciliation,
    "Wake must retain the mutation fence until reconciliation completes.")
precondition(
    !powerMonitor.acknowledge(wakeGeneration: 0),
    "A stale wake generation must not clear the fence.")
precondition(
    powerMonitor.acknowledge(
        wakeGeneration: wakePower.wakeGeneration),
    "The exact observed wake generation must clear the fence.")
precondition(
    powerMonitor.snapshot().state == .active &&
        !powerMonitor.snapshot().requiresWakeReconciliation,
    "Successful wake reconciliation must restore active state.")
powerMonitor.simulateTerminationForTesting()
precondition(
    powerMonitor.snapshot().state == .terminating &&
        !powerMonitor.acknowledge(
            wakeGeneration: wakePower.wakeGeneration),
    "Termination must remain fenced and cannot be acknowledged as a wake.")

let fencedService = HelperService(adapter: FakeVirtualizationAdapter(
    powerObservation: HostPowerObservation(
        state: .wakeReconciliationRequired,
        sleepGeneration: 1,
        wakeGeneration: 1,
        requiresWakeReconciliation: true,
        observedAt: "2026-07-30T22:00:00Z")))
let fencedMutation = fencedService.handle(HelperEnvelope(raw: [
    "ProtocolVersion": HelperProtocol.currentVersion,
    "Operation": Operation.processStart.rawValue,
    "RequestId": "fenced-process-start",
    "SequenceNumber": 1
]))
let fencedError = fencedMutation["Error"] as? [String: Any]
precondition(
    fencedError?["Code"] as? String ==
        "AppleVirtualization.WakeReconciliationRequired",
    "New mutations must fail closed while wake reconciliation is fenced.")
let allowedCleanup = fencedService.handle(HelperEnvelope(raw: [
    "ProtocolVersion": HelperProtocol.currentVersion,
    "Operation": Operation.hostStop.rawValue,
    "RequestId": "fenced-host-stop",
    "SequenceNumber": 2,
    "HostLifecycleRequest": ["HostId": "host-a"]
]))
precondition(
    allowedCleanup["Error"] == nil,
    "Host stop must remain available while wake reconciliation is fenced.")

let leaseRoot = FileManager.default.temporaryDirectory
    .appendingPathComponent(
        "hpd-vz-disk-lease-\(UUID().uuidString)",
        isDirectory: true)
try FileManager.default.createDirectory(
    at: leaseRoot,
    withIntermediateDirectories: true)
defer {
    try? FileManager.default.removeItem(at: leaseRoot)
}
let leasedDisk = leaseRoot.appendingPathComponent("disk.raw")
precondition(
    FileManager.default.createFile(
        atPath: leasedDisk.path,
        contents: Data(repeating: 0, count: 4096)),
    "The exclusive-disk-lease fixture could not be created.")
var firstLease: ExclusiveDiskLease? =
    try ExclusiveDiskLease(path: leasedDisk.path)
do {
    _ = try ExclusiveDiskLease(path: leasedDisk.path)
    preconditionFailure(
        "Concurrent helper ownership of one disk was accepted.")
} catch {
}
firstLease = nil
_ = try ExclusiveDiskLease(path: leasedDisk.path)

let hardLinkedDisk =
    leaseRoot.appendingPathComponent("disk-alias.raw")
try FileManager.default.linkItem(
    at: leasedDisk,
    to: hardLinkedDisk)
do {
    _ = try ExclusiveDiskLease(path: leasedDisk.path)
    preconditionFailure(
        "A hard-linked VM disk image was accepted.")
} catch {
}

let hosts = [
    "host-a": EngineHostRouteState(running: true, providerGeneration: 7, socketAvailable: true),
    "host-b": EngineHostRouteState(running: true, providerGeneration: 7, socketAvailable: false)
]

precondition(
    EngineHostRouter.resolve(hostId: "host-b", providerGeneration: 7, hosts: hosts)
        == .socketMissing,
    "A request for host B must not resolve host A's socket.")
precondition(
    EngineHostRouter.resolve(hostId: "host-b", providerGeneration: 7, hosts: hosts)
        != .resolved(hostId: "host-a"),
    "A request for host B reached host A.")
precondition(
    EngineHostRouter.resolve(hostId: "missing", providerGeneration: 7, hosts: hosts)
        == .unknownHost,
    "Unknown hosts must be rejected.")
precondition(
    EngineHostRouter.resolve(hostId: "host-a", providerGeneration: 6, hosts: hosts)
        == .staleProviderGeneration,
    "Stale provider generations must be rejected.")

let validResponse: [String: Any] = [
    "HostId": "host-b",
    "EngineStatusResponse": [
        "HostId": "host-b",
        "EngineId": "docker",
        "GuestEngineStatus": [
            "HostId": "host-b",
            "EngineId": "docker",
            "Generation": [
                "ProviderGeneration": 7,
                "HostStartGeneration": 4
            ]
        ]
    ]
]
precondition(
    EngineResponseIdentityValidator.validate(
        response: validResponse,
        hostId: "host-b",
        engineId: "docker",
        providerGeneration: 7,
        hostStartGeneration: 4) == .valid,
    "Matching nested engine identity must be accepted.")

var nestedMismatch = validResponse
nestedMismatch["EngineStatusResponse"] = [
    "HostId": "host-a",
    "EngineId": "docker",
    "GuestEngineStatus": [
        "HostId": "host-b",
        "EngineId": "docker",
        "Generation": [
            "ProviderGeneration": 7,
            "HostStartGeneration": 4
        ]
    ]
]
precondition(
    EngineResponseIdentityValidator.validate(
        response: nestedMismatch,
        hostId: "host-b",
        engineId: "docker",
        providerGeneration: 7,
        hostStartGeneration: 4) == .hostMismatch,
    "A mismatched nested EngineStatusResponse host must be rejected.")

var generationMismatch = validResponse
generationMismatch["EngineStatusResponse"] = [
    "HostId": "host-b",
    "EngineId": "docker",
    "GuestEngineStatus": [
        "HostId": "host-b",
        "EngineId": "docker",
        "Generation": [
            "ProviderGeneration": 6,
            "HostStartGeneration": 4
        ]
    ]
]
precondition(
    EngineResponseIdentityValidator.validate(
        response: generationMismatch,
        hostId: "host-b",
        engineId: "docker",
        providerGeneration: 7,
        hostStartGeneration: 4) == .generationMismatch,
    "A mismatched nested provider generation must be rejected.")

var responseEngineMismatch = validResponse
responseEngineMismatch["EngineStatusResponse"] = [
    "HostId": "host-b",
    "EngineId": "containerd",
    "GuestEngineStatus": [
        "HostId": "host-b",
        "EngineId": "docker",
        "Generation": [
            "ProviderGeneration": 7,
            "HostStartGeneration": 4
        ]
    ]
]
precondition(
    EngineResponseIdentityValidator.validate(
        response: responseEngineMismatch,
        hostId: "host-b",
        engineId: "docker",
        providerGeneration: 7,
        hostStartGeneration: 4) == .engineMismatch,
    "A mismatched EngineStatusResponse engine must be rejected.")

var guestEngineMismatch = validResponse
guestEngineMismatch["EngineStatusResponse"] = [
    "HostId": "host-b",
    "EngineId": "docker",
    "GuestEngineStatus": [
        "HostId": "host-b",
        "EngineId": "containerd",
        "Generation": [
            "ProviderGeneration": 7,
            "HostStartGeneration": 4
        ]
    ]
]
precondition(
    EngineResponseIdentityValidator.validate(
        response: guestEngineMismatch,
        hostId: "host-b",
        engineId: "docker",
        providerGeneration: 7,
        hostStartGeneration: 4) == .engineMismatch,
    "A mismatched GuestEngineStatus engine must be rejected.")

precondition(
    HostDeletionGenerationDecision.evaluate(
        recordGeneration: 8,
        requestGeneration: 7) == .stale,
    "A stale deletion request must be rejected before it can stop or remove the current VM record.")
precondition(
    HostDeletionGenerationDecision.evaluate(
        recordGeneration: 8,
        requestGeneration: 8) == .current,
    "A deletion request from the current provider generation must be eligible for lifecycle handling.")

precondition(
    HostLifecycleObservationDecision.reconcile(
        current: .stopping,
        observed: .running) == .stopping,
    "An accepted guest shutdown must remain stopping while Apple still observes the VM running.")
precondition(
    HostLifecycleObservationDecision.reconcile(
        current: .stopping,
        observed: .stopped) == .stopped,
    "Apple's terminal stopped observation must complete an accepted guest shutdown.")
precondition(
    HostLifecycleObservationDecision.reconcile(
        current: .stopping,
        observed: .failed) == .failed,
    "Apple's terminal error observation must fail an accepted guest shutdown.")

precondition(
    HostStartGenerationDecision.evaluate(
        recordGeneration: 2,
        requestGeneration: 1) == .stale,
    "A process route from the VM incarnation before stop/restart must be rejected.")
precondition(
    HostStartGenerationDecision.evaluate(
        recordGeneration: 2,
        requestGeneration: 2) == .current,
    "A process route for the current restarted VM incarnation must be accepted.")

precondition(
    HostStartLifecycleDecision.evaluate(
        state: .running,
        recordGeneration: 4,
        requestGeneration: 4) == .reuse,
    "Starting an already-running VM must be idempotent only for its current generation.")
precondition(
    HostStartLifecycleDecision.evaluate(
        state: .stopped,
        recordGeneration: 4,
        requestGeneration: 5) == .replace,
    "A stopped VM must accept exactly the next generation and create a replacement incarnation.")
precondition(
    HostStartLifecycleDecision.evaluate(
        state: .stopped,
        recordGeneration: 4,
        requestGeneration: 4) == .reject,
    "A stopped VM must reject reuse of its completed incarnation.")
precondition(
    HostStartGenerationDecision.evaluate(
        recordGeneration: 5,
        requestGeneration: 4) == .stale,
    "After stopped generation 4 restarts as generation 5, process routes for generation 4 must be rejected.")

let normalizedEngineStatus = EngineStatusWireNormalizer.normalize([
    "Ready": false,
    "Diagnostics": [
        [
            "Severity": 3,
            "Code": "AppleVirtualization.Engine.Unavailable",
            "Message": "Docker is not ready."
        ]
    ],
    "GuestEngineStatus": [
        "Diagnostics": [
            [
                "Severity": 3,
                "Code": "AppleVirtualization.Engine.Unavailable",
                "Message": "Docker is not ready."
            ]
        ]
    ]
])
let normalizedDiagnostics = normalizedEngineStatus["Diagnostics"] as? [[String: Any]]
let normalizedCode = normalizedDiagnostics?.first?["Code"] as? [String: Any]
precondition(
    normalizedCode?["Value"] as? String == "AppleVirtualization.Engine.Unavailable",
    "Guest engine diagnostics must use the framework DiagnosticCode wire shape.")
let normalizedGuest = normalizedEngineStatus["GuestEngineStatus"] as? [String: Any]
let normalizedGuestDiagnostics = normalizedGuest?["Diagnostics"] as? [[String: Any]]
let normalizedGuestCode = normalizedGuestDiagnostics?.first?["Code"] as? [String: Any]
precondition(
    normalizedGuestCode?["Value"] as? String == "AppleVirtualization.Engine.Unavailable",
    "Nested guest engine diagnostics must use the framework DiagnosticCode wire shape.")

let wrappedDiagnostic = EngineStatusWireNormalizer.normalize([
    "Diagnostics": [
        [
            "Code": ["Value": "AppleVirtualization.Engine.Wrapped"],
            "Message": "Already canonical."
        ]
    ]
])
let wrappedCode = ((wrappedDiagnostic["Diagnostics"] as? [[String: Any]])?.first?["Code"] as? [String: Any])?["Value"] as? String
precondition(
    wrappedCode == "AppleVirtualization.Engine.Wrapped",
    "Already-wrapped diagnostic codes must remain valid.")

let malformedDiagnostic = EngineStatusWireNormalizer.normalize([
    "Diagnostics": [["Message": "missing code"]]
])
let malformedCode = ((malformedDiagnostic["Diagnostics"] as? [[String: Any]])?.first?["Code"] as? [String: Any])?["Value"] as? String
precondition(
    malformedCode == "AppleVirtualization.EngineDiagnosticMalformed",
    "Malformed diagnostics must become actionable protocol diagnostics.")

let excessiveDiagnostic = EngineStatusWireNormalizer.normalize([
    "Diagnostics": (0..<80).map { index in
        ["Code": "diagnostic-\(index)", "Message": "bounded"]
    }
])
let excessiveDiagnostics = excessiveDiagnostic["Diagnostics"] as? [[String: Any]]
let excessiveLastCode = (excessiveDiagnostics?.last?["Code"] as? [String: Any])?["Value"] as? String
precondition(
    excessiveDiagnostics?.count == 64 &&
        excessiveLastCode == "AppleVirtualization.EngineDiagnosticMalformed",
    "Excessive diagnostics must remain bounded and report truncation.")

let noDiagnostics = EngineStatusWireNormalizer.normalize(["Ready": true])
precondition(
    (noDiagnostics["Diagnostics"] as? [[String: Any]])?.isEmpty == true,
    "An absent diagnostic collection must normalize to an empty collection.")

let mergedProcessRequest = ProcessRequest.parse(from: HelperEnvelope(raw: [
    "Operation": Operation.processStart.rawValue,
    "ProcessStartRequest": [
        "ProcessId": "process-merged",
        "UnitId": "unit-1",
        "Command": [
            "FileName": "/bin/sh",
            "Arguments": ["-c", "printf out; printf err >&2"]
        ],
        "Io": [
            "MergeStandardError": true
        ]
    ]
]))
let mergedGuestPayload = mergedProcessRequest.toGuestPayload(operation: .processStart)
let mergedGuestStart = mergedGuestPayload["ProcessStartRequest"] as? [String: Any]
let mergedGuestIo = mergedGuestStart?["Io"] as? [String: Any]
precondition(
    mergedProcessRequest.mergeStandardError &&
        (mergedGuestIo?["MergeStandardError"] as? Bool) == true,
    "The Swift helper must preserve the requested stderr merge policy at the guest boundary.")
let mergedFakeResult = ProcessStateFactory.result(
    mergedProcessRequest.withScriptedReadinessState(.ready),
    operation: .processWait)
let mergedFakeOutput = mergedFakeResult.result?["Output"] as? [String: Any]
precondition(
    (mergedFakeOutput?["MergedStandardError"] as? Bool) == true,
    "The Swift fake process result must preserve the effective stderr merge state.")

let stdinProcessRequest = ProcessRequest.parse(from: HelperEnvelope(raw: [
    "Operation": Operation.processStdin.rawValue,
    "ProcessStdinRequest": [
        "ProcessId": "process-stdin",
        "Bytes": "aW5wdXQK",
        "Sequence": 7,
        "CloseAfterWrite": true
    ]
]))
let stdinGuestPayload = stdinProcessRequest.toGuestPayload(operation: .processStdin)
let stdinGuestRequest = stdinGuestPayload["ProcessStdinRequest"] as? [String: Any]
precondition(
    (stdinGuestPayload["Operation"] as? Int) == 24 &&
        (stdinGuestRequest?["ProcessId"] as? String) == "process-stdin" &&
        (stdinGuestRequest?["Bytes"] as? String) == "aW5wdXQK" &&
        (stdinGuestRequest?["CloseAfterWrite"] as? Bool) == true,
    "The Swift helper must map process stdin to guest-agent operation 24 without losing its payload.")

print("Engine host routing tests passed.")
