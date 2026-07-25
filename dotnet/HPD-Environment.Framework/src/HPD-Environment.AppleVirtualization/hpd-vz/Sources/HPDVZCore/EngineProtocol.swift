import Foundation

public enum EngineProtocolEventKind {
    public static let engineObserved = 47
    public static let engineDegraded = 48
    public static let engineProvisioned = 49
}

public struct EngineDiagnosticPayload: Codable, Equatable, Sendable {
    public let severity: Int
    public let code: String
    public let message: String
    public let targetPath: String?

    public init(severity: Int = 3, code: String, message: String, targetPath: String? = nil) {
        self.severity = severity
        self.code = code
        self.message = message
        self.targetPath = targetPath
    }

    public static func parse(_ value: Any?) -> EngineDiagnosticPayload? {
        guard let payload = value as? [String: Any] else {
            return nil
        }

        let code = Self.codeValue(payload["Code"]) ??
            VmConfigurationValidationRequest.string(payload["code"])
        guard let code else {
            return nil
        }

        return EngineDiagnosticPayload(
            severity: VmConfigurationValidationRequest.int(payload["Severity"]) ??
                VmConfigurationValidationRequest.int(payload["severity"]) ?? 3,
            code: code,
            message: VmConfigurationValidationRequest.string(payload["Message"]) ??
                VmConfigurationValidationRequest.string(payload["message"]) ?? code,
            targetPath: VmConfigurationValidationRequest.string(payload["TargetPath"]) ??
                VmConfigurationValidationRequest.string(payload["targetPath"]))
    }

    public func toJson() -> [String: Any] {
        var json: [String: Any] = [
            "Severity": severity,
            "Code": ["Value": code],
            "Message": message,
            "ProviderId": ["Value": "hpd.execution.apple-virtualization"]
        ]
        if let targetPath {
            json["TargetPath"] = targetPath
        }
        return json
    }

    private static func codeValue(_ value: Any?) -> String? {
        if let string = value as? String {
            return string
        }
        if let wrapped = value as? [String: Any] {
            return VmConfigurationValidationRequest.string(wrapped["Value"])
        }
        return nil
    }
}

package enum EngineStatusWireNormalizer {
    private static let maximumDiagnosticCount = 64

    package static func normalize(_ payload: [String: Any]) -> [String: Any] {
        var normalized = payload
        normalized["Diagnostics"] = normalizeDiagnostics(payload["Diagnostics"])

        if let guestStatus = payload["GuestEngineStatus"] as? [String: Any] {
            normalized["GuestEngineStatus"] = normalize(guestStatus)
        }

        return normalized
    }

    private static func normalizeDiagnostics(_ value: Any?) -> [[String: Any]] {
        guard let value else {
            return []
        }
        guard let diagnostics = value as? [Any] else {
            return [malformedDiagnostic("The guest engine diagnostic collection was not an array.").toJson()]
        }

        var normalized: [EngineDiagnosticPayload] = []
        var malformedCount = max(0, diagnostics.count - maximumDiagnosticCount)
        for diagnostic in diagnostics.prefix(maximumDiagnosticCount) {
            if let parsed = EngineDiagnosticPayload.parse(diagnostic) {
                normalized.append(parsed)
            } else {
                malformedCount += 1
            }
        }

        if malformedCount > 0 {
            if normalized.count == maximumDiagnosticCount {
                normalized.removeLast()
            }
            normalized.append(malformedDiagnostic(
                "\(malformedCount) malformed or excessive guest engine diagnostic(s) were rejected."))
        }
        return normalized.map { $0.toJson() }
    }

    private static func malformedDiagnostic(_ message: String) -> EngineDiagnosticPayload {
        EngineDiagnosticPayload(
            severity: 4,
            code: "AppleVirtualization.EngineDiagnosticMalformed",
            message: message,
            targetPath: "engineStatus.diagnostics")
    }
}

public struct EngineConditionPayload: Codable, Equatable, Sendable {
    public let type: String
    public let status: Int
    public let reason: String
    public let message: String
    public let severity: Int

    public init(type: String, status: Int, reason: String, message: String, severity: Int) {
        self.type = type
        self.status = status
        self.reason = reason
        self.message = message
        self.severity = severity
    }

    public func toJson(timestamp: String) -> [String: Any] {
        [
            "Type": type,
            "Status": status,
            "Reason": reason,
            "Message": message,
            "LastTransitionAt": timestamp,
            "ObservedGeneration": ["Value": 0],
            "Severity": severity
        ]
    }
}

public struct EngineContainerObservationPayload: Codable, Equatable, Sendable {
    public let containerId: String
    public let name: String?
    public let phase: Int
    public let hpdOwned: Bool
    public let imageRef: String?

    public init(containerId: String, name: String? = nil, phase: Int = 0, hpdOwned: Bool = false, imageRef: String? = nil) {
        self.containerId = containerId
        self.name = name
        self.phase = phase
        self.hpdOwned = hpdOwned
        self.imageRef = imageRef
    }

    public static func parse(_ value: Any?) -> EngineContainerObservationPayload? {
        guard let payload = value as? [String: Any] else {
            return nil
        }
        guard let containerId = VmConfigurationValidationRequest.string(payload["ContainerId"]) else {
            return nil
        }
        return EngineContainerObservationPayload(
            containerId: containerId,
            name: VmConfigurationValidationRequest.string(payload["Name"]),
            phase: VmConfigurationValidationRequest.int(payload["Phase"]) ?? 0,
            hpdOwned: VmConfigurationValidationRequest.bool(payload["HpdOwned"]) ?? false,
            imageRef: VmConfigurationValidationRequest.string(payload["ImageRef"]))
    }

