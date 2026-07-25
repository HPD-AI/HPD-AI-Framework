import HPDVZCore

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

print("Engine host routing tests passed.")