    public func toJson() -> [String: Any] {
        var json: [String: Any] = [
            "ContainerId": containerId,
            "Phase": phase,
            "HpdOwned": hpdOwned
        ]
        if let name {
            json["Name"] = name
        }
        if let imageRef {
            json["ImageRef"] = imageRef
        }
        return json
    }
}

public struct EngineApiEndpointPayload: Codable, Equatable, Sendable {
    public let name: String
    public let api: Int
    public let socketPath: String
    public let authorityMode: Int

    public init(name: String, api: Int, socketPath: String, authorityMode: Int) {
        self.name = name
        self.api = api
        self.socketPath = socketPath
        self.authorityMode = authorityMode
    }

    public func toJson() -> [String: Any] {
        [
            "Name": name,
            "Api": api,
            "Transport": 2,
            "SocketPath": ["Value": socketPath],
            "GuestVisibleOnly": true,
            "HpdPublished": false,
            "RequiresAuthorityBinding": true,
            "SensitivePolicy": [
                "Kind": 1,
                "AuthorityClass": authorityMode == 1 ? 4 : 5,
                "Redaction": 2,
                "RequireAudit": true,
                "RequireExplicitUserApproval": false,
                "Lease": [
                    "Lifetime": 3,
                    "RevokeOnTargetStop": true,
                    "SurviveTargetRestart": false
                ]
            ]
        ]
    }
}

public struct EngineStatusRequestPayload: Codable, Equatable, Sendable {
    public let hostId: String
    public let providerGeneration: UInt64
    public let hostStartGeneration: UInt64
    public let engineId: String?
    public let kind: Int
    public let api: Int
    public let authorityMode: Int
    public let imageStore: Int
    public let workloadAdoption: Int
    public let includeGuestObservation: Bool
    public let explicitRealMode: Bool
    public let observationLocus: Int
    public let observedSocketPath: String?
    public let observedVersion: String?
    public let observedStatus: String?
    public let observedContainers: [EngineContainerObservationPayload]
    public let observedDiagnostics: [EngineDiagnosticPayload]
    public let maxEndpoints: Int
    public let maxContainers: Int
    public let maxDiagnostics: Int
    public let maxVersionLength: Int
    public let maxStatusLength: Int
    public let scriptedObservationState: Int?

    public static func parse(from envelope: HelperEnvelope) -> EngineStatusRequestPayload {
        let payload = envelope.raw["EngineStatusRequest"] as? [String: Any] ?? [:]
        return EngineStatusRequestPayload(
            hostId: VmConfigurationValidationRequest.string(payload["HostId"]) ?? "unknown",
            providerGeneration: VmConfigurationValidationRequest.uint64(payload["ProviderGeneration"])
                ?? VmConfigurationValidationRequest.uint64(envelope.raw["ProviderGeneration"])
                ?? 0,
            hostStartGeneration: VmConfigurationValidationRequest.uint64(payload["HostStartGeneration"]) ?? 0,
            engineId: VmConfigurationValidationRequest.string(payload["EngineId"]),
            kind: VmConfigurationValidationRequest.int(payload["Kind"]) ?? 0,
            api: VmConfigurationValidationRequest.int(payload["Api"]) ?? 0,
            authorityMode: VmConfigurationValidationRequest.int(payload["AuthorityMode"]) ?? 0,
            imageStore: VmConfigurationValidationRequest.int(payload["ImageStore"]) ?? 0,
            workloadAdoption: VmConfigurationValidationRequest.int(payload["WorkloadAdoption"]) ?? 0,
            includeGuestObservation: VmConfigurationValidationRequest.bool(payload["IncludeGuestObservation"]) ?? true,
            explicitRealMode: VmConfigurationValidationRequest.bool(payload["ExplicitRealMode"]) ?? false,
            observationLocus: VmConfigurationValidationRequest.int(payload["ObservationLocus"]) ?? 2,
            observedSocketPath: VmConfigurationValidationRequest.string(payload["ObservedSocketPath"]),
            observedVersion: VmConfigurationValidationRequest.string(payload["ObservedVersion"]),
            observedStatus: VmConfigurationValidationRequest.string(payload["ObservedStatus"]),
            observedContainers: (payload["ObservedContainers"] as? [Any] ?? []).compactMap(EngineContainerObservationPayload.parse),
            observedDiagnostics: (payload["ObservedDiagnostics"] as? [Any] ?? []).compactMap(EngineDiagnosticPayload.parse),
            maxEndpoints: VmConfigurationValidationRequest.int(payload["MaxEndpoints"]) ?? 8,
            maxContainers: VmConfigurationValidationRequest.int(payload["MaxContainers"]) ?? 32,
            maxDiagnostics: VmConfigurationValidationRequest.int(payload["MaxDiagnostics"]) ?? 16,
            maxVersionLength: VmConfigurationValidationRequest.int(payload["MaxVersionLength"]) ?? 128,
            maxStatusLength: VmConfigurationValidationRequest.int(payload["MaxStatusLength"]) ?? 512,
            scriptedObservationState: VmConfigurationValidationRequest.int(payload["ScriptedObservationState"]))
    }
}

public struct EngineStatusPayload: Codable, Equatable, Sendable {
    public let hostId: String
    public let providerGeneration: UInt64
    public let hostStartGeneration: UInt64
    public let engineId: String
    public let observationState: Int
    public let kind: Int
    public let api: Int
    public let authorityMode: Int
    public let imageStore: Int
    public let workloadAdoption: Int
    public let enginePhase: Int
    public let phase: Int
    public let installed: Bool
    public let running: Bool
    public let ready: Bool
    public let version: String?
    public let status: String?
    public let endpoints: [EngineApiEndpointPayload]
    public let containers: [EngineContainerObservationPayload]
    public let endpointsTruncated: Bool
    public let containersTruncated: Bool
    public let diagnosticsTruncated: Bool
    public let conditions: [EngineConditionPayload]
    public let diagnostics: [EngineDiagnosticPayload]

    public static func fromRequest(_ request: EngineStatusRequestPayload) -> EngineStatusPayload {
        let requestedState = request.scriptedObservationState ?? (request.explicitRealMode && request.includeGuestObservation ? 4 : 1)
        let api = resolveApi(request.api, socketPath: request.observedSocketPath)
        let kind = resolveKind(request.kind, api: api)
        let authorityMode = resolveAuthorityMode(request.authorityMode, socketPath: request.observedSocketPath, api: api)
        let hostSocketRejected = request.observationLocus == 0 && isEngineAuthorityObservation(kind: kind, api: api, socketPath: request.observedSocketPath)
        let state = hostSocketRejected ? 7 : requestedState
        let ready = !hostSocketRejected && state == 4
        let degraded = state == 5
        let installed = ready || degraded || state == 2 || state == 3
        let socketPath = socketPathFor(api: api, authorityMode: authorityMode, observedSocketPath: request.observedSocketPath)
        let endpointCandidates = (ready || degraded)
            ? [EngineApiEndpointPayload(name: kindName(kind), api: api, socketPath: socketPath, authorityMode: authorityMode)]
            : []
        let boundedEndpoints = bound(endpointCandidates, request.maxEndpoints)
        let boundedContainers = bound(request.observedContainers, request.maxContainers)
        let diagnostics = bound(diagnosticsFor(state: state, engineId: request.engineId ?? "engine-docker", hostSocketRejected: hostSocketRejected, observed: request.observedDiagnostics), request.maxDiagnostics)
        let message = engineMessageFor(state)

        return EngineStatusPayload(
            hostId: request.hostId,
            providerGeneration: request.providerGeneration,
            hostStartGeneration: request.hostStartGeneration,
            engineId: request.engineId ?? "engine-docker",
            observationState: state,
            kind: kind,
            api: api,
            authorityMode: authorityMode,
            imageStore: request.imageStore,
            workloadAdoption: request.workloadAdoption,
            enginePhase: enginePhaseFor(state),
            phase: resourcePhaseFor(state),
            installed: installed,
            running: ready || degraded,
            ready: ready,
            version: boundString(ready ? request.observedVersion ?? "swift-fake-engine-0.1" : request.observedVersion, maxLength: request.maxVersionLength),
            status: boundString(request.observedStatus ?? message, maxLength: request.maxStatusLength),
            endpoints: boundedEndpoints.items,
            containers: boundedContainers.items,
            endpointsTruncated: boundedEndpoints.truncated,
            containersTruncated: boundedContainers.truncated,
            diagnosticsTruncated: diagnostics.truncated,
            conditions: [
                EngineConditionPayload(
                    type: "AppleVirtualization.EngineObserved",
                    status: ready ? 2 : 1,
                    reason: "\(state)",
                    message: message,
                    severity: ready ? 2 : diagnostics.items.contains { $0.severity >= 4 } ? 4 : 3)
            ],
            diagnostics: diagnostics.items)
    }

    public func responseJson(guestAgentReady: Bool, timestamp: String) -> [String: Any] {
        var json = statusJson(timestamp: timestamp)
        json["GuestAgentReady"] = guestAgentReady
        json["GuestEngineStatus"] = guestStatusJson(timestamp: timestamp)
        return json
    }

    public func guestStatusJson(timestamp: String) -> [String: Any] {
        var json = statusJson(timestamp: timestamp)
        json["Generation"] = [
            "ProviderGeneration": providerGeneration,
            "HostStartGeneration": hostStartGeneration,
            "GuestBootGeneration": 1,
            "GuestAgentGeneration": 1,
            "EngineGeneration": ready ? 1 : 0
        ]
        return json
    }

    private func statusJson(timestamp: String) -> [String: Any] {
        [
            "HostId": hostId,
            "EngineId": engineId,
            "ObservationState": observationState,
            "Kind": kind,
            "Api": api,
            "AuthorityMode": authorityMode,
            "ImageStore": imageStore,
            "WorkloadAdoption": workloadAdoption,
            "EnginePhase": enginePhase,
            "Phase": phase,
            "Installed": installed,
            "Running": running,
            "Ready": ready,
            "Version": version ?? NSNull(),
            "Status": status ?? NSNull(),
            "Endpoints": endpoints.map { $0.toJson() },
            "Containers": containers.map { $0.toJson() },
            "EndpointsTruncated": endpointsTruncated,
            "ContainersTruncated": containersTruncated,
            "DiagnosticsTruncated": diagnosticsTruncated,
            "Conditions": conditions.map { $0.toJson(timestamp: timestamp) },
            "Diagnostics": diagnostics.map { $0.toJson() }
        ]
    }

    private static func resolveApi(_ requestedApi: Int, socketPath: String?) -> Int {
        guard let socketPath = socketPath?.lowercased(), !socketPath.isEmpty else {
            return requestedApi
        }
        if socketPath.contains("containerd") {
            return 2
        }
        if socketPath.contains("podman") {
            return 1
        }
        if socketPath.contains("buildkit") {
            return 4
        }
        if socketPath.hasSuffix("/docker.sock") {
            return 0
        }
        return requestedApi
    }

    private static func resolveKind(_ requestedKind: Int, api: Int) -> Int {
        switch api {
        case 0:
            return 0
        case 1:
            return 1
        case 2:
            return 2
        case 4:
            return 4
        default:
            return requestedKind
        }
    }

    private static func resolveAuthorityMode(_ requestedMode: Int, socketPath: String?, api: Int) -> Int {
        guard let socketPath = socketPath?.lowercased(), !socketPath.isEmpty else {
            return requestedMode
        }
        if socketPath.hasPrefix("/run/user/") {
            return 0
        }
        if socketPath == "/var/run/docker.sock" ||
            socketPath == "/run/docker.sock" ||
            socketPath.hasPrefix("/run/containerd/") ||
            socketPath.hasPrefix("/var/run/containerd/") ||
            socketPath.hasPrefix("/run/buildkit/") ||
            api == 2 ||
            api == 4 {
            return 1
        }
        return requestedMode
    }

    private static func socketPathFor(api: Int, authorityMode: Int, observedSocketPath: String?) -> String {
        if let observedSocketPath, !observedSocketPath.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            return observedSocketPath
        }
        switch api {
        case 2:
            return "/run/containerd/containerd.sock"
        case 1:
            return "/run/user/1000/podman/podman.sock"
        case 4:
            return authorityMode == 0 ? "/run/user/1000/buildkit-default/buildkitd.sock" : "/run/buildkit/buildkitd.sock"
        default:
            return authorityMode == 1 ? "/var/run/docker.sock" : "/run/user/1000/docker.sock"
        }
    }

    private static func isEngineAuthorityObservation(kind: Int, api: Int, socketPath: String?) -> Bool {
        if [0, 1, 2, 4].contains(kind) || [0, 1, 2, 4].contains(api) {
            return true
        }
        guard let socketPath = socketPath?.lowercased() else {
            return false
        }
        return socketPath.contains("docker.sock") ||
            socketPath.contains("podman") ||
            socketPath.contains("containerd") ||
            socketPath.contains("buildkit")
    }

    private static func diagnosticsFor(state: Int, engineId: String, hostSocketRejected: Bool, observed: [EngineDiagnosticPayload]) -> [EngineDiagnosticPayload] {
        var diagnostics: [EngineDiagnosticPayload] = []
        if state == 5 || state == 6 {
            diagnostics.append(EngineDiagnosticPayload(
                severity: state == 6 ? 4 : 3,
                code: "AppleVirtualization.Engine\(stateName(state))",
                message: engineMessageFor(state),
                targetPath: engineId))
        }
        if hostSocketRejected {
            diagnostics.append(EngineDiagnosticPayload(
                severity: 4,
                code: "AppleVirtualization.EngineStatusHostSocketRejected",
                message: "Host Docker, Podman, containerd, and BuildKit sockets cannot satisfy Apple Virtualization engine status; status must be observed inside the guest.",
                targetPath: "engine.status.socket"))
        }
        diagnostics.append(contentsOf: observed)
        return diagnostics
    }

    private static func enginePhaseFor(_ state: Int) -> Int {
        switch state {
        case 4:
            return 3
        case 3:
            return 2
        case 5:
            return 4
        case 6, 7:
            return 7
        case 2:
            return 6
        default:
            return 0
        }
    }

    private static func resourcePhaseFor(_ state: Int) -> Int {
        switch state {
        case 4:
            return 3
        case 5:
            return 4
        case 6, 7:
            return 5
        case 3:
            return 2
        default:
            return 1
        }
    }

    private static func engineMessageFor(_ state: Int) -> String {
        switch state {
        case 4:
            return "Guest engine API is ready inside the VM and requires authority binding for access."
        case 5:
            return "Guest engine is present but degraded; API access remains authority-bound."
        case 1:
            return "Guest engine is not installed or not configured."
        case 8:
            return "Guest engine observation requires explicit opt-in configuration."
        case 7:
            return "Guest engine status cannot be satisfied by host engine sockets."
        default:
            return "Guest engine status was observed."
        }
    }

    private static func kindName(_ kind: Int) -> String {
        switch kind {
        case 1:
            return "Podman"
        case 2:
            return "Containerd"
        case 3:
            return "Kubernetes"
        case 4:
            return "BuildKit"
        case 5:
            return "ProviderDefined"
        default:
            return "DockerCompatible"
        }
    }

    private static func stateName(_ state: Int) -> String {
        switch state {
        case 5:
            return "Degraded"
        case 6:
            return "Failed"
        default:
            return "\(state)"
        }
    }

    private static func bound<T>(_ items: [T], _ limit: Int) -> (items: [T], truncated: Bool) {
        let boundedLimit = min(max(limit, 0), items.count)
        return (Array(items.prefix(boundedLimit)), items.count > boundedLimit)
    }

    private static func boundString(_ value: String?, maxLength: Int) -> String? {
        guard let value else {
            return nil
        }
        let boundedLength = min(max(maxLength, 0), value.count)
        return String(value.prefix(boundedLength))
    }
}

public struct EngineProvisioningPrerequisiteStatusPayload: Codable, Equatable, Sendable {
    public let packageManagerAvailable: Bool
    public let systemdAvailable: Bool
    public let userSystemdAvailable: Bool
    public let guestAgentAvailable: Bool
    public let rootlessSupported: Bool
    public let rootfulSupported: Bool
    public let imageStoreSupported: Bool
    public let networkAvailable: Bool
    public let writableGuestStorageAvailable: Bool
    public let guestOsSupported: Bool

    public static let supported = EngineProvisioningPrerequisiteStatusPayload(
        packageManagerAvailable: true,
        systemdAvailable: true,
        userSystemdAvailable: true,
        guestAgentAvailable: true,
        rootlessSupported: true,
        rootfulSupported: true,
        imageStoreSupported: true,
        networkAvailable: true,
        writableGuestStorageAvailable: true,
        guestOsSupported: true)

    public static func parse(_ value: Any?) -> EngineProvisioningPrerequisiteStatusPayload {
        guard let payload = value as? [String: Any] else {
            return .supported
        }
        return EngineProvisioningPrerequisiteStatusPayload(
            packageManagerAvailable: VmConfigurationValidationRequest.bool(payload["PackageManagerAvailable"]) ?? true,
            systemdAvailable: VmConfigurationValidationRequest.bool(payload["SystemdAvailable"]) ?? true,
            userSystemdAvailable: VmConfigurationValidationRequest.bool(payload["UserSystemdAvailable"]) ?? true,
            guestAgentAvailable: VmConfigurationValidationRequest.bool(payload["GuestAgentAvailable"]) ?? true,
            rootlessSupported: VmConfigurationValidationRequest.bool(payload["RootlessSupported"]) ?? true,
            rootfulSupported: VmConfigurationValidationRequest.bool(payload["RootfulSupported"]) ?? true,
            imageStoreSupported: VmConfigurationValidationRequest.bool(payload["ImageStoreSupported"]) ?? true,
            networkAvailable: VmConfigurationValidationRequest.bool(payload["NetworkAvailable"]) ?? true,
            writableGuestStorageAvailable: VmConfigurationValidationRequest.bool(payload["WritableGuestStorageAvailable"]) ?? true,
            guestOsSupported: VmConfigurationValidationRequest.bool(payload["GuestOsSupported"]) ?? true)
    }

    public func toJson() -> [String: Any] {
        [
            "PackageManagerAvailable": packageManagerAvailable,
            "SystemdAvailable": systemdAvailable,
            "UserSystemdAvailable": userSystemdAvailable,
            "GuestAgentAvailable": guestAgentAvailable,
            "RootlessSupported": rootlessSupported,
            "RootfulSupported": rootfulSupported,
            "ImageStoreSupported": imageStoreSupported,
            "NetworkAvailable": networkAvailable,
            "WritableGuestStorageAvailable": writableGuestStorageAvailable,
            "GuestOsSupported": guestOsSupported
        ]
    }

    public var missingDiagnostics: [EngineDiagnosticPayload] {
        var diagnostics: [EngineDiagnosticPayload] = []
        addIfMissing(&diagnostics, packageManagerAvailable, "AppleVirtualization.EngineProvisioning.PackageManagerMissing", "Engine provisioning requires a supported in-guest package manager.", "engine.provisioning.packageManager")
        addIfMissing(&diagnostics, systemdAvailable, "AppleVirtualization.EngineProvisioning.SystemdMissing", "Rootful engine provisioning requires systemd inside the guest.", "engine.provisioning.systemd")
        addIfMissing(&diagnostics, guestAgentAvailable, "AppleVirtualization.EngineProvisioning.GuestAgentMissing", "Engine provisioning requires the HPD guest agent.", "engine.provisioning.guestAgent")
        addIfMissing(&diagnostics, imageStoreSupported, "AppleVirtualization.EngineProvisioning.ImageStoreUnsupported", "The requested engine image-store mode is not supported by provisioning.", "engine.provisioning.imageStore")
        addIfMissing(&diagnostics, networkAvailable, "AppleVirtualization.EngineProvisioning.NetworkMissing", "Engine provisioning requires guest network egress for package retrieval.", "engine.provisioning.network")
        addIfMissing(&diagnostics, writableGuestStorageAvailable, "AppleVirtualization.EngineProvisioning.WritableStorageMissing", "Engine provisioning requires writable guest storage.", "engine.provisioning.storage")
        addIfMissing(&diagnostics, guestOsSupported, "AppleVirtualization.EngineProvisioning.GuestOsUnsupported", "The guest OS is not a supported Linux target for engine provisioning.", "engine.provisioning.guestOs")
        return diagnostics
    }

    private func addIfMissing(_ diagnostics: inout [EngineDiagnosticPayload], _ available: Bool, _ code: String, _ message: String, _ targetPath: String) {
        if !available {
            diagnostics.append(EngineDiagnosticPayload(severity: 3, code: code, message: message, targetPath: targetPath))
        }
    }
}

public struct EngineProvisioningRequestPayload: Codable, Equatable, Sendable {
    public let hostId: String
    public let engineId: String?
    public let kind: Int
    public let api: Int
    public let authorityMode: Int
    public let imageStore: Int
    public let workloadAdoption: Int
    public let explicitRealMode: Bool
    public let allowPackageInstall: Bool
    public let allowServiceEnablement: Bool
    public let provisioningTimeoutMilliseconds: Int
    public let maxCapturedOutputBytes: Int
    public let packageManager: Int
    public let scriptedExecutionState: Int
    public let scriptedPrerequisites: EngineProvisioningPrerequisiteStatusPayload
    public let scriptedOutput: String?
    public let scriptedStdout: String?
    public let scriptedStderr: String?

    public static func parse(from envelope: HelperEnvelope) -> EngineProvisioningRequestPayload {
        let payload = envelope.raw["EngineProvisioningRequest"] as? [String: Any] ?? [:]
        return EngineProvisioningRequestPayload(
            hostId: VmConfigurationValidationRequest.string(payload["HostId"]) ?? "unknown",
            engineId: VmConfigurationValidationRequest.string(payload["EngineId"]),
            kind: VmConfigurationValidationRequest.int(payload["Kind"]) ?? 0,
            api: VmConfigurationValidationRequest.int(payload["Api"]) ?? 0,
            authorityMode: VmConfigurationValidationRequest.int(payload["AuthorityMode"]) ?? 0,
            imageStore: VmConfigurationValidationRequest.int(payload["ImageStore"]) ?? 0,
            workloadAdoption: VmConfigurationValidationRequest.int(payload["WorkloadAdoption"]) ?? 0,
            explicitRealMode: VmConfigurationValidationRequest.bool(payload["ExplicitRealMode"]) ?? false,
            allowPackageInstall: VmConfigurationValidationRequest.bool(payload["AllowPackageInstall"]) ?? false,
            allowServiceEnablement: VmConfigurationValidationRequest.bool(payload["AllowServiceEnablement"]) ?? false,
            provisioningTimeoutMilliseconds: VmConfigurationValidationRequest.int(payload["ProvisioningTimeoutMilliseconds"]) ?? 120_000,
            maxCapturedOutputBytes: VmConfigurationValidationRequest.int(payload["MaxCapturedOutputBytes"]) ?? 4096,
            packageManager: VmConfigurationValidationRequest.int(payload["PackageManager"]) ?? 0,
            scriptedExecutionState: VmConfigurationValidationRequest.int(payload["ScriptedExecutionState"]) ?? 0,
            scriptedPrerequisites: EngineProvisioningPrerequisiteStatusPayload.parse(payload["ScriptedPrerequisites"]),
            scriptedOutput: VmConfigurationValidationRequest.string(payload["ScriptedOutput"]),
            scriptedStdout: VmConfigurationValidationRequest.string(payload["ScriptedStdout"]),
            scriptedStderr: VmConfigurationValidationRequest.string(payload["ScriptedStderr"]))
    }
}

public struct EngineProvisioningPlanStepPayload: Codable, Equatable, Sendable {
    public let name: String
    public let action: Int
    public let required: Bool
    public let detail: String?

    public init(name: String, action: Int, required: Bool = true, detail: String? = nil) {
        self.name = name
        self.action = action
        self.required = required
        self.detail = detail
    }

    public func toJson() -> [String: Any] {
        var json: [String: Any] = [
            "Name": name,
            "Action": action,
            "Required": required
        ]
        if let detail {
            json["Detail"] = detail
        }
        return json
    }
}

public struct EngineProvisioningOutputCapturePayload: Codable, Equatable, Sendable {
    public let maxCapturedBytes: Int
    public let capturedBytes: Int
    public let truncated: Bool
    public let text: String?
    public let stdoutCapturedBytes: Int
    public let stderrCapturedBytes: Int
    public let stdoutTruncated: Bool
    public let stderrTruncated: Bool
    public let stdoutText: String?
    public let stderrText: String?

    public static func capture(_ output: String?, maxBytes: Int) -> EngineProvisioningOutputCapturePayload {
        capture(output, stdout: output, stderr: nil, maxBytes: maxBytes)
    }

    public static func capture(_ output: String?, stdout: String?, stderr: String?, maxBytes: Int) -> EngineProvisioningOutputCapturePayload {
        let boundedMax = min(max(maxBytes, 0), 4096)
        let combined = captureText(output, maxBytes: boundedMax)
        let stdoutCapture = captureText(stdout, maxBytes: boundedMax)
        let stderrCapture = captureText(stderr, maxBytes: boundedMax)
        return EngineProvisioningOutputCapturePayload(
            maxCapturedBytes: boundedMax,
            capturedBytes: combined.capturedBytes,
            truncated: combined.truncated,
            text: combined.text,
            stdoutCapturedBytes: stdoutCapture.capturedBytes,
            stderrCapturedBytes: stderrCapture.capturedBytes,
            stdoutTruncated: stdoutCapture.truncated,
            stderrTruncated: stderrCapture.truncated,
            stdoutText: stdoutCapture.text,
            stderrText: stderrCapture.text)
    }

    public func toJson() -> [String: Any] {
        var json: [String: Any] = [
            "MaxCapturedBytes": maxCapturedBytes,
            "CapturedBytes": capturedBytes,
            "Truncated": truncated,
            "StdoutCapturedBytes": stdoutCapturedBytes,
            "StderrCapturedBytes": stderrCapturedBytes,
            "StdoutTruncated": stdoutTruncated,
            "StderrTruncated": stderrTruncated
        ]
        if let text {
            json["Text"] = text
        }
        if let stdoutText {
            json["StdoutText"] = stdoutText
        }
        if let stderrText {
            json["StderrText"] = stderrText
        }
        return json
    }

    private static func captureText(_ output: String?, maxBytes: Int) -> (text: String?, capturedBytes: Int, truncated: Bool) {
        guard let output, !output.isEmpty, maxBytes > 0 else {
            return (nil, 0, false)
        }
        let text = String(output.prefix(maxBytes))
        return (text, text.count, output.count > maxBytes)
    }
}

public struct EngineProvisioningEvidencePayload: Codable, Equatable, Sendable {
    public let helperMediated: Bool
    public let guestAgentMediated: Bool
    public let hostShellInvoked: Bool
    public let hostDockerInvoked: Bool
    public let packageManager: Int
    public let packageManagerAvailable: Bool
    public let networkAvailable: Bool
    public let writableGuestStorageAvailable: Bool
    public let systemdAvailable: Bool
    public let userSystemdAvailable: Bool
    public let existingEngineObserved: Bool
    public let packageInstallAllowed: Bool
    public let serviceEnablementAllowed: Bool
    public let installAttempted: Bool
    public let timeoutMilliseconds: Int
    public let timedOut: Bool
    public let maxCapturedOutputBytes: Int
    public let stdoutCapturedBytes: Int
    public let stderrCapturedBytes: Int
    public let stdoutTruncated: Bool
    public let stderrTruncated: Bool

    public static func from(
        request: EngineProvisioningRequestPayload,
        output: EngineProvisioningOutputCapturePayload,
        existing: Bool,
        installAttempted: Bool
    ) -> EngineProvisioningEvidencePayload {
        EngineProvisioningEvidencePayload(
            helperMediated: true,
            guestAgentMediated: true,
            hostShellInvoked: false,
            hostDockerInvoked: false,
            packageManager: request.packageManager,
            packageManagerAvailable: request.scriptedPrerequisites.packageManagerAvailable,
            networkAvailable: request.scriptedPrerequisites.networkAvailable,
            writableGuestStorageAvailable: request.scriptedPrerequisites.writableGuestStorageAvailable,
            systemdAvailable: request.scriptedPrerequisites.systemdAvailable,
            userSystemdAvailable: request.scriptedPrerequisites.userSystemdAvailable,
            existingEngineObserved: existing,
            packageInstallAllowed: request.allowPackageInstall,
            serviceEnablementAllowed: request.allowServiceEnablement,
            installAttempted: installAttempted,
            timeoutMilliseconds: max(0, request.provisioningTimeoutMilliseconds),
            timedOut: request.scriptedExecutionState == 2,
            maxCapturedOutputBytes: output.maxCapturedBytes,
            stdoutCapturedBytes: output.stdoutCapturedBytes,
            stderrCapturedBytes: output.stderrCapturedBytes,
            stdoutTruncated: output.stdoutTruncated,
            stderrTruncated: output.stderrTruncated)
    }

    public func toJson() -> [String: Any] {
        [
            "HelperMediated": helperMediated,
            "GuestAgentMediated": guestAgentMediated,
            "HostShellInvoked": hostShellInvoked,
            "HostDockerInvoked": hostDockerInvoked,
            "PackageManager": packageManager,
            "PackageManagerAvailable": packageManagerAvailable,
            "NetworkAvailable": networkAvailable,
            "WritableGuestStorageAvailable": writableGuestStorageAvailable,
            "SystemdAvailable": systemdAvailable,
            "UserSystemdAvailable": userSystemdAvailable,
            "ExistingEngineObserved": existingEngineObserved,
            "PackageInstallAllowed": packageInstallAllowed,
            "ServiceEnablementAllowed": serviceEnablementAllowed,
            "InstallAttempted": installAttempted,
            "TimeoutMilliseconds": timeoutMilliseconds,
            "TimedOut": timedOut,
            "MaxCapturedOutputBytes": maxCapturedOutputBytes,
            "StdoutCapturedBytes": stdoutCapturedBytes,
            "StderrCapturedBytes": stderrCapturedBytes,
            "StdoutTruncated": stdoutTruncated,
            "StderrTruncated": stderrTruncated
        ]
    }
}

public struct EngineProvisioningResponsePayload: Codable, Equatable, Sendable {
    public let hostId: String
    public let engineId: String?
    public let phase: Int
    public let kind: Int
    public let api: Int
    public let authorityMode: Int
    public let imageStore: Int
    public let existingEngineObserved: Bool
    public let installAttempted: Bool
    public let guestSocketPath: String?
    public let prerequisites: EngineProvisioningPrerequisiteStatusPayload
    public let plan: [EngineProvisioningPlanStepPayload]
    public let output: EngineProvisioningOutputCapturePayload
    public let evidence: EngineProvisioningEvidencePayload
    public let conditions: [EngineConditionPayload]
    public let diagnostics: [EngineDiagnosticPayload]

    public static func plan(for request: EngineProvisioningRequestPayload) -> EngineProvisioningResponsePayload {
        var diagnostics = request.scriptedPrerequisites.missingDiagnostics
        if request.authorityMode != 0 && request.authorityMode != 1 {
            diagnostics.insert(EngineDiagnosticPayload(
                severity: 3,
                code: "AppleVirtualization.EngineProvisioning.AuthorityModeUnsupported",
                message: "Engine provisioning supports explicit rootless or rootful authority mode only.",
                targetPath: "engine.provisioning.authorityMode"), at: 0)
        }
        if request.authorityMode == 0 && !request.scriptedPrerequisites.userSystemdAvailable {
            diagnostics.append(EngineDiagnosticPayload(severity: 3, code: "AppleVirtualization.EngineProvisioning.UserSystemdMissing", message: "Rootless engine provisioning requires user systemd inside the guest.", targetPath: "engine.provisioning.userSystemd"))
        }
        if request.authorityMode == 0 && !request.scriptedPrerequisites.rootlessSupported {
            diagnostics.append(EngineDiagnosticPayload(severity: 3, code: "AppleVirtualization.EngineProvisioning.RootlessUnsupported", message: "The guest does not support rootless engine provisioning.", targetPath: "engine.provisioning.authorityMode"))
        }
        if request.authorityMode == 1 && !request.scriptedPrerequisites.rootfulSupported {
            diagnostics.append(EngineDiagnosticPayload(severity: 3, code: "AppleVirtualization.EngineProvisioning.RootfulUnsupported", message: "The guest does not support rootful engine provisioning.", targetPath: "engine.provisioning.authorityMode"))
        }

        let existing = containsExistingEngineMarker(request.scriptedOutput) ||
            containsExistingEngineMarker(request.scriptedStdout) ||
            containsExistingEngineMarker(request.scriptedStderr)
        if !existing {
            if !request.allowPackageInstall {
                diagnostics.append(EngineDiagnosticPayload(
                    severity: 3,
                    code: "AppleVirtualization.EngineProvisioning.PackageInstallDisabled",
                    message: "Engine provisioning package installation is disabled; set the explicit package-install gate before the guest agent can install Docker or containerd packages.",
                    targetPath: "engine.provisioning.allowPackageInstall"))
            }
            if !request.allowServiceEnablement {
                diagnostics.append(EngineDiagnosticPayload(
                    severity: 3,
                    code: "AppleVirtualization.EngineProvisioning.ServiceEnablementDisabled",
                    message: "Engine provisioning service enablement is disabled; set the explicit service gate before the guest agent can enable or start Docker or containerd services.",
                    targetPath: "engine.provisioning.allowServiceEnablement"))
            }
        }

        let degraded = !diagnostics.isEmpty
        let output = EngineProvisioningOutputCapturePayload.capture(
            request.scriptedOutput ?? request.scriptedStdout ?? request.scriptedStderr,
            stdout: request.scriptedStdout ?? request.scriptedOutput,
            stderr: request.scriptedStderr,
            maxBytes: request.maxCapturedOutputBytes)
        let executionFailed = !degraded && !existing && (request.scriptedExecutionState == 2 || request.scriptedExecutionState == 3)
        let executionSucceeded = !degraded && !existing && request.scriptedExecutionState == 1
        if executionFailed {
            diagnostics.append(executionDiagnostic(request))
        }

        let socketPath = socketPathFor(api: request.api, authorityMode: request.authorityMode)
        let plan: [EngineProvisioningPlanStepPayload]
        if degraded {
            plan = [step("validate-prerequisites", 1, "Provisioning is blocked until guest prerequisites are available.")]
        } else if existing {
            plan = [step("observe-existing-engine", 0, "Existing in-guest engine was observed; no package installation is planned.")]
        } else if executionSucceeded {
            plan = [step("observe-provisioned-engine", 5, "Guest-agent provisioning completed and readiness can be observed on the next status probe.")]
        } else if executionFailed {
            plan = [step("execute-provisioning", 2, "Guest-agent provisioning execution did not complete successfully.")]
        } else {
            plan = [
                step("validate-prerequisites", 1, "Verify package manager, init system, network, and writable guest storage."),
                step("install-engine-packages", 2, "Install Docker/containerd-compatible engine packages inside the guest when explicitly allowed."),
                step("configure-engine-service", 3, "Configure the guest-local engine service and socket path."),
                step("enable-engine-service", 4, "Enable the appropriate system or user service."),
                step("start-engine-service", 5, "Start the engine service and observe readiness.")
            ]
        }

        let phase = degraded ? 3 : executionFailed ? 4 : existing || executionSucceeded ? 2 : 1
        let installAttempted = executionSucceeded || executionFailed
        let evidence = EngineProvisioningEvidencePayload.from(
            request: request,
            output: output,
            existing: existing,
            installAttempted: installAttempted)
        let reason = degraded ? "PrerequisitesMissing" :
            executionFailed ? "\(request.scriptedExecutionState)" :
            existing ? "ExistingEngineObserved" :
            executionSucceeded ? "ProvisioningExecuted" : "ProvisioningPlanned"
        let message = degraded ? "Engine provisioning is blocked by missing guest prerequisites." :
            executionFailed ? "Engine provisioning execution failed or timed out inside the guest-agent path." :
            existing ? "Existing in-guest engine was observed; no install attempt was made." :
            executionSucceeded ? "Engine provisioning was executed inside the guest-agent path." :
            "Engine provisioning plan was accepted for explicit in-guest bootstrap."

        return EngineProvisioningResponsePayload(
            hostId: request.hostId,
            engineId: request.engineId,
            phase: phase,
            kind: request.kind,
            api: request.api,
            authorityMode: request.authorityMode,
            imageStore: request.imageStore,
            existingEngineObserved: existing,
            installAttempted: installAttempted,
            guestSocketPath: socketPath,
            prerequisites: request.scriptedPrerequisites,
            plan: plan,
            output: output,
            evidence: evidence,
            conditions: [EngineConditionPayload(type: "AppleVirtualization.EngineProvisioning", status: degraded || executionFailed ? 1 : 2, reason: reason, message: message, severity: degraded ? 3 : executionFailed ? 4 : 2)],
            diagnostics: diagnostics)
    }

    public func toJson(timestamp: String) -> [String: Any] {
        var json: [String: Any] = [
            "HostId": hostId,
            "Phase": phase,
            "Kind": kind,
            "Api": api,
            "AuthorityMode": authorityMode,
            "ImageStore": imageStore,
            "ExistingEngineObserved": existingEngineObserved,
            "InstallAttempted": installAttempted,
            "Prerequisites": prerequisites.toJson(),
            "Plan": plan.map { $0.toJson() },
            "Output": output.toJson(),
            "Evidence": evidence.toJson(),
            "Conditions": conditions.map { $0.toJson(timestamp: timestamp) },
            "Diagnostics": diagnostics.map { $0.toJson() }
        ]
        if let engineId {
            json["EngineId"] = engineId
        }
        if let guestSocketPath {
            json["GuestSocketPath"] = guestSocketPath
        }
        return json
    }

    private static func step(_ name: String, _ action: Int, _ detail: String) -> EngineProvisioningPlanStepPayload {
        EngineProvisioningPlanStepPayload(name: name, action: action, detail: detail)
    }

    private static func containsExistingEngineMarker(_ value: String?) -> Bool {
        value?.range(of: "existing-engine", options: .caseInsensitive) != nil
    }

    private static func executionDiagnostic(_ request: EngineProvisioningRequestPayload) -> EngineDiagnosticPayload {
        if request.scriptedExecutionState == 2 {
            return EngineDiagnosticPayload(
                severity: 4,
                code: "AppleVirtualization.EngineProvisioning.Timeout",
                message: "Engine provisioning timed out after \(max(0, request.provisioningTimeoutMilliseconds))ms inside the guest-agent execution path.",
                targetPath: "engine.provisioning.timeout")
        }
        return EngineDiagnosticPayload(
            severity: 4,
            code: "AppleVirtualization.EngineProvisioning.ExecutionFailed",
            message: "Engine provisioning failed inside the guest-agent execution path.",
            targetPath: "engine.provisioning.execution")
    }

    private static func socketPathFor(api: Int, authorityMode: Int) -> String {
        switch api {
        case 2:
            return "/run/containerd/containerd.sock"
        case 1:
            return "/run/user/1000/podman/podman.sock"
        case 4:
            return authorityMode == 0 ? "/run/user/1000/buildkit-default/buildkitd.sock" : "/run/buildkit/buildkitd.sock"
        default:
            return authorityMode == 1 ? "/var/run/docker.sock" : "/run/user/1000/docker.sock"
        }
    }
}
