import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(Virtualization)
import Virtualization
#endif

#if canImport(Security)
import Security
#endif

#if canImport(AppKit)
import AppKit
#endif

#if canImport(Darwin)
@_silgen_name("flock")
private func hpd_flock(
    _ descriptor: Int32,
    _ operation: Int32
) -> Int32

enum DiskLeaseError: LocalizedError {
    case invalidPath
    case duplicatePath
    case openFailed(String, Int32)
    case unsafeFile(String)
    case alreadyOwned(String)

    var errorDescription: String? {
        switch self {
        case .invalidPath:
            return "Every VM disk lease requires one non-empty path."
        case .duplicatePath:
            return "A VM configuration cannot attach the same disk path more than once."
        case .openFailed(let path, let code):
            return "The VM disk '\(path)' could not be opened for exclusive ownership (errno \(code))."
        case .unsafeFile(let path):
            return "The VM disk '\(path)' is not one regular, single-link file."
        case .alreadyOwned(let path):
            return "The VM disk '\(path)' is already owned by another helper or VM incarnation."
        }
    }
}

@_spi(Testing)
public final class ExclusiveDiskLease {
    private var descriptor: Int32

    public init(path: String) throws {
        let disk = Darwin.open(
            path,
            O_RDWR | O_CLOEXEC | O_NOFOLLOW)
        guard disk >= 0 else {
            throw DiskLeaseError.openFailed(
                path,
                errno)
        }
        do {
            var metadata = stat()
            guard Darwin.fstat(disk, &metadata) == 0 else {
                throw DiskLeaseError.openFailed(
                    path,
                    errno)
            }
            guard
                metadata.st_mode & S_IFMT == S_IFREG,
                metadata.st_nlink == 1
            else {
                throw DiskLeaseError.unsafeFile(path)
            }
        } catch {
            Darwin.close(disk)
            throw error
        }
        Darwin.close(disk)

        let leasePath = path + ".hpd-vz.lock"
        let opened = Darwin.open(
            leasePath,
            O_RDWR | O_CREAT | O_CLOEXEC | O_NOFOLLOW,
            S_IRUSR | S_IWUSR)
        guard opened >= 0 else {
            throw DiskLeaseError.openFailed(
                leasePath,
                errno)
        }
        descriptor = opened
        do {
            var leaseMetadata = stat()
            guard Darwin.fstat(opened, &leaseMetadata) == 0 else {
                throw DiskLeaseError.openFailed(
                    leasePath,
                    errno)
            }
            guard
                leaseMetadata.st_mode & S_IFMT == S_IFREG,
                leaseMetadata.st_nlink == 1
            else {
                throw DiskLeaseError.unsafeFile(leasePath)
            }
            guard hpd_flock(
                opened,
                LOCK_EX | LOCK_NB) == 0
            else {
                throw DiskLeaseError.alreadyOwned(path)
            }
        } catch {
            Darwin.close(opened)
            descriptor = -1
            throw error
        }
    }

    deinit {
        if descriptor >= 0 {
            _ = hpd_flock(descriptor, LOCK_UN)
            Darwin.close(descriptor)
        }
    }
}
#endif

public struct GeneratedVirtualMachineIdentity: Codable, Sendable {
    public let schema: String
    public let machineIdentifierData: String
    public let macAddress: String

    public init(
        schema: String,
        machineIdentifierData: String,
        macAddress: String
    ) {
        self.schema = schema
        self.machineIdentifierData = machineIdentifierData
        self.macAddress = macAddress
    }
}

public enum VirtualMachineIdentityFactory {
    public static func generate() throws
        -> GeneratedVirtualMachineIdentity
    {
        #if canImport(Virtualization)
        if #available(macOS 13.0, *) {
            let machineIdentifier = VZGenericMachineIdentifier()
            let macAddress = VZMACAddress.randomLocallyAdministered()
            return GeneratedVirtualMachineIdentity(
                schema:
                    "hpd.execution.apple-virtualization.machine-identity/v1",
                machineIdentifierData:
                    machineIdentifier.dataRepresentation
                        .base64EncodedString(),
                macAddress: macAddress.string)
        }
        throw NSError(
            domain: "HPDVZCore",
            code: 1,
            userInfo: [
                NSLocalizedDescriptionKey:
                    "Persistent generic machine identity requires macOS 13.0 or newer."
            ])
        #else
        throw NSError(
            domain: "HPDVZCore",
            code: 2,
            userInfo: [
                NSLocalizedDescriptionKey:
                    "Virtualization.framework is unavailable."
            ])
        #endif
    }
}

public struct VirtualizationPreflightFact {
    public let name: String
    public let state: PreflightFactState
    public let reason: String
    public let message: String
    public let observedValue: String?
    public let severity: Int

    public init(
        name: String,
        state: PreflightFactState,
        reason: String,
        message: String,
        observedValue: String? = nil,
        severity: Int
    ) {
        self.name = name
        self.state = state
        self.reason = reason
        self.message = message
        self.observedValue = observedValue
        self.severity = severity
    }

    public func toJson() -> [String: Any] {
        var json: [String: Any] = [
            "Name": name,
            "State": state.rawValue,
            "Reason": reason,
            "Message": message,
            "Severity": severity
        ]
        if let observedValue {
            json["ObservedValue"] = observedValue
        }
        return json
    }

    public func toCheck() -> [String: Any] {
        [
            "Name": name,
            "State": preflightCheckState,
            "Severity": severity,
            "Detail": message
        ]
    }

    private var preflightCheckState: Int {
        switch state {
        case .unknown:
            return 0
        case .supported:
            return 1
        case .unsupported:
            return 2
        case .requiresConfiguration:
            return 4
        case .requiresRemediation:
            return 5
        }
    }
}

public struct VirtualizationPreflight {
    public let frameworkAvailable: Bool
    public let virtualizationSupported: Bool
    public let entitlementVerified: Bool
    public let facts: [VirtualizationPreflightFact]

    public init(
        frameworkAvailable: Bool,
        virtualizationSupported: Bool,
        entitlementVerified: Bool,
        facts: [VirtualizationPreflightFact]
    ) {
        self.frameworkAvailable = frameworkAvailable
        self.virtualizationSupported = virtualizationSupported
        self.entitlementVerified = entitlementVerified
        self.facts = facts
    }
}

public protocol VirtualizationAdapter {
    var allowsSyntheticAuthorityFallback: Bool { get }
    func preflight() -> VirtualizationPreflight
    func validateVmConfiguration(_ request: VmConfigurationValidationRequest) -> VmConfigurationValidationResult
    func startHost(_ request: HostLifecycleRequest) -> HostLifecycleResult
    func hostStatus(_ request: HostLifecycleRequest) -> HostLifecycleResult
    func requestStopHost(_ request: HostLifecycleRequest) -> HostLifecycleResult
    func stopHost(_ request: HostLifecycleRequest) -> HostLifecycleResult
    func deleteHost(_ request: HostLifecycleRequest) -> HostLifecycleResult
    func acknowledgeWake(_ request: HostLifecycleRequest) -> HostLifecycleResult
    func powerObservation() -> HostPowerObservation
    func probeGuestAgentTransport(_ request: GuestAgentTransportProbeRequest) -> GuestAgentTransportProbeResult
    func probeGuestAgentReadiness(_ request: GuestAgentReadinessProbeRequest) -> GuestAgentReadinessProbeResult
    func mountProjection(_ request: ProjectionRequest) -> ProjectionResult
    func projectionStatus(_ request: ProjectionRequest) -> ProjectionResult
    func unmountProjection(_ request: ProjectionRequest) -> ProjectionResult
    func observeProjection(_ request: ProjectionRequest) -> ProjectionResult
    func syncProjection(_ request: ProjectionSyncRequest) -> ProjectionSyncResult
    func finalizeProjection(_ request: ProjectionFinalizationRequest) -> ProjectionFinalizationResult
    func enumerateProjectionChanges(_ request: ProjectionChangeEnumerationRequest) -> ProjectionChangeEnumerationResult
    func promoteProjection(_ request: ProjectionPromotionRequest) -> ProjectionPromotionResult
    func networkStatus(_ request: NetworkStatusRequest) -> NetworkStatusResult
    func publishEndpoint(_ request: EndpointPublicationRequest) -> EndpointPublicationResult
    func releaseEndpoint(_ request: EndpointPublicationRequest) -> EndpointPublicationResult
    func engineStatus(_ payload: [String: Any]) -> [String: Any]?
    func storage(_ payload: [String: Any]) -> [String: Any]?
    func authorityBinding(_ payload: [String: Any], operation: Operation) -> [String: Any]?
    func startProcess(_ request: ProcessRequest) -> ProcessResult
    func processStatus(_ request: ProcessRequest) -> ProcessResult
    func waitProcess(_ request: ProcessRequest) -> ProcessResult
    func writeProcessStdin(_ request: ProcessRequest) -> ProcessResult
    func closeProcessStdin(_ request: ProcessRequest) -> ProcessResult
    func signalProcess(_ request: ProcessRequest) -> ProcessResult
    func stopProcess(_ request: ProcessRequest) -> ProcessResult
    func readProcessOutput(_ request: ProcessRequest) -> ProcessResult
}

public enum NetworkAttachmentKind: Int {
    case nat = 0
    case bridged = 1
    case vmnet = 2
    case fileHandle = 3
    case virtioSocket = 4
    case providerDefined = 5
}

public enum NetworkObservationState: Int {
    case unknown = 0
    case ready = 1
    case requiresConfiguration = 2
    case requiresPermission = 3
    case unsupported = 4
    case temporarilyUnavailable = 5
    case failed = 6
}

public enum NetworkCapabilityFlags {
    public static let ipv4 = 1
    public static let natEgress = 4
    public static let peerConnectivity = 8
    public static let tcpPublish = 512
    public static let udpPublish = 1024
    public static let staticAddress = 4096
    public static let staticMacAddress = 8192
    public static let customMtu = 16384
}

public enum NetworkDegradedFeatureRaw {
    public static let peerConnectivity = 1
    public static let tcpPublish = 4
    public static let udpPublish = 5
    public static let staticAddress = 7
    public static let staticMacAddress = 8
    public static let customMtu = 9
}

public enum CapabilityDegradationModeRaw {
    public static let unsupported = 0
    public static let disabledByPolicy = 1
    public static let requiresPermission = 4
}

public struct NetworkStatusRequest {
    public let hostId: String
    public let requestedAttachment: NetworkAttachmentKind
    public let includeGuestObservation: Bool
    public let includeSocketObservation: Bool
    public let explicitRealMode: Bool
    public let maxInterfaces: Int
    public let maxRoutes: Int
    public let maxListeners: Int
    public let scriptedReadinessState: GuestAgentReadinessState?

    public static func parse(from envelope: HelperEnvelope) -> NetworkStatusRequest {
        let payload = envelope.raw["NetworkStatusRequest"] as? [String: Any] ?? [:]
        return NetworkStatusRequest(
            hostId: VmConfigurationValidationRequest.string(payload["HostId"]) ?? "unknown",
            requestedAttachment: NetworkAttachmentKind(rawValue: VmConfigurationValidationRequest.int(payload["RequestedAttachment"]) ?? 0) ?? .nat,
            includeGuestObservation: VmConfigurationValidationRequest.bool(payload["IncludeGuestObservation"]) ?? true,
            includeSocketObservation: VmConfigurationValidationRequest.bool(payload["IncludeSocketObservation"]) ?? true,
            explicitRealMode: VmConfigurationValidationRequest.bool(payload["ExplicitRealMode"]) ?? false,
            maxInterfaces: VmConfigurationValidationRequest.int(payload["MaxInterfaces"]) ?? 16,
            maxRoutes: VmConfigurationValidationRequest.int(payload["MaxRoutes"]) ?? 64,
            maxListeners: VmConfigurationValidationRequest.int(payload["MaxListeners"]) ?? 128,
            scriptedReadinessState: (payload["ScriptedReadinessState"].flatMap { VmConfigurationValidationRequest.int($0) }).flatMap { GuestAgentReadinessState(rawValue: $0) })
    }
}

public struct NetworkLimitationPayload {
    public let feature: Int
    public let mode: Int
    public let reasonCode: String
    public let message: String

    public init(feature: Int, mode: Int, reasonCode: String, message: String) {
        self.feature = feature
        self.mode = mode
        self.reasonCode = reasonCode
        self.message = message
    }

    public func toJson() -> [String: Any] {
        [
            "Feature": feature,
            "Mode": mode,
            "ReasonCode": reasonCode,
            "Message": message
        ]
    }
}

public struct NetworkAttachmentCapabilityFact {
    public let attachmentKind: NetworkAttachmentKind
    public let state: Int
    public let capabilities: Int
    public let configuredAtVmConfigurationTime: Bool
    public let runtimeAttachmentMutable: Bool
    public let requiresEntitlement: Bool
    public let detail: String
    public let limitations: [NetworkLimitationPayload]

    public func toJson() -> [String: Any] {
        [
            "AttachmentKind": attachmentKind.rawValue,
            "State": state,
            "Capabilities": capabilities,
            "ConfiguredAtVmConfigurationTime": configuredAtVmConfigurationTime,
            "RuntimeAttachmentMutable": runtimeAttachmentMutable,
            "RequiresEntitlement": requiresEntitlement,
            "Detail": detail,
            "Limitations": limitations.map { $0.toJson() }
        ]
    }
}

public struct NetworkStatusResult {
    public let hostId: String
    public let state: NetworkObservationState
    public let defaultAttachment: NetworkAttachmentKind
    public let requestedAttachment: NetworkAttachmentKind
    public let realizedCapabilities: Int
    public let discoveryCapabilities: Int
    public let vmRunning: Bool
    public let guestAgentReady: Bool
    public let virtioSocketConfigured: Bool
    public let attachmentCapabilities: [NetworkAttachmentCapabilityFact]
    public let guestNetworkStatus: [String: Any]?
    public let limitations: [NetworkLimitationPayload]
    public let conditions: [[String: Any]]
    public let diagnostics: [[String: Any]]

    public func toJson() -> [String: Any] {
        var json: [String: Any] = [
            "HostId": hostId,
            "State": state.rawValue,
            "DefaultAttachment": defaultAttachment.rawValue,
            "RequestedAttachment": requestedAttachment.rawValue,
            "RealizedCapabilities": realizedCapabilities,
            "DiscoveryCapabilities": discoveryCapabilities,
            "VmRunning": vmRunning,
            "GuestAgentReady": guestAgentReady,
            "VirtioSocketConfigured": virtioSocketConfigured,
            "AttachmentCapabilities": attachmentCapabilities.map { $0.toJson() },
            "Limitations": limitations.map { $0.toJson() },
            "Conditions": conditions,
            "Diagnostics": diagnostics
        ]
        if let guestNetworkStatus {
            json["GuestNetworkStatus"] = guestNetworkStatus
        }
        return json
    }
}

public enum NetworkStatusFactory {
    public static func result(_ request: NetworkStatusRequest, vmRunning: Bool = true) -> NetworkStatusResult {
        let guestReady = request.scriptedReadinessState == nil || request.scriptedReadinessState == .ready
        let state: NetworkObservationState
        switch request.requestedAttachment {
        case .nat:
            state = .ready
        case .bridged:
            state = .requiresPermission
        case .vmnet, .fileHandle:
            state = .requiresConfiguration
        case .virtioSocket:
            state = .ready
        case .providerDefined:
            state = .unsupported
        }

        let defaultCapabilities = NetworkCapabilityFlags.ipv4 | NetworkCapabilityFlags.natEgress
        return NetworkStatusResult(
            hostId: request.hostId,
            state: state,
            defaultAttachment: .nat,
            requestedAttachment: request.requestedAttachment,
            realizedCapabilities: request.requestedAttachment == .nat ? defaultCapabilities : 0,
            discoveryCapabilities: 0,
            vmRunning: vmRunning,
            guestAgentReady: guestReady,
            virtioSocketConfigured: request.includeSocketObservation,
            attachmentCapabilities: attachmentFacts(),
            guestNetworkStatus: request.includeGuestObservation ? guestStatus(request, guestReady: guestReady) : nil,
            limitations: baseLimitations(),
            conditions: [],
            diagnostics: [])
    }

    public static func attachmentFacts() -> [NetworkAttachmentCapabilityFact] {
        [
            NetworkAttachmentCapabilityFact(
                attachmentKind: .nat,
                state: 0,
                capabilities: NetworkCapabilityFlags.ipv4 | NetworkCapabilityFlags.natEgress,
                configuredAtVmConfigurationTime: true,
                runtimeAttachmentMutable: true,
                requiresEntitlement: false,
                detail: "NAT attachment is the default Apple Virtualization network shape; it provides guest egress without HPD endpoint publication.",
                limitations: baseLimitations()),
            NetworkAttachmentCapabilityFact(
                attachmentKind: .bridged,
                state: 3,
                capabilities: NetworkCapabilityFlags.peerConnectivity,
                configuredAtVmConfigurationTime: true,
                runtimeAttachmentMutable: true,
                requiresEntitlement: true,
                detail: "Bridged attachment requires physical interface selection and the com.apple.vm.networking entitlement.",
                limitations: [NetworkLimitationPayload(feature: NetworkDegradedFeatureRaw.peerConnectivity, mode: CapabilityDegradationModeRaw.requiresPermission, reasonCode: "AppleVirtualization.BridgedRequiresEntitlement", message: "Bridged networking is modeled but requires entitlement and explicit configuration.")]),
            NetworkAttachmentCapabilityFact(
                attachmentKind: .vmnet,
                state: 4,
                capabilities: NetworkCapabilityFlags.peerConnectivity,
                configuredAtVmConfigurationTime: true,
                runtimeAttachmentMutable: true,
                requiresEntitlement: true,
                detail: "vmnet attachment is modeled as a future/configured lane and is not claimed available by the helper fake.",
                limitations: [NetworkLimitationPayload(feature: NetworkDegradedFeatureRaw.peerConnectivity, mode: CapabilityDegradationModeRaw.unsupported, reasonCode: "AppleVirtualization.VmnetDeferred", message: "vmnet requires OS and logical network configuration beyond the current provider slice.")]),
            NetworkAttachmentCapabilityFact(
                attachmentKind: .fileHandle,
                state: 3,
                capabilities: 0,
                configuredAtVmConfigurationTime: true,
                runtimeAttachmentMutable: true,
                requiresEntitlement: false,
                detail: "File-handle networking is a data-link datagram socket attachment; HPD must provide and manage the socket and MTU.",
                limitations: [NetworkLimitationPayload(feature: NetworkDegradedFeatureRaw.customMtu, mode: CapabilityDegradationModeRaw.disabledByPolicy, reasonCode: "AppleVirtualization.FileHandleNetworkDeferred", message: "File-handle networking is not treated as a successful default network until explicitly configured.")]),
            NetworkAttachmentCapabilityFact(
                attachmentKind: .virtioSocket,
                state: 0,
                capabilities: 0,
                configuredAtVmConfigurationTime: true,
                runtimeAttachmentMutable: true,
                requiresEntitlement: false,
                detail: "Virtio socket is guest/host port-to-file-descriptor transport and does not imply TCP or UDP endpoint publication.",
                limitations: [])
        ]
    }

    public static func baseLimitations() -> [NetworkLimitationPayload] {
        [
            NetworkLimitationPayload(feature: NetworkDegradedFeatureRaw.staticAddress, mode: CapabilityDegradationModeRaw.unsupported, reasonCode: "AppleVirtualization.StaticAddressNotAssignedByVz", message: "Static guest address assignment is not provided by the Apple Virtualization attachment contract and must be handled by guest policy later."),
            NetworkLimitationPayload(feature: NetworkDegradedFeatureRaw.staticMacAddress, mode: CapabilityDegradationModeRaw.disabledByPolicy, reasonCode: "AppleVirtualization.StaticMacDeferred", message: "Static MAC requests remain policy-gated until provider orchestration owns VM network configuration."),
            NetworkLimitationPayload(feature: NetworkDegradedFeatureRaw.customMtu, mode: CapabilityDegradationModeRaw.disabledByPolicy, reasonCode: "AppleVirtualization.CustomMtuDeferred", message: "Custom MTU is only meaningful for file-handle networking and is deferred until that lane is explicitly configured.")
        ]
    }

    private static func guestStatus(_ request: NetworkStatusRequest, guestReady: Bool) -> [String: Any] {
        [
            "HostId": request.hostId,
            "GuestAgentReady": guestReady,
            "Interfaces": [
                [
                    "Name": "en0",
                    "Mtu": 1500,
                    "IsUp": true,
                    "Addresses": [
                        [
                            "Address": ["Family": 0, "HighBits": 0, "LowBits": 167772162],
                            "PrefixLength": 24,
                            "Kind": 3,
                            "IsPrimary": true
                        ]
                    ]
                ]
            ],
            "Routes": request.maxRoutes == 0 ? [] : [
                [
                    "Gateway": ["Family": 0, "HighBits": 0, "LowBits": 167772161],
                    "InterfaceName": "en0",
                    "IsDefault": true
                ]
            ],
            "Listeners": request.maxListeners == 0 ? [] : [
                [
                    "Name": "guest-observed-tcp-listener",
                    "Transport": 0,
                    "Address": ["Family": 0, "HighBits": 0, "LowBits": 167772162],
                    "Port": ["Value": 8080],
                    "GuestVisibleOnly": true,
                    "HpdPublished": false
                ]
            ],
            "InterfacesTruncated": false,
            "RoutesTruncated": false,
            "ListenersTruncated": false,
            "Generation": [
                "GuestBootId": "guest-boot-1",
                "GuestBootGeneration": 1,
                "GuestAgentGeneration": 1
            ],
            "Limitations": baseLimitations().map { $0.toJson() },
            "Conditions": []
        ]
    }
}

public struct EndpointPublicationRequest {
    public let endpointId: String
    public let action: Int
    public let listenerKind: Int
    public let transport: Int
    public let exposureScope: Int
    public let listenerAddress: String
    public let requestedPort: UInt16?
    public let allowEphemeralPort: Bool
    public let requireStableListener: Bool
    public let targetKind: Int
    public let targetResourceId: String?
    public let targetAddress: String?
    public let targetPort: UInt16?
    public let targetSocketPath: String?
    public let reconcileRouteOnTargetRestart: Bool
    public let requireRouteHealth: Bool
    public let scriptedRouteHealthy: Bool

    public static func parse(from envelope: HelperEnvelope) -> EndpointPublicationRequest {
        let payload = envelope.raw["EndpointPublicationRequest"] as? [String: Any] ?? [:]
        return EndpointPublicationRequest(
            endpointId: VmConfigurationValidationRequest.string(payload["EndpointId"]) ?? "unknown",
            action: VmConfigurationValidationRequest.int(payload["Action"]) ?? 0,
            listenerKind: VmConfigurationValidationRequest.int(payload["ListenerKind"]) ?? 0,
            transport: VmConfigurationValidationRequest.int(payload["Transport"]) ?? 0,
            exposureScope: VmConfigurationValidationRequest.int(payload["ExposureScope"]) ?? 0,
            listenerAddress: VmConfigurationValidationRequest.string(payload["ListenerAddress"]) ?? "127.0.0.1",
            requestedPort: VmConfigurationValidationRequest.int(payload["RequestedPort"]).map { UInt16(clamping: $0) },
            allowEphemeralPort: VmConfigurationValidationRequest.bool(payload["AllowEphemeralPort"]) ?? false,
            requireStableListener: VmConfigurationValidationRequest.bool(payload["RequireStableListener"]) ?? false,
            targetKind: VmConfigurationValidationRequest.int(payload["TargetKind"]) ?? 0,
            targetResourceId: VmConfigurationValidationRequest.string(payload["TargetResourceId"]),
            targetAddress: VmConfigurationValidationRequest.string(payload["TargetAddress"]),
            targetPort: VmConfigurationValidationRequest.int(payload["TargetPort"]).map { UInt16(clamping: $0) },
            targetSocketPath: VmConfigurationValidationRequest.string(payload["TargetSocketPath"]),
            reconcileRouteOnTargetRestart: VmConfigurationValidationRequest.bool(payload["ReconcileRouteOnTargetRestart"]) ?? true,
            requireRouteHealth: VmConfigurationValidationRequest.bool(payload["RequireRouteHealth"]) ?? true,
            scriptedRouteHealthy: VmConfigurationValidationRequest.bool(payload["ScriptedRouteHealthy"]) ?? true)
    }
}

public struct EndpointPublicationResult {
    public let endpointId: String
    public let endpointPhase: Int
    public let listenerKind: Int
    public let transport: Int
    public let exposureScope: Int
    public let boundAddress: String?
    public let boundPort: UInt16?
    public let hpdOwned: Bool
    public let routeHealthy: Bool
    public let resolvedAddress: String?
    public let resolvedPort: UInt16?
    public let resolvedSocketPath: String?
    public let limitations: [NetworkLimitationPayload]
    public let diagnostics: [[String: Any]]

    public func toJson() -> [String: Any] {
        var json: [String: Any] = [
            "EndpointId": endpointId,
            "EndpointPhase": endpointPhase,
            "ListenerKind": listenerKind,
            "Transport": transport,
            "ExposureScope": exposureScope,
            "HpdOwned": hpdOwned,
            "RouteHealthy": routeHealthy,
            "Limitations": limitations.map { $0.toJson() },
            "Conditions": [],
            "Diagnostics": diagnostics
        ]
        if let boundAddress { json["BoundAddress"] = boundAddress }
        if let boundPort { json["BoundPort"] = Int(boundPort) }
        if let resolvedAddress { json["ResolvedAddress"] = resolvedAddress }
        if let resolvedPort { json["ResolvedPort"] = Int(resolvedPort) }
        if let resolvedSocketPath { json["ResolvedSocketPath"] = resolvedSocketPath }
        return json
    }
}

public enum EndpointPublicationStateFactory {
    public static func publish(_ request: EndpointPublicationRequest) -> EndpointPublicationResult {
        let supported = request.listenerKind == 0 && request.transport == 0 && request.exposureScope == 0
        let routeHealthy = supported && (!request.requireRouteHealth || request.scriptedRouteHealthy)
        let boundPort = request.requestedPort ?? (request.allowEphemeralPort ? 49152 : nil)
        let phase = supported && routeHealthy && boundPort != nil ? 2 : supported ? 3 : 4
        let limitations: [NetworkLimitationPayload] = supported && routeHealthy
            ? []
            : [NetworkLimitationPayload(
                feature: 9,
                mode: supported ? 2 : 0,
                reasonCode: supported ? "AppleVirtualization.EndpointRouteUnhealthy" : "AppleVirtualization.EndpointUnsupported",
                message: supported
                    ? "The fake helper did not verify a healthy HPD-owned route."
                    : "The fake helper only models host-local TCP endpoint publication.")]

        return EndpointPublicationResult(
            endpointId: request.endpointId,
            endpointPhase: phase,
            listenerKind: request.listenerKind,
            transport: request.transport,
            exposureScope: request.exposureScope,
            boundAddress: supported ? request.listenerAddress : nil,
            boundPort: supported ? boundPort : nil,
            hpdOwned: supported,
            routeHealthy: routeHealthy,
            resolvedAddress: request.targetAddress,
            resolvedPort: request.targetPort,
            resolvedSocketPath: request.targetSocketPath,
            limitations: limitations,
            diagnostics: [])
    }

    public static func release(_ request: EndpointPublicationRequest) -> EndpointPublicationResult {
        EndpointPublicationResult(
            endpointId: request.endpointId,
            endpointPhase: 6,
            listenerKind: request.listenerKind,
            transport: request.transport,
            exposureScope: request.exposureScope,
            boundAddress: nil,
            boundPort: nil,
            hpdOwned: false,
            routeHealthy: false,
            resolvedAddress: request.targetAddress,
            resolvedPort: request.targetPort,
            resolvedSocketPath: request.targetSocketPath,
            limitations: [],
            diagnostics: [])
    }
}

public enum HostLifecycleState: Int {
    case notCreated = 0
    case starting = 1
    case running = 2
    case stopping = 3
    case stopped = 4
    case failed = 5
}

package enum HostLifecycleObservationDecision {
    package static func reconcile(
        current: HostLifecycleState,
        observed: HostLifecycleState
    ) -> HostLifecycleState {
        // VZVirtualMachine remains `running` while a guest-initiated shutdown
        // drains services. Once HPDOS has accepted an identity-bound shutdown,
        // only a terminal Apple observation may advance that lifecycle. A
        // transient `running` observation must not cancel the stopping fence.
        if current == .stopping && observed == .running {
            return .stopping
        }
        return observed
    }
}

package enum HostDeletionGenerationDecision: Equatable {
    case current
    case stale

    package static func evaluate(recordGeneration: UInt64, requestGeneration: UInt64) -> Self {
        recordGeneration == requestGeneration ? .current : .stale
    }
}

package enum HostStartGenerationDecision: Equatable {
    case current
    case stale

    package static func evaluate(recordGeneration: UInt64, requestGeneration: UInt64) -> Self {
        recordGeneration == requestGeneration ? .current : .stale
    }
}

package enum HostStartLifecycleDecision: Equatable {
    case reuse
    case replace
    case reject

    package static func evaluate(
        state: HostLifecycleState,
        recordGeneration: UInt64,
        requestGeneration: UInt64
    ) -> Self {
        switch state {
        case .running, .starting:
            return requestGeneration == recordGeneration ? .reuse : .reject
        case .stopping:
            return .reject
        case .stopped, .failed, .notCreated:
            guard recordGeneration < UInt64.max else {
                return .reject
            }
            return requestGeneration == recordGeneration + 1 ? .replace : .reject
        }
    }
}

public struct EngineHostRouteState {
    public let running: Bool
    public let providerGeneration: UInt64
    public let socketAvailable: Bool

    public init(running: Bool, providerGeneration: UInt64, socketAvailable: Bool) {
        self.running = running
        self.providerGeneration = providerGeneration
        self.socketAvailable = socketAvailable
    }
}

public enum EngineHostRouteResolution: Equatable {
    case resolved(hostId: String)
    case unknownHost
    case stopped
    case staleProviderGeneration
    case socketMissing
}

public enum EngineHostRouter {
    public static func resolve(
        hostId: String,
        providerGeneration: UInt64,
        hosts: [String: EngineHostRouteState]
    ) -> EngineHostRouteResolution {
        guard let host = hosts[hostId] else {
            return .unknownHost
        }
        guard host.providerGeneration == providerGeneration else {
            return .staleProviderGeneration
        }
        guard host.running else {
            return .stopped
        }
        guard host.socketAvailable else {
            return .socketMissing
        }
        return .resolved(hostId: hostId)
    }
}

public enum EngineResponseIdentityValidation: Equatable {
    case valid
    case hostMismatch
    case engineMismatch
    case generationMismatch
}

public enum EngineResponseIdentityValidator {
    public static func validate(
        response: [String: Any],
        hostId: String,
        engineId: String,
        providerGeneration: UInt64,
        hostStartGeneration: UInt64
    ) -> EngineResponseIdentityValidation {
        guard VmConfigurationValidationRequest.string(response["HostId"]) == hostId,
              let engineStatus = response["EngineStatusResponse"] as? [String: Any],
              VmConfigurationValidationRequest.string(engineStatus["HostId"]) == hostId,
              let guestEngineStatus = engineStatus["GuestEngineStatus"] as? [String: Any],
              VmConfigurationValidationRequest.string(guestEngineStatus["HostId"]) == hostId else {
            return .hostMismatch
        }
        guard VmConfigurationValidationRequest.string(engineStatus["EngineId"]) == engineId,
              VmConfigurationValidationRequest.string(guestEngineStatus["EngineId"]) == engineId else {
            return .engineMismatch
        }
        guard let generation = guestEngineStatus["Generation"] as? [String: Any],
              VmConfigurationValidationRequest.uint64(generation["ProviderGeneration"]) == providerGeneration,
              VmConfigurationValidationRequest.uint64(generation["HostStartGeneration"]) == hostStartGeneration else {
            return .generationMismatch
        }
        return .valid
    }
}

public enum HostPowerState: Int, Sendable {
    case active = 0
    case sleeping = 1
    case wakeReconciliationRequired = 2
    case terminating = 3
}

public struct HostPowerObservation: Sendable {
    public let state: HostPowerState
    public let sleepGeneration: UInt64
    public let wakeGeneration: UInt64
    public let requiresWakeReconciliation: Bool
    public let observedAt: String

    public init(
        state: HostPowerState,
        sleepGeneration: UInt64,
        wakeGeneration: UInt64,
        requiresWakeReconciliation: Bool,
        observedAt: String
    ) {
        self.state = state
        self.sleepGeneration = sleepGeneration
        self.wakeGeneration = wakeGeneration
        self.requiresWakeReconciliation =
            requiresWakeReconciliation
        self.observedAt = observedAt
    }

    public static let active = HostPowerObservation(
        state: .active,
        sleepGeneration: 0,
        wakeGeneration: 0,
        requiresWakeReconciliation: false,
        observedAt: ISO8601DateFormatter()
            .string(from: Date()))
}

@_spi(Testing)
public final class HostPowerMonitor: @unchecked Sendable {
    private let lock = NSLock()
    private var state: HostPowerState = .active
    private var sleepGeneration: UInt64 = 0
    private var wakeGeneration: UInt64 = 0
    private var requiresWakeReconciliation = false
    private var observedAt = ISO8601DateFormatter().string(from: Date())
    #if canImport(AppKit)
    private var observers: [NSObjectProtocol] = []
    #endif

    public init(registerSystemNotifications: Bool = true) {
        #if canImport(AppKit)
        guard registerSystemNotifications else {
            return
        }
        let center = NSWorkspace.shared.notificationCenter
        observers = [
            center.addObserver(
                forName: NSWorkspace.willSleepNotification,
                object: nil,
                queue: nil
            ) { [weak self] _ in
                self?.recordSleep()
            },
            center.addObserver(
                forName: NSWorkspace.didWakeNotification,
                object: nil,
                queue: nil
            ) { [weak self] _ in
                self?.recordWake()
            },
            center.addObserver(
                forName: NSWorkspace.willPowerOffNotification,
                object: nil,
                queue: nil
            ) { [weak self] _ in
                self?.recordTermination()
            }
        ]
        #endif
    }

    deinit {
        #if canImport(AppKit)
        let center = NSWorkspace.shared.notificationCenter
        for observer in observers {
            center.removeObserver(observer)
        }
        #endif
    }

    public func snapshot() -> HostPowerObservation {
        lock.lock()
        defer { lock.unlock() }
        return HostPowerObservation(
            state: state,
            sleepGeneration: sleepGeneration,
            wakeGeneration: wakeGeneration,
            requiresWakeReconciliation: requiresWakeReconciliation,
            observedAt: observedAt)
    }

    public func acknowledge(wakeGeneration observedGeneration: UInt64) -> Bool {
        lock.lock()
        defer { lock.unlock() }
        guard state == .wakeReconciliationRequired,
              requiresWakeReconciliation,
              observedGeneration == wakeGeneration else {
            return false
        }
        state = .active
        requiresWakeReconciliation = false
        observedAt = Self.now()
        return true
    }

    public func simulateSleepForTesting() {
        recordSleep()
    }

    public func simulateWakeForTesting() {
        recordWake()
    }

    public func simulateTerminationForTesting() {
        recordTermination()
    }

    private func recordSleep() {
        lock.lock()
        defer { lock.unlock() }
        sleepGeneration &+= 1
        state = .sleeping
        requiresWakeReconciliation = true
        observedAt = Self.now()
    }

    private func recordWake() {
        lock.lock()
        defer { lock.unlock() }
        wakeGeneration &+= 1
        state = .wakeReconciliationRequired
        requiresWakeReconciliation = true
        observedAt = Self.now()
    }

    private func recordTermination() {
        lock.lock()
        defer { lock.unlock() }
        state = .terminating
        requiresWakeReconciliation = true
        observedAt = Self.now()
    }

    private static func now() -> String {
        ISO8601DateFormatter().string(from: Date())
    }
}

public struct HostLifecycleResult {
    public let hostId: String
    public let state: HostLifecycleState
    public let accepted: Bool
    public let diagnostics: [VmConfigurationValidationDiagnostic]
    public let powerObservation: HostPowerObservation

    public init(
        hostId: String,
        state: HostLifecycleState,
        accepted: Bool,
        diagnostics: [VmConfigurationValidationDiagnostic] = [],
        powerObservation: HostPowerObservation = .active
    ) {
        self.hostId = hostId
        self.state = state
        self.accepted = accepted
        self.diagnostics = diagnostics
        self.powerObservation = powerObservation
    }
}

public struct HostLifecycleRequest {
    public let hostId: String
    public let providerGeneration: UInt64
    public let hostStartGeneration: UInt64
    public let explicitRealMode: Bool
    public let reason: String?
    public let gracePeriodMilliseconds: Int?
    public let observedWakeGeneration: UInt64?
    public let vmConfiguration: VmConfigurationValidationRequest?

    public static func parse(from envelope: HelperEnvelope) -> HostLifecycleRequest {
        let payload = envelope.raw["HostLifecycleRequest"] as? [String: Any] ?? [:]
        let configurationPayload = payload["VmConfigurationValidationRequest"] as? [String: Any]
        return HostLifecycleRequest(
            hostId: VmConfigurationValidationRequest.string(payload["HostId"]) ?? "unknown",
            providerGeneration: VmConfigurationValidationRequest.uint64(envelope.raw["ProviderGeneration"]) ?? 0,
            hostStartGeneration: VmConfigurationValidationRequest.uint64(payload["HostStartGeneration"]) ?? 0,
            explicitRealMode: VmConfigurationValidationRequest.bool(payload["ExplicitRealMode"]) ?? false,
            reason: VmConfigurationValidationRequest.string(payload["Reason"]),
            gracePeriodMilliseconds: VmConfigurationValidationRequest.int(payload["GracePeriodMilliseconds"]),
            observedWakeGeneration:
                VmConfigurationValidationRequest.uint64(
                    payload["ObservedWakeGeneration"]),
            vmConfiguration: configurationPayload.map { configuration in
                VmConfigurationValidationRequest.parse(from: HelperEnvelope(raw: [
                    "ProtocolVersion": HelperProtocol.currentVersion,
                    "Operation": Operation.vmConfigurationValidate.rawValue,
                    "VmConfigurationValidationRequest": configuration
                ]))
            }
        )
    }
}

public enum GuestAgentTransportState: Int {
    case notAttempted = 0
    case waitingForVmRunning = 1
    case connecting = 2
    case connected = 3
    case refused = 4
    case timeout = 5
    case unsupported = 6
    case failed = 7
}

public enum GuestAgentTransportKind: Int {
    case virtioSocket = 0
    case serialPort = 1
    case network = 2
    case providerDefined = 3
}

public let DefaultGuestAgentVirtioSocketPort: UInt32 = 7_777

public struct GuestAgentTransportEndpoint {
    public let kind: GuestAgentTransportKind
    public let port: UInt32?
    public let address: String?
    public let name: String?

    public static func parse(_ value: Any?) -> GuestAgentTransportEndpoint {
        let payload = value as? [String: Any] ?? [:]
        let rawKind = VmConfigurationValidationRequest.int(payload["Kind"]) ?? GuestAgentTransportKind.virtioSocket.rawValue
        return GuestAgentTransportEndpoint(
            kind: GuestAgentTransportKind(rawValue: rawKind) ?? .virtioSocket,
            port: VmConfigurationValidationRequest.uint64(payload["Port"]).map { UInt32(truncatingIfNeeded: $0) } ?? DefaultGuestAgentVirtioSocketPort,
            address: VmConfigurationValidationRequest.string(payload["Address"]),
            name: VmConfigurationValidationRequest.string(payload["Name"])
        )
    }

    public func toJson() -> [String: Any] {
        var json: [String: Any] = ["Kind": kind.rawValue]
        if let port {
            json["Port"] = port
        }
        if let address {
            json["Address"] = address
        }
        if let name {
            json["Name"] = name
        }
        return json
    }
}

public struct GuestAgentTransportProbeRequest {
    public let hostId: String
    public let endpoint: GuestAgentTransportEndpoint
    public let timeoutMilliseconds: Int
    public let explicitRealMode: Bool
    public let requireVmRunning: Bool
    public let scriptedStatus: GuestAgentTransportState?

    public static func parse(from envelope: HelperEnvelope) -> GuestAgentTransportProbeRequest {
        let payload = envelope.raw["GuestAgentTransportProbeRequest"] as? [String: Any] ?? [:]
        let scripted = VmConfigurationValidationRequest.int(payload["ScriptedStatus"])
            .flatMap { GuestAgentTransportState(rawValue: $0) }
        return GuestAgentTransportProbeRequest(
            hostId: VmConfigurationValidationRequest.string(payload["HostId"]) ?? "unknown",
            endpoint: GuestAgentTransportEndpoint.parse(payload["Endpoint"]),
            timeoutMilliseconds: VmConfigurationValidationRequest.int(payload["TimeoutMilliseconds"]) ?? 1_000,
            explicitRealMode: VmConfigurationValidationRequest.bool(payload["ExplicitRealMode"]) ?? false,
            requireVmRunning: VmConfigurationValidationRequest.bool(payload["RequireVmRunning"]) ?? true,
            scriptedStatus: scripted
        )
    }
}

public struct GuestAgentTransportProbeResult {
    public let hostId: String
    public let state: GuestAgentTransportState
    public let endpoint: GuestAgentTransportEndpoint
    public let vmRunning: Bool
    public let reason: String
    public let message: String
    public let diagnostic: VmConfigurationValidationDiagnostic?

    public init(
        hostId: String,
        state: GuestAgentTransportState,
        endpoint: GuestAgentTransportEndpoint,
        vmRunning: Bool,
        reason: String,
        message: String,
        diagnostic: VmConfigurationValidationDiagnostic? = nil
    ) {
        self.hostId = hostId
        self.state = state
        self.endpoint = endpoint
        self.vmRunning = vmRunning
        self.reason = reason
        self.message = message
        self.diagnostic = diagnostic
    }
}

public enum GuestAgentReadinessState: Int {
    case notAttempted = 0
    case transportNotConnected = 1
    case handshaking = 2
    case ready = 3
    case notReady = 4
    case incompatibleProtocol = 5
    case incompatibleAgentVersion = 6
    case missingCapability = 7
    case malformedFrame = 8
    case guestAgentError = 9
    case disconnected = 10
    case timeout = 11
    case failed = 12
}

public struct GuestAgentCapabilities: Sendable {
    public let processStart: Bool
    public let processStdin: Bool
    public let processSignal: Bool
    public let processStop: Bool
    public let processReadOutput: Bool
    public let pty: Bool
    public let processResize: Bool
    public let projectionMount: Bool
    public let projectionObserve: Bool
    public let projectionSync: Bool
    public let projectionFinalize: Bool
    public let projectionPromote: Bool
    public let authorityProjection: Bool
    public let authorityRevocation: Bool
    public let engineStatus: Bool
    public let engineProvisioning: Bool
    public let limitations: [String]

    public static let firstSlice = GuestAgentCapabilities(
        processStart: true,
        processStdin: true,
        processSignal: true,
        processStop: true,
        processReadOutput: true,
        pty: false,
        processResize: false,
        projectionMount: true,
        projectionObserve: false,
        projectionSync: false,
        projectionFinalize: false,
        projectionPromote: false,
        authorityProjection: false,
        authorityRevocation: false,
        engineStatus: false,
        engineProvisioning: false,
        limitations: []
    )

    public func contains(_ capability: String) -> Bool {
        switch capability {
        case "process.start": return processStart
        case "process.stdin": return processStdin
        case "process.signal": return processSignal
        case "process.stop": return processStop
        case "process.readOutput": return processReadOutput
        case "process.resize": return processResize
        case "projection.mount": return projectionMount
        case "projection.observe": return projectionObserve
        case "projection.sync": return projectionSync
        case "projection.finalize": return projectionFinalize
        case "projection.promote": return projectionPromote
        case "authority.bind", "authority.projection": return authorityProjection
        case "authority.revoke", "authority.revocation": return authorityRevocation
        case "engine.status": return engineStatus
        case "engine.provision", "engine.provisioning": return engineProvisioning
        case "pty": return pty
        default: return false
        }
    }

    public func toJson() -> [String: Any] {
        [
            "ProcessStart": processStart,
            "ProcessStdin": processStdin,
            "ProcessSignal": processSignal,
            "ProcessStop": processStop,
            "ProcessReadOutput": processReadOutput,
            "Pty": pty,
            "ProcessResize": processResize,
            "ProjectionMount": projectionMount,
            "ProjectionObserve": projectionObserve,
            "ProjectionSync": projectionSync,
            "ProjectionFinalize": projectionFinalize,
            "ProjectionPromote": projectionPromote,
            "AuthorityProjection": authorityProjection,
            "AuthorityRevocation": authorityRevocation,
            "EngineStatus": engineStatus,
            "EngineProvisioning": engineProvisioning,
            "Limitations": limitations
        ]
    }
}

public struct GuestAgentReadinessProbeRequest {
    public let hostId: String
    public let endpoint: GuestAgentTransportEndpoint
    public let timeoutMilliseconds: Int
    public let explicitRealMode: Bool
    public let expectedProtocolVersion: String
    public let expectedAgentVersion: String?
    public let expectedRuntimeFilesystemUuid: String?
    public let expectedAppDataFilesystemUuid: String?
    public let requiredCapabilities: [String]
    public let hostUtcUnixMilliseconds: Int64?
    public let maximumClockSkewMilliseconds: Int
    public let correctGuestClock: Bool
    public let scriptedState: GuestAgentReadinessState?

    public static func parse(from envelope: HelperEnvelope) -> GuestAgentReadinessProbeRequest {
        let payload = envelope.raw["GuestAgentReadinessProbeRequest"] as? [String: Any] ?? [:]
        let scripted = VmConfigurationValidationRequest.int(payload["ScriptedState"])
            .flatMap { GuestAgentReadinessState(rawValue: $0) }
        return GuestAgentReadinessProbeRequest(
            hostId: VmConfigurationValidationRequest.string(payload["HostId"]) ?? "unknown",
            endpoint: GuestAgentTransportEndpoint.parse(payload["Endpoint"]),
            timeoutMilliseconds: VmConfigurationValidationRequest.int(payload["TimeoutMilliseconds"]) ?? 1_000,
            explicitRealMode: VmConfigurationValidationRequest.bool(payload["ExplicitRealMode"]) ?? false,
            expectedProtocolVersion: VmConfigurationValidationRequest.string(payload["ExpectedProtocolVersion"]) ?? "1.0",
            expectedAgentVersion: VmConfigurationValidationRequest.string(payload["ExpectedAgentVersion"]),
            expectedRuntimeFilesystemUuid: VmConfigurationValidationRequest.string(payload["ExpectedRuntimeFilesystemUuid"]),
            expectedAppDataFilesystemUuid: VmConfigurationValidationRequest.string(payload["ExpectedAppDataFilesystemUuid"]),
            requiredCapabilities: payload["RequiredCapabilities"] as? [String] ?? [],
            hostUtcUnixMilliseconds:
                VmConfigurationValidationRequest.int(
                    payload["HostUtcUnixMilliseconds"])
                    .map(Int64.init),
            maximumClockSkewMilliseconds:
                VmConfigurationValidationRequest.int(
                    payload["MaximumClockSkewMilliseconds"]) ?? 5_000,
            correctGuestClock:
                VmConfigurationValidationRequest.bool(
                    payload["CorrectGuestClock"]) ?? false,
            scriptedState: scripted
        )
    }
}

public struct GuestAgentReadinessProbeResult {
    public let hostId: String
    public let state: GuestAgentReadinessState
    public let transportState: GuestAgentTransportState
    public let endpoint: GuestAgentTransportEndpoint
    public let vmRunning: Bool
    public let transportConnected: Bool
    public let verifiedReady: Bool
    public let protocolVersion: String?
    public let agentVersion: String?
    public let guestBootId: String?
    public let guestBootGeneration: UInt64
    public let guestAgentGeneration: UInt64
    public let runtimeFilesystemUuid: String?
    public let appDataFilesystemUuid: String?
    public let capabilities: GuestAgentCapabilities?
    public let missingCapabilities: [String]
    public let reason: String
    public let message: String
    public let diagnostic: VmConfigurationValidationDiagnostic?
}

private struct GuestAgentHandshakeResult {
    let state: GuestAgentReadinessState
    let transportState: GuestAgentTransportState
    let transportConnected: Bool
    let protocolVersion: String?
    let agentVersion: String?
    let guestBootId: String?
    let guestBootGeneration: UInt64
    let guestAgentGeneration: UInt64
    let runtimeFilesystemUuid: String?
    let appDataFilesystemUuid: String?
    let capabilities: GuestAgentCapabilities?
    let reason: String
    let message: String
    let diagnostic: VmConfigurationValidationDiagnostic?
}

public enum ProjectionScriptedGuestState: Int {
    case notReady = 0
    case hostConfiguredOnly = 1
    case frameworkAcceptedOnly = 2
    case verified = 3
    case accessMismatch = 4
    case coherenceUnknown = 5
    case coherenceDegraded = 6
    case malformedResponse = 7
    case notFound = 8
}

public struct ProjectionRequest {
    public let projectionId: String
    public let hostId: String
    public let hostPath: String?
    public let tag: String
    public let guestPath: String
    public let accessMode: Int
    public let realization: Int
    public let requestedWriteEffect: Int
    public let requestedCoherence: Int
    public let force: Bool
    public let recursive: Bool
    public let afterSequence: Int64?
    public let limit: Int?
    public let scriptedReadinessState: GuestAgentReadinessState?
    public let scriptedGuestProjectionState: ProjectionScriptedGuestState?

    public static func parse(from envelope: HelperEnvelope) -> ProjectionRequest {
        let operation = envelope.operation
        let payload: [String: Any]
        switch operation {
        case .projectionMount:
            payload = envelope.raw["ProjectionMountRequest"] as? [String: Any] ?? [:]
        case .projectionStatus:
            payload = envelope.raw["ProjectionStatusRequest"] as? [String: Any] ?? [:]
        case .projectionUnmount:
            payload = envelope.raw["ProjectionUnmountRequest"] as? [String: Any] ?? [:]
        case .projectionObserve:
            payload = envelope.raw["ProjectionObserveRequest"] as? [String: Any] ?? [:]
        default:
            payload = [:]
        }

        let scriptedReadiness = VmConfigurationValidationRequest.int(payload["ScriptedReadinessState"])
            .flatMap { GuestAgentReadinessState(rawValue: $0) }
        let scriptedProjection = VmConfigurationValidationRequest.int(payload["ScriptedGuestProjectionState"])
            .flatMap { ProjectionScriptedGuestState(rawValue: $0) }

        return ProjectionRequest(
            projectionId: VmConfigurationValidationRequest.string(payload["ProjectionId"]) ?? "unknown-projection",
            hostId: VmConfigurationValidationRequest.string(payload["HostId"]) ?? "unknown-host",
            hostPath: VmConfigurationValidationRequest.string(payload["HostPath"]),
            tag: VmConfigurationValidationRequest.string(payload["Tag"]) ?? "unknown-tag",
            guestPath: VmConfigurationValidationRequest.string(payload["GuestPath"])
                ?? VmConfigurationValidationRequest.string(payload["ExpectedGuestPath"])
                ?? "/hpd/projections/unknown",
            accessMode: VmConfigurationValidationRequest.int(payload["AccessMode"]) ?? 0,
            realization: VmConfigurationValidationRequest.int(payload["Realization"]) ?? 1,
            requestedWriteEffect: VmConfigurationValidationRequest.int(payload["RequestedWriteEffect"]) ?? 0,
            requestedCoherence: VmConfigurationValidationRequest.int(payload["RequestedCoherence"]) ?? 0,
            force: VmConfigurationValidationRequest.bool(payload["Force"]) ?? false,
            recursive: VmConfigurationValidationRequest.bool(payload["Recursive"]) ?? true,
            afterSequence: VmConfigurationValidationRequest.uint64(payload["AfterSequence"]).map { Int64(truncatingIfNeeded: $0) },
            limit: VmConfigurationValidationRequest.int(payload["Limit"]),
            scriptedReadinessState: scriptedReadiness,
            scriptedGuestProjectionState: scriptedProjection
        )
    }
}

public struct ProjectionResult {
    public let projectionId: String
    public let phase: Int
    public let effectiveRealization: Int
    public let effectiveWriteEffect: Int
    public let effectiveCoherence: Int
    public let guestAgentReady: Bool
    public let hostShareConfigured: Bool
    public let frameworkShareAccepted: Bool
    public let verifiedByGuestAgent: Bool
    public let guestStatus: [String: Any]?
    public let unmountResult: [String: Any]?
    public let observeResult: [String: Any]?
    public let diagnostic: VmConfigurationValidationDiagnostic?
}

public struct ProjectionLifecycleGeneration {
    public let projectionGeneration: UInt64

    public static func parse(_ value: Any?) -> ProjectionLifecycleGeneration {
        let payload = value as? [String: Any] ?? [:]
        return ProjectionLifecycleGeneration(
            projectionGeneration: VmConfigurationValidationRequest.uint64(payload["ProjectionGeneration"]) ?? 0)
    }
}

public struct ProjectionSyncRequest {
    public let projectionId: String
    public let hostId: String
    public let guestPath: String
    public let mode: Int
    public let direction: Int
    public let conflictPolicy: Int
    public let dryRun: Bool
    public let maxChanges: Int
    public let maxConflicts: Int
    public let generation: ProjectionLifecycleGeneration
    public let scriptedReadinessState: GuestAgentReadinessState?
    public let scriptedGuestProjectionState: ProjectionScriptedGuestState?

    public static func parse(from envelope: HelperEnvelope) -> ProjectionSyncRequest {
        let payload = envelope.raw["ProjectionSyncRequest"] as? [String: Any] ?? [:]
        let scriptedReadiness = VmConfigurationValidationRequest.int(payload["ScriptedReadinessState"])
            .flatMap { GuestAgentReadinessState(rawValue: $0) }
        let scriptedProjection = VmConfigurationValidationRequest.int(payload["ScriptedGuestProjectionState"])
            .flatMap { ProjectionScriptedGuestState(rawValue: $0) }
        return ProjectionSyncRequest(
            projectionId: VmConfigurationValidationRequest.string(payload["ProjectionId"]) ?? "unknown-projection",
            hostId: VmConfigurationValidationRequest.string(payload["HostId"]) ?? "unknown-host",
            guestPath: VmConfigurationValidationRequest.string(payload["GuestPath"]) ?? "/hpd/projections/unknown",
            mode: VmConfigurationValidationRequest.int(payload["Mode"]) ?? 2,
            direction: VmConfigurationValidationRequest.int(payload["Direction"]) ?? 2,
            conflictPolicy: VmConfigurationValidationRequest.int(payload["ConflictPolicy"]) ?? 1,
            dryRun: VmConfigurationValidationRequest.bool(payload["DryRun"]) ?? false,
            maxChanges: VmConfigurationValidationRequest.int(payload["MaxChanges"]) ?? 1024,
            maxConflicts: VmConfigurationValidationRequest.int(payload["MaxConflicts"]) ?? 128,
            generation: ProjectionLifecycleGeneration.parse(payload["Generation"]),
            scriptedReadinessState: scriptedReadiness,
            scriptedGuestProjectionState: scriptedProjection)
    }
}

public struct ProjectionFinalizationRequest {
    public let projectionId: String
    public let hostId: String
    public let guestPath: String
    public let kind: Int
    public let includeProvenance: Bool
    public let includeDeletedEntries: Bool
    public let producerId: String?
    public let maxContentRefs: Int
    public let maxConflicts: Int
    public let generation: ProjectionLifecycleGeneration
    public let scriptedReadinessState: GuestAgentReadinessState?
    public let scriptedGuestProjectionState: ProjectionScriptedGuestState?

    public static func parse(from envelope: HelperEnvelope) -> ProjectionFinalizationRequest {
        let payload = envelope.raw["ProjectionFinalizationRequest"] as? [String: Any] ?? [:]
        let scriptedReadiness = VmConfigurationValidationRequest.int(payload["ScriptedReadinessState"])
            .flatMap { GuestAgentReadinessState(rawValue: $0) }
        let scriptedProjection = VmConfigurationValidationRequest.int(payload["ScriptedGuestProjectionState"])
            .flatMap { ProjectionScriptedGuestState(rawValue: $0) }
        return ProjectionFinalizationRequest(
            projectionId: VmConfigurationValidationRequest.string(payload["ProjectionId"]) ?? "unknown-projection",
            hostId: VmConfigurationValidationRequest.string(payload["HostId"]) ?? "unknown-host",
            guestPath: VmConfigurationValidationRequest.string(payload["GuestPath"]) ?? "/hpd/projections/unknown",
            kind: VmConfigurationValidationRequest.int(payload["Kind"]) ?? 2,
            includeProvenance: VmConfigurationValidationRequest.bool(payload["IncludeProvenance"]) ?? true,
            includeDeletedEntries: VmConfigurationValidationRequest.bool(payload["IncludeDeletedEntries"]) ?? true,
            producerId: VmConfigurationValidationRequest.string(payload["ProducerId"]),
            maxContentRefs: VmConfigurationValidationRequest.int(payload["MaxContentRefs"]) ?? 1024,
            maxConflicts: VmConfigurationValidationRequest.int(payload["MaxConflicts"]) ?? 128,
            generation: ProjectionLifecycleGeneration.parse(payload["Generation"]),
            scriptedReadinessState: scriptedReadiness,
            scriptedGuestProjectionState: scriptedProjection)
    }
}

public struct ProjectionChangeEnumerationRequest {
    public let projectionId: String
    public let hostId: String
    public let guestPath: String
    public let afterSequence: Int64?
    public let limit: Int
    public let includeDeletedEntries: Bool
    public let generation: ProjectionLifecycleGeneration
    public let scriptedReadinessState: GuestAgentReadinessState?
    public let scriptedGuestProjectionState: ProjectionScriptedGuestState?

    public static func parse(from envelope: HelperEnvelope) -> ProjectionChangeEnumerationRequest {
        let payload = envelope.raw["ProjectionChangeEnumerationRequest"] as? [String: Any] ?? [:]
        let scriptedReadiness = VmConfigurationValidationRequest.int(payload["ScriptedReadinessState"])
            .flatMap { GuestAgentReadinessState(rawValue: $0) }
        let scriptedProjection = VmConfigurationValidationRequest.int(payload["ScriptedGuestProjectionState"])
            .flatMap { ProjectionScriptedGuestState(rawValue: $0) }
        return ProjectionChangeEnumerationRequest(
            projectionId: VmConfigurationValidationRequest.string(payload["ProjectionId"]) ?? "unknown-projection",
            hostId: VmConfigurationValidationRequest.string(payload["HostId"]) ?? "unknown-host",
            guestPath: VmConfigurationValidationRequest.string(payload["GuestPath"]) ?? "/hpd/projections/unknown",
            afterSequence: VmConfigurationValidationRequest.uint64(payload["AfterSequence"]).map { Int64(truncatingIfNeeded: $0) },
            limit: VmConfigurationValidationRequest.int(payload["Limit"]) ?? 1024,
            includeDeletedEntries: VmConfigurationValidationRequest.bool(payload["IncludeDeletedEntries"]) ?? true,
            generation: ProjectionLifecycleGeneration.parse(payload["Generation"]),
            scriptedReadinessState: scriptedReadiness,
            scriptedGuestProjectionState: scriptedProjection)
    }
}

public struct ProjectionPromotionRequest {
    public let projectionId: String
    public let hostId: String
    public let guestPath: String
    public let direction: Int
    public let conflictPolicy: Int
    public let dryRun: Bool
    public let maxChanges: Int
    public let maxConflicts: Int
    public let generation: ProjectionLifecycleGeneration
    public let scriptedReadinessState: GuestAgentReadinessState?
    public let scriptedGuestProjectionState: ProjectionScriptedGuestState?

    public static func parse(from envelope: HelperEnvelope) -> ProjectionPromotionRequest {
        let payload = envelope.raw["ProjectionPromotionRequest"] as? [String: Any] ?? [:]
        let scriptedReadiness = VmConfigurationValidationRequest.int(payload["ScriptedReadinessState"])
            .flatMap { GuestAgentReadinessState(rawValue: $0) }
        let scriptedProjection = VmConfigurationValidationRequest.int(payload["ScriptedGuestProjectionState"])
            .flatMap { ProjectionScriptedGuestState(rawValue: $0) }
        return ProjectionPromotionRequest(
            projectionId: VmConfigurationValidationRequest.string(payload["ProjectionId"]) ?? "unknown-projection",
            hostId: VmConfigurationValidationRequest.string(payload["HostId"]) ?? "unknown-host",
            guestPath: VmConfigurationValidationRequest.string(payload["GuestPath"]) ?? "/hpd/projections/unknown",
            direction: VmConfigurationValidationRequest.int(payload["Direction"]) ?? 2,
            conflictPolicy: VmConfigurationValidationRequest.int(payload["ConflictPolicy"]) ?? 4,
            dryRun: VmConfigurationValidationRequest.bool(payload["DryRun"]) ?? false,
            maxChanges: VmConfigurationValidationRequest.int(payload["MaxChanges"]) ?? 1024,
            maxConflicts: VmConfigurationValidationRequest.int(payload["MaxConflicts"]) ?? 128,
            generation: ProjectionLifecycleGeneration.parse(payload["Generation"]),
            scriptedReadinessState: scriptedReadiness,
            scriptedGuestProjectionState: scriptedProjection)
    }
}

public struct ProjectionSyncResult {
    public let projectionId: String
    public let state: Int
    public let succeeded: Bool
    public let dryRun: Bool
    public let checkpointVersion: Int64
    public let changeSummary: [String: Any]
    public let changes: [[String: Any]]
    public let conflicts: [[String: Any]]
    public let changesTruncated: Bool
    public let conflictsTruncated: Bool
    public let unsupportedReason: String?
    public let diagnostic: VmConfigurationValidationDiagnostic?

    public func toJson() -> [String: Any] {
        var json: [String: Any] = [
            "ProjectionId": projectionId,
            "State": state,
            "Succeeded": succeeded,
            "DryRun": dryRun,
            "CheckpointVersion": checkpointVersion,
            "CompletedAt": ProjectionContentStateFactory.isoTimestamp(),
            "ChangeSummary": changeSummary,
            "Changes": changes,
            "Conflicts": conflicts,
            "ChangesTruncated": changesTruncated,
            "ConflictsTruncated": conflictsTruncated,
            "Conditions": [],
            "Diagnostics": diagnostic.map { [$0.toJson()] } ?? []
        ]
        if let unsupportedReason {
            json["UnsupportedReason"] = unsupportedReason
        }
        return json
    }
}

public struct ProjectionFinalizationResult {
    public let projectionId: String
    public let state: Int
    public let succeeded: Bool
    public let manifestDigest: [String: Any]?
    public let content: [[String: Any]]
    public let conflicts: [[String: Any]]
    public let contentTruncated: Bool
    public let conflictsTruncated: Bool
    public let unsupportedReason: String?
    public let diagnostic: VmConfigurationValidationDiagnostic?

    public func toJson() -> [String: Any] {
        var json: [String: Any] = [
            "ProjectionId": projectionId,
            "State": state,
            "Succeeded": succeeded,
            "CompletedAt": ProjectionContentStateFactory.isoTimestamp(),
            "Content": content,
            "Conflicts": conflicts,
            "ContentTruncated": contentTruncated,
            "ConflictsTruncated": conflictsTruncated,
            "Conditions": [],
            "Diagnostics": diagnostic.map { [$0.toJson()] } ?? []
        ]
        if let manifestDigest {
            json["ManifestDigest"] = manifestDigest
        }
        if let unsupportedReason {
            json["UnsupportedReason"] = unsupportedReason
        }
        return json
    }
}

public struct ProjectionChangeEnumerationResult {
    public let projectionId: String
    public let changes: [[String: Any]]
    public let nextSequence: Int64?
    public let hasMore: Bool
    public let truncated: Bool

    public func toJson() -> [String: Any] {
        var json: [String: Any] = [
            "ProjectionId": projectionId,
            "Changes": changes,
            "HasMore": hasMore,
            "Truncated": truncated,
            "Conditions": []
        ]
        if let nextSequence {
            json["NextSequence"] = nextSequence
        }
        return json
    }
}

public struct ProjectionPromotionResult {
    public let projectionId: String
    public let state: Int
    public let succeeded: Bool
    public let dryRun: Bool
    public let changeSummary: [String: Any]
    public let changes: [[String: Any]]
    public let conflicts: [[String: Any]]
    public let changesTruncated: Bool
    public let conflictsTruncated: Bool
    public let unsupportedReason: String?
    public let diagnostic: VmConfigurationValidationDiagnostic?

    public func toJson() -> [String: Any] {
        var json: [String: Any] = [
            "ProjectionId": projectionId,
            "State": state,
            "Succeeded": succeeded,
            "DryRun": dryRun,
            "ChangeSummary": changeSummary,
            "Changes": changes,
            "Conflicts": conflicts,
            "ChangesTruncated": changesTruncated,
            "ConflictsTruncated": conflictsTruncated,
            "Conditions": []
        ]
        if let unsupportedReason {
            json["UnsupportedReason"] = unsupportedReason
        }
        if let diagnostic {
            json["Conditions"] = []
            json["Diagnostics"] = [diagnostic.toJson()]
        }
        return json
    }
}

public enum ProjectionStateFactory {
    public static func result(_ request: ProjectionRequest, operation: Operation) -> ProjectionResult {
        let state = request.scriptedGuestProjectionState ?? .hostConfiguredOnly
        guard request.scriptedReadinessState == .ready else {
            return notReady(request, diagnostic: VmConfigurationValidationDiagnostic(
                code: "AppleVirtualization.GuestAgentNotReady",
                message: "Projection \(operation.wireName) requires guest-agent readiness.",
                targetPath: "projection.guestAgentReady",
                severity: 3))
        }

        if state == .malformedResponse {
            return ProjectionResult(
                projectionId: request.projectionId,
                phase: 7,
                effectiveRealization: request.realization,
                effectiveWriteEffect: 0,
                effectiveCoherence: 0,
                guestAgentReady: true,
                hostShareConfigured: true,
                frameworkShareAccepted: true,
                verifiedByGuestAgent: false,
                guestStatus: nil,
                unmountResult: nil,
                observeResult: nil,
                diagnostic: VmConfigurationValidationDiagnostic(
                    code: "AppleVirtualization.GuestProjectionMalformedResponse",
                    message: "Fake guest-agent projection response was malformed.",
                    targetPath: "guestAgent.projection"))
        }

        let guestStatus = status(request, state: state)
        let verified = state == .verified
        var unmountResult: [String: Any]? = nil
        var observeResult: [String: Any]? = nil
        if operation == .projectionUnmount {
            unmountResult = [
                "ProjectionId": request.projectionId,
                "Unmounted": state != .notFound,
                "WasMounted": verified,
                "Detail": state == .notFound ? "Projection was not mounted in the guest." : "Projection unmount is idempotent in fake helper mode."
            ]
        }
        if operation == .projectionObserve {
            observeResult = [
                "ProjectionId": request.projectionId,
                "Status": guestStatus,
                "Events": [],
                "HasMore": false
            ]
        }

        return ProjectionResult(
            projectionId: request.projectionId,
            phase: verified ? 2 : (state == .notFound ? 8 : 4),
            effectiveRealization: request.realization,
            effectiveWriteEffect: state == .verified ? request.requestedWriteEffect : 0,
            effectiveCoherence: state == .verified ? 3 : (state == .coherenceUnknown ? 0 : request.requestedCoherence),
            guestAgentReady: true,
            hostShareConfigured: true,
            frameworkShareAccepted: state != .hostConfiguredOnly,
            verifiedByGuestAgent: verified,
            guestStatus: guestStatus,
            unmountResult: unmountResult,
            observeResult: observeResult,
            diagnostic: diagnostic(for: state))
    }

    public static func notReady(_ request: ProjectionRequest, diagnostic: VmConfigurationValidationDiagnostic?) -> ProjectionResult {
        ProjectionResult(
            projectionId: request.projectionId,
            phase: 1,
            effectiveRealization: request.realization,
            effectiveWriteEffect: 0,
            effectiveCoherence: 0,
            guestAgentReady: false,
            hostShareConfigured: true,
            frameworkShareAccepted: true,
            verifiedByGuestAgent: false,
            guestStatus: status(request, state: .notReady),
            unmountResult: nil,
            observeResult: nil,
            diagnostic: diagnostic)
    }

    private static func status(_ request: ProjectionRequest, state: ProjectionScriptedGuestState) -> [String: Any] {
        let verified = state == .verified
        let frameworkAccepted = state != .hostConfiguredOnly
        let verificationState: Int
        switch state {
        case .notReady: verificationState = 0
        case .hostConfiguredOnly: verificationState = 1
        case .frameworkAcceptedOnly: verificationState = 2
        case .verified: verificationState = 6
        case .accessMismatch: verificationState = 9
        case .coherenceUnknown: verificationState = 10
        case .coherenceDegraded: verificationState = 11
        case .malformedResponse: verificationState = 12
        case .notFound: verificationState = 8
        }

        return [
            "ProjectionId": request.projectionId,
            "GuestPath": request.guestPath,
            "Tag": request.tag,
            "Mounted": verified,
            "GuestMountVerified": verified,
            "HostShareState": 2,
            "FrameworkShareState": frameworkAccepted ? 2 : 1,
            "VerificationState": verificationState,
            "ExpectedGuestPath": request.guestPath,
            "ActualGuestPath": verified ? request.guestPath : NSNull(),
            "RequestedAccessMode": request.accessMode,
            "EffectiveAccessMode": state == .accessMismatch ? 0 : request.accessMode,
            "ProjectionPhase": verified ? 2 : 4,
            "EffectiveRealization": request.realization,
            "EffectiveWriteEffect": verified ? request.requestedWriteEffect : 0,
            "EffectiveCoherence": verified ? 3 : (state == .coherenceUnknown ? 0 : request.requestedCoherence),
            "EffectiveCache": 0,
            "Generation": [
                "ProviderGeneration": 0,
                "HostStartGeneration": 0,
                "GuestBootId": "guest-boot-1",
                "GuestBootGeneration": 1,
                "GuestAgentGeneration": 1,
                "ProjectionGeneration": 1
            ],
            "Limitations": [],
            "Conditions": []
        ]
    }

    private static func diagnostic(for state: ProjectionScriptedGuestState) -> VmConfigurationValidationDiagnostic? {
        switch state {
        case .accessMismatch:
            return VmConfigurationValidationDiagnostic(code: "AppleVirtualization.ProjectionAccessMismatch", message: "Guest projection effective access does not match requested access.", targetPath: "guestAgent.projection.access", severity: 3)
        case .coherenceUnknown, .coherenceDegraded:
            return VmConfigurationValidationDiagnostic(code: "AppleVirtualization.ProjectionCoherenceUnverified", message: "Guest projection coherence is not verified as ready for HPD use.", targetPath: "guestAgent.projection.coherence", severity: 3)
        case .notFound:
            return VmConfigurationValidationDiagnostic(code: "AppleVirtualization.ProjectionNotFound", message: "Guest projection was not found.", targetPath: "guestAgent.projection", severity: 3)
        default:
            return nil
        }
    }
}

public enum ProjectionContentStateFactory {
    public static func syncResult(_ request: ProjectionSyncRequest) -> ProjectionSyncResult {
        if let diagnostic = readinessDiagnostic(request.scriptedReadinessState, operation: "projection.sync") {
            return syncFailure(request, state: 7, diagnostic: diagnostic)
        }

        if let diagnostic = projectionDiagnostic(
            request.scriptedGuestProjectionState,
            generation: request.generation,
            operation: "projection.sync")
        {
            return syncFailure(request, state: projectionFailureState(diagnostic), diagnostic: diagnostic)
        }

        if request.mode == 0 || request.mode == 4 {
            return ProjectionSyncResult(
                projectionId: request.projectionId,
                state: 3,
                succeeded: false,
                dryRun: request.dryRun,
                checkpointVersion: 0,
                changeSummary: emptyChangeSummary(),
                changes: [],
                conflicts: [],
                changesTruncated: false,
                conflictsTruncated: false,
                unsupportedReason: "UnsupportedMode",
                diagnostic: nil)
        }

        if request.direction == 0 || request.direction == 3 {
            return ProjectionSyncResult(
                projectionId: request.projectionId,
                state: 4,
                succeeded: false,
                dryRun: request.dryRun,
                checkpointVersion: 0,
                changeSummary: emptyChangeSummary(),
                changes: [],
                conflicts: [],
                changesTruncated: false,
                conflictsTruncated: false,
                unsupportedReason: "UnsupportedDirection",
                diagnostic: nil)
        }

        if request.conflictPolicy == 2 || request.conflictPolicy == 3 {
            return ProjectionSyncResult(
                projectionId: request.projectionId,
                state: 5,
                succeeded: false,
                dryRun: request.dryRun,
                checkpointVersion: 0,
                changeSummary: emptyChangeSummary(),
                changes: [],
                conflicts: [],
                changesTruncated: false,
                conflictsTruncated: false,
                unsupportedReason: "UnsupportedConflictPolicy",
                diagnostic: nil)
        }

        let bounded = boundedChanges(limit: request.maxChanges)
        let boundedConflicts = boundedConflicts(limit: request.maxConflicts)
        return ProjectionSyncResult(
            projectionId: request.projectionId,
            state: request.dryRun ? 2 : 1,
            succeeded: true,
            dryRun: request.dryRun,
            checkpointVersion: request.dryRun ? 0 : 1,
            changeSummary: changeSummary(created: 1, modified: 1, deleted: 1, conflicted: 1),
            changes: bounded.items,
            conflicts: boundedConflicts.items,
            changesTruncated: bounded.truncated,
            conflictsTruncated: boundedConflicts.truncated,
            unsupportedReason: nil,
            diagnostic: nil)
    }

    public static func finalizationResult(_ request: ProjectionFinalizationRequest) -> ProjectionFinalizationResult {
        if let diagnostic = readinessDiagnostic(request.scriptedReadinessState, operation: "projection.finalize") {
            return finalizationFailure(request, state: 5, diagnostic: diagnostic)
        }

        if let diagnostic = projectionDiagnostic(
            request.scriptedGuestProjectionState,
            generation: request.generation,
            operation: "projection.finalize")
        {
            return finalizationFailure(request, state: projectionFailureState(diagnostic), diagnostic: diagnostic)
        }

        if request.kind == 4 || request.kind == 5 || request.kind == 6 {
            return ProjectionFinalizationResult(
                projectionId: request.projectionId,
                state: 2,
                succeeded: false,
                manifestDigest: nil,
                content: [],
                conflicts: [],
                contentTruncated: false,
                conflictsTruncated: false,
                unsupportedReason: "UnsupportedKind",
                diagnostic: nil)
        }

        let boundedContent = boundedContent(limit: request.maxContentRefs, includeDeletedEntries: request.includeDeletedEntries)
        let boundedConflicts = boundedConflicts(limit: request.maxConflicts)
        return ProjectionFinalizationResult(
            projectionId: request.projectionId,
            state: 1,
            succeeded: true,
            manifestDigest: digest("sha256", "fake-helper-manifest"),
            content: boundedContent.items,
            conflicts: boundedConflicts.items,
            contentTruncated: boundedContent.truncated,
            conflictsTruncated: boundedConflicts.truncated,
            unsupportedReason: nil,
            diagnostic: nil)
    }

    public static func changeEnumerationResult(_ request: ProjectionChangeEnumerationRequest) -> ProjectionChangeEnumerationResult {
        if readinessDiagnostic(request.scriptedReadinessState, operation: "projection.enumerateChanges") != nil ||
            projectionDiagnostic(request.scriptedGuestProjectionState, generation: request.generation, operation: "projection.enumerateChanges") != nil
        {
            return ProjectionChangeEnumerationResult(
                projectionId: request.projectionId,
                changes: [],
                nextSequence: nil,
                hasMore: false,
                truncated: false)
        }

        let bounded = boundedChanges(limit: request.limit)
        return ProjectionChangeEnumerationResult(
            projectionId: request.projectionId,
            changes: bounded.items,
            nextSequence: bounded.items.last?["Sequence"] as? Int64,
            hasMore: bounded.truncated,
            truncated: bounded.truncated)
    }

    public static func promotionResult(_ request: ProjectionPromotionRequest) -> ProjectionPromotionResult {
        if let diagnostic = readinessDiagnostic(request.scriptedReadinessState, operation: "projection.promote") {
            return promotionFailure(request, state: 5, diagnostic: diagnostic)
        }

        if let diagnostic = projectionDiagnostic(
            request.scriptedGuestProjectionState,
            generation: request.generation,
            operation: "projection.promote")
        {
            return promotionFailure(request, state: projectionFailureState(diagnostic), diagnostic: diagnostic)
        }

        if request.conflictPolicy != 4 || request.direction == 0 || request.direction == 3 {
            return ProjectionPromotionResult(
                projectionId: request.projectionId,
                state: 3,
                succeeded: false,
                dryRun: request.dryRun,
                changeSummary: emptyChangeSummary(),
                changes: [],
                conflicts: [],
                changesTruncated: false,
                conflictsTruncated: false,
                unsupportedReason: "Unsupported",
                diagnostic: nil)
        }

        let bounded = boundedChanges(limit: request.maxChanges)
        let boundedConflicts = boundedConflicts(limit: request.maxConflicts)
        return ProjectionPromotionResult(
            projectionId: request.projectionId,
            state: request.dryRun ? 2 : 1,
            succeeded: true,
            dryRun: request.dryRun,
            changeSummary: changeSummary(created: 1, modified: 1, deleted: 1, conflicted: 1),
            changes: bounded.items,
            conflicts: boundedConflicts.items,
            changesTruncated: bounded.truncated,
            conflictsTruncated: boundedConflicts.truncated,
            unsupportedReason: nil,
            diagnostic: nil)
    }

    public static func isoTimestamp() -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: Date())
    }

    private static func syncFailure(
        _ request: ProjectionSyncRequest,
        state: Int,
        diagnostic: VmConfigurationValidationDiagnostic
    ) -> ProjectionSyncResult {
        ProjectionSyncResult(
            projectionId: request.projectionId,
            state: state,
            succeeded: false,
            dryRun: request.dryRun,
            checkpointVersion: 0,
            changeSummary: emptyChangeSummary(),
            changes: [],
            conflicts: [],
            changesTruncated: false,
            conflictsTruncated: false,
            unsupportedReason: nil,
            diagnostic: diagnostic)
    }

    private static func finalizationFailure(
        _ request: ProjectionFinalizationRequest,
        state: Int,
        diagnostic: VmConfigurationValidationDiagnostic
    ) -> ProjectionFinalizationResult {
        ProjectionFinalizationResult(
            projectionId: request.projectionId,
            state: state,
            succeeded: false,
            manifestDigest: nil,
            content: [],
            conflicts: [],
            contentTruncated: false,
            conflictsTruncated: false,
            unsupportedReason: nil,
            diagnostic: diagnostic)
    }

    private static func promotionFailure(
        _ request: ProjectionPromotionRequest,
        state: Int,
        diagnostic: VmConfigurationValidationDiagnostic
    ) -> ProjectionPromotionResult {
        ProjectionPromotionResult(
            projectionId: request.projectionId,
            state: state,
            succeeded: false,
            dryRun: request.dryRun,
            changeSummary: emptyChangeSummary(),
            changes: [],
            conflicts: [],
            changesTruncated: false,
            conflictsTruncated: false,
            unsupportedReason: nil,
            diagnostic: diagnostic)
    }

    private static func readinessDiagnostic(_ state: GuestAgentReadinessState?, operation: String) -> VmConfigurationValidationDiagnostic? {
        guard state == .ready else {
            return VmConfigurationValidationDiagnostic(
                code: "AppleVirtualization.GuestAgentNotReady",
                message: "\(operation) requires verified guest-agent readiness.",
                targetPath: "projection.guestAgentReady",
                severity: 3)
        }

        return nil
    }

    private static func projectionDiagnostic(
        _ state: ProjectionScriptedGuestState?,
        generation: ProjectionLifecycleGeneration,
        operation: String
    ) -> VmConfigurationValidationDiagnostic? {
        guard state == .verified else {
            return VmConfigurationValidationDiagnostic(
                code: "AppleVirtualization.ProjectionNotVerified",
                message: "\(operation) requires a guest-verified projection.",
                targetPath: "projection.verification",
                severity: 4)
        }

        if generation.projectionGeneration != 0 && generation.projectionGeneration != 1 {
            return VmConfigurationValidationDiagnostic(
                code: "AppleVirtualization.ProjectionStaleGeneration",
                message: "\(operation) rejected a stale projection generation.",
                targetPath: "projection.generation",
                severity: 4)
        }

        return nil
    }

    private static func projectionFailureState(_ diagnostic: VmConfigurationValidationDiagnostic) -> Int {
        diagnostic.code == "AppleVirtualization.ProjectionStaleGeneration" ? 7 : 6
    }

    private static func boundedChanges(limit: Int) -> (items: [[String: Any]], truncated: Bool) {
        let changes: [[String: Any]] = [
            ["Sequence": Int64(1), "Kind": 0, "Path": "/workspace/created.txt", "Size": ["Value": 11], "Digest": digest("sha256", "created"), "Deleted": false, "Role": 0],
            ["Sequence": Int64(2), "Kind": 1, "Path": "/workspace/modified.txt", "Size": ["Value": 17], "Digest": digest("sha256", "modified"), "Deleted": false, "Role": 0],
            ["Sequence": Int64(3), "Kind": 2, "Path": "/workspace/deleted.txt", "Size": ["Value": 0], "Deleted": true, "Role": 0]
        ]
        return bound(changes, limit: limit)
    }

    private static func boundedConflicts(limit: Int) -> (items: [[String: Any]], truncated: Bool) {
        bound([
            ["Path": "/workspace/conflict.txt", "Kind": 0, "Description": "Fake helper conflict for protocol verification."],
            ["Path": "/workspace/conflict-2.txt", "Kind": 3, "Description": "Second fake helper conflict for bounds verification."]
        ], limit: limit)
    }

    private static func boundedContent(limit: Int, includeDeletedEntries: Bool) -> (items: [[String: Any]], truncated: Bool) {
        var refs: [[String: Any]] = [
            ["Path": "/workspace/created.txt", "ContentId": "content-created", "Digest": digest("sha256", "created"), "Size": ["Value": 11], "Role": 0],
            ["Path": "/workspace/modified.txt", "ContentId": "content-modified", "Digest": digest("sha256", "modified"), "Size": ["Value": 17], "Role": 0]
        ]
        if includeDeletedEntries {
            refs.append(["Path": "/workspace/deleted.txt", "ContentId": "content-deleted", "Size": ["Value": 0], "Role": 0])
        }
        return bound(refs, limit: limit)
    }

    private static func bound(_ items: [[String: Any]], limit: Int) -> (items: [[String: Any]], truncated: Bool) {
        let boundedLimit = max(0, min(limit, items.count))
        return (Array(items.prefix(boundedLimit)), items.count > boundedLimit)
    }

    private static func changeSummary(created: Int, modified: Int, deleted: Int, conflicted: Int) -> [String: Any] {
        [
            "Created": created,
            "Modified": modified,
            "Deleted": deleted,
            "Conflicted": conflicted,
            "ManifestDigest": digest("sha256", "fake-helper-manifest")
        ]
    }

    private static func emptyChangeSummary() -> [String: Any] {
        [
            "Created": 0,
            "Modified": 0,
            "Deleted": 0,
            "Conflicted": 0
        ]
    }

    private static func digest(_ algorithm: String, _ value: String) -> [String: Any] {
        ["Algorithm": algorithm, "Value": value]
    }
}

public struct ProcessRequest {
    public let processId: String
    public let unitId: String?
    public let hostId: String?
    public let providerGeneration: UInt64
    public let hostStartGeneration: UInt64
    public let guestBootId: String?
    public let guestBootGeneration: UInt64
    public let guestAgentGeneration: UInt64
    public let fileName: String?
    public let arguments: [String]
    public let workingDirectory: String?
    public let environment: [String: String]
    public let mergeStandardError: Bool
    public let io: [String: Any]
    public let policy: [String: Any]
    public let isolation: [String: Any]
    public let sandboxPlan: [String: Any]
    public let timeoutMilliseconds: Int?
    public let requiredProjectionId: String?
    public let requiredProjectionGuestPath: String?
    public let requireVerifiedProjection: Bool
    public let afterOutputSequence: Int64?
    public let outputLimit: Int?
    public let stdinBytesBase64: String?
    public let stdinSequence: Int64
    public let closeAfterWrite: Bool
    public let signalName: String?
    public let stopKind: Int?
    public let stopReason: String?
    public let scriptedReadinessState: GuestAgentReadinessState?
    public let scriptedGuestProjectionState: ProjectionScriptedGuestState?

    public static func parse(from envelope: HelperEnvelope) -> ProcessRequest {
        let operation = envelope.operation
        let payload: [String: Any]
        switch operation {
        case .processStart:
            payload = envelope.raw["ProcessStartRequest"] as? [String: Any] ?? [:]
        case .processStdin:
            payload = envelope.raw["ProcessStdinRequest"] as? [String: Any] ?? [:]
        case .processCloseStdin:
            payload = envelope.raw["ProcessStdinRequest"] as? [String: Any] ??
                envelope.raw["ProcessCloseStdinRequest"] as? [String: Any] ?? [:]
        case .processSignal:
            payload = envelope.raw["ProcessSignalRequest"] as? [String: Any] ?? [:]
        case .processStop:
            payload = envelope.raw["ProcessStopRequest"] as? [String: Any] ?? [:]
        case .processStatus, .processWait, .processReadOutput:
            payload = envelope.raw["ProcessLifecycleRequest"] as? [String: Any] ?? [:]
        default:
            payload = [:]
        }

        let scriptedReadiness = VmConfigurationValidationRequest.int(payload["ScriptedReadinessState"])
            .flatMap { GuestAgentReadinessState(rawValue: $0) }
        let scriptedProjection = VmConfigurationValidationRequest.int(payload["ScriptedGuestProjectionState"])
            .flatMap { ProjectionScriptedGuestState(rawValue: $0) }
        let signalPayload = payload["Signal"] as? [String: Any]
        let command = payload["Command"] as? [String: Any] ?? [:]
        let io = payload["Io"] as? [String: Any] ?? [:]
        let hostRoute = envelope.raw["ProcessHost"] as? [String: Any] ?? [:]

        return ProcessRequest(
            processId: VmConfigurationValidationRequest.string(payload["ProcessId"]) ?? "unknown-process",
            unitId: VmConfigurationValidationRequest.string(payload["UnitId"]),
            hostId: VmConfigurationValidationRequest.string(hostRoute["HostId"]),
            providerGeneration: VmConfigurationValidationRequest.uint64(envelope.raw["ProviderGeneration"]) ?? 0,
            hostStartGeneration: VmConfigurationValidationRequest.uint64(hostRoute["HostStartGeneration"]) ?? 0,
            guestBootId: VmConfigurationValidationRequest.string(hostRoute["GuestBootId"]),
            guestBootGeneration: VmConfigurationValidationRequest.uint64(hostRoute["GuestBootGeneration"]) ?? 0,
            guestAgentGeneration: VmConfigurationValidationRequest.uint64(hostRoute["GuestAgentGeneration"]) ?? 0,
            fileName: VmConfigurationValidationRequest.string(command["FileName"]),
            arguments: Self.stringArray(command["Arguments"]),
            workingDirectory: VmConfigurationValidationRequest.string(command["WorkingDirectory"]),
            environment: Self.stringDictionary(command["Environment"]),
            mergeStandardError: VmConfigurationValidationRequest.bool(io["MergeStandardError"]) ?? false,
            io: io,
            policy: payload["Policy"] as? [String: Any] ?? [:],
            isolation: payload["Isolation"] as? [String: Any] ?? [:],
            sandboxPlan: payload["SandboxPlan"] as? [String: Any] ?? [:],
            timeoutMilliseconds: Self.milliseconds(payload["Timeout"]),
            requiredProjectionId: VmConfigurationValidationRequest.string(payload["RequiredProjectionId"]),
            requiredProjectionGuestPath: VmConfigurationValidationRequest.string(payload["RequiredProjectionGuestPath"]),
            requireVerifiedProjection: VmConfigurationValidationRequest.bool(payload["RequireVerifiedProjection"]) ?? false,
            afterOutputSequence: VmConfigurationValidationRequest.uint64(payload["AfterOutputSequence"]).map { Int64(truncatingIfNeeded: $0) },
            outputLimit: VmConfigurationValidationRequest.int(payload["OutputLimit"]),
            stdinBytesBase64: VmConfigurationValidationRequest.string(payload["Bytes"]),
            stdinSequence: VmConfigurationValidationRequest.uint64(payload["Sequence"]).map { Int64(truncatingIfNeeded: $0) } ?? 0,
            closeAfterWrite: VmConfigurationValidationRequest.bool(payload["CloseAfterWrite"]) ?? false,
            signalName: VmConfigurationValidationRequest.string(signalPayload?["Name"]),
            stopKind: VmConfigurationValidationRequest.int(payload["Kind"]),
            stopReason: VmConfigurationValidationRequest.string(payload["Reason"]),
            scriptedReadinessState: scriptedReadiness,
            scriptedGuestProjectionState: scriptedProjection
        )
    }

    public func withScriptedReadinessState(_ state: GuestAgentReadinessState) -> ProcessRequest {
        ProcessRequest(
            processId: processId,
            unitId: unitId,
            hostId: hostId,
            providerGeneration: providerGeneration,
            hostStartGeneration: hostStartGeneration,
            guestBootId: guestBootId,
            guestBootGeneration: guestBootGeneration,
            guestAgentGeneration: guestAgentGeneration,
            fileName: fileName,
            arguments: arguments,
            workingDirectory: workingDirectory,
            environment: environment,
            mergeStandardError: mergeStandardError,
            io: io,
            policy: policy,
            isolation: isolation,
            sandboxPlan: sandboxPlan,
            timeoutMilliseconds: timeoutMilliseconds,
            requiredProjectionId: requiredProjectionId,
            requiredProjectionGuestPath: requiredProjectionGuestPath,
            requireVerifiedProjection: requireVerifiedProjection,
            afterOutputSequence: afterOutputSequence,
            outputLimit: outputLimit,
            stdinBytesBase64: stdinBytesBase64,
            stdinSequence: stdinSequence,
            closeAfterWrite: closeAfterWrite,
            signalName: signalName,
            stopKind: stopKind,
            stopReason: stopReason,
            scriptedReadinessState: state,
            scriptedGuestProjectionState: scriptedGuestProjectionState)
    }

    public func toGuestPayload(operation: Operation) -> [String: Any] {
        var payload: [String: Any] = [
            "ProtocolVersion": HelperProtocol.currentVersion,
            "MessageType": 0,
            "Operation": operation.rawValue,
            "RequestId": "guest-process-\(processId)-\(UUID().uuidString)",
            "SequenceNumber": 1,
            "ProcessId": processId,
            "HostId": hostId ?? "",
            "ProviderGeneration": providerGeneration,
            "HostStartGeneration": hostStartGeneration,
            "GuestBootGeneration": guestBootGeneration,
            "GuestAgentGeneration": guestAgentGeneration
        ]
        if let guestBootId {
            payload["GuestBootId"] = guestBootId
        }

        switch operation {
        case .processStart:
            var command: [String: Any] = [:]
            if let fileName {
                command["FileName"] = fileName
            }
            command["Arguments"] = arguments
            if let workingDirectory {
                command["WorkingDirectory"] = workingDirectory
            }
            command["Environment"] = Self.applyEnvironmentPolicy(environment, isolation: isolation, sandboxPlan: sandboxPlan)
            payload["ProcessStartRequest"] = [
                "ProcessId": processId,
                "UnitId": unitId ?? "",
                "Command": command,
                "Io": io,
                "Policy": policy,
                "Isolation": isolation,
                "SandboxPlan": sandboxPlan
            ]
        case .processStatus:
            payload["Operation"] = 23
            payload["ProcessStatusRequest"] = [
                "ProcessId": processId,
                "IncludeResult": true
            ]
        case .processWait:
            var lifecycle: [String: Any] = ["ProcessId": processId]
            if let timeoutMilliseconds {
                lifecycle["TimeoutMilliseconds"] = timeoutMilliseconds
            }
            payload["ProcessLifecycleRequest"] = lifecycle
        case .processStdin, .processCloseStdin:
            payload["Operation"] = 24
            payload["ProcessStdinRequest"] = [
                "ProcessId": processId,
                "Bytes": stdinBytesBase64 ?? "",
                "Sequence": stdinSequence,
                "CloseAfterWrite": closeAfterWrite || operation == .processCloseStdin
            ]
        case .processSignal:
            payload["ProcessSignalRequest"] = [
                "ProcessId": processId,
                "Signal": ["Name": signalName ?? "SIGTERM"]
            ]
        case .processStop:
            payload["ProcessStopRequest"] = [
                "ProcessId": processId,
                "Kind": stopKind ?? 2,
                "Reason": stopReason ?? ""
            ]
        default:
            payload["ProcessLifecycleRequest"] = ["ProcessId": processId]
        }

        return payload
    }

    private static func applyEnvironmentPolicy(
        _ environment: [String: String],
        isolation: [String: Any],
        sandboxPlan: [String: Any]
    ) -> [String: String] {
        let policy = environmentPolicy(from: isolation) ?? environmentPolicy(from: sandboxPlan)
        guard let policy else {
            return environment
        }

        let stripUnlisted = VmConfigurationValidationRequest.bool(policy["StripUnlistedVariables"]) ??
            VmConfigurationValidationRequest.bool(policy["stripUnlistedVariables"]) ??
            false
        let allowedVariables = Set(stringArray(policy["AllowedVariables"]) + stringArray(policy["allowedVariables"]))
        let injectedVariables = stringDictionary(policy["InjectedVariables"]).merging(
            stringDictionary(policy["injectedVariables"])
        ) { _, new in new }

        var result = stripUnlisted
            ? environment.filter { allowedVariables.contains($0.key) }
            : environment

        for (key, value) in injectedVariables {
            result[key] = value
        }

        return result
    }

    private static func environmentPolicy(from value: [String: Any]) -> [String: Any]? {
        if let environment = value["Environment"] as? [String: Any] {
            return environment
        }
        if let environment = value["environment"] as? [String: Any] {
            return environment
        }
        if let plan = value["Plan"] as? [String: Any] {
            return environmentPolicy(from: plan)
        }
        if let plan = value["plan"] as? [String: Any] {
            return environmentPolicy(from: plan)
        }
        return nil
    }

    private static func stringArray(_ value: Any?) -> [String] {
        guard let array = value as? [Any] else {
            return []
        }

        return array.compactMap { VmConfigurationValidationRequest.string($0) }
    }

    private static func stringDictionary(_ value: Any?) -> [String: String] {
        guard let dictionary = value as? [String: Any] else {
            return [:]
        }

        var result: [String: String] = [:]
        for (key, value) in dictionary {
            if let stringValue = VmConfigurationValidationRequest.string(value) {
                result[key] = stringValue
            }
        }
        return result
    }

    private static func milliseconds(_ value: Any?) -> Int? {
        if let text = VmConfigurationValidationRequest.string(value) {
            let parts = text.split(separator: ":")
            if parts.count == 3,
               let hours = Double(parts[0]),
               let minutes = Double(parts[1]),
               let seconds = Double(parts[2]) {
                return Int(((hours * 3600.0) + (minutes * 60.0) + seconds) * 1000.0)
            }
        }
        return VmConfigurationValidationRequest.int(value)
    }
}

public struct ProcessResult {
    public let processId: String
    public let phase: Int
    public let ioState: Int
    public let providerProcessId: String?
    public let systemProcessId: Int?
    public let result: [String: Any]?
    public let outputEvent: [String: Any]?
    public let diagnostic: VmConfigurationValidationDiagnostic?
}

public enum ProcessStateFactory {
    public static func result(_ request: ProcessRequest, operation: Operation) -> ProcessResult {
        guard request.scriptedReadinessState == .ready else {
            return failure(
                request,
                phase: 7,
                ioState: 0,
                code: "AppleVirtualization.GuestAgentNotReady",
                message: "Process \(operation.wireName) requires verified guest-agent readiness.",
                targetPath: "process.guestAgentReady",
                severity: 3)
        }

        if request.requireVerifiedProjection && request.scriptedGuestProjectionState != .verified {
            return failure(
                request,
                phase: 7,
                ioState: 0,
                code: "AppleVirtualization.ProcessProjectionNotReady",
                message: "Process \(operation.wireName) requires a guest-verified projection/workdir before execution.",
                targetPath: "process.requiredProjection",
                severity: 3)
        }

        switch operation {
        case .processStart:
            return ProcessResult(
                processId: request.processId,
                phase: 3,
                ioState: 1,
                providerProcessId: "guest-" + request.processId,
                systemProcessId: 4242,
                result: nil,
                outputEvent: nil,
                diagnostic: nil)
        case .processStatus:
            return ProcessResult(
                processId: request.processId,
                phase: 3,
                ioState: 1,
                providerProcessId: "guest-" + request.processId,
                systemProcessId: 4242,
                result: nil,
                outputEvent: nil,
                diagnostic: nil)
        case .processWait:
            return ProcessResult(
                processId: request.processId,
                phase: 6,
                ioState: 4,
                providerProcessId: "guest-" + request.processId,
                systemProcessId: 4242,
                result: processInvocationResult(request),
                outputEvent: nil,
                diagnostic: nil)
        case .processReadOutput:
            return ProcessResult(
                processId: request.processId,
                phase: 3,
                ioState: 1,
                providerProcessId: "guest-" + request.processId,
                systemProcessId: 4242,
                result: nil,
                outputEvent: outputEvent(request),
                diagnostic: nil)
        case .processStdin:
            return ProcessResult(
                processId: request.processId,
                phase: 3,
                ioState: request.closeAfterWrite ? 2 : 1,
                providerProcessId: "guest-" + request.processId,
                systemProcessId: 4242,
                result: nil,
                outputEvent: nil,
                diagnostic: nil)
        case .processCloseStdin:
            return ProcessResult(
                processId: request.processId,
                phase: 3,
                ioState: 2,
                providerProcessId: "guest-" + request.processId,
                systemProcessId: 4242,
                result: nil,
                outputEvent: nil,
                diagnostic: nil)
        case .processSignal:
            return ProcessResult(
                processId: request.processId,
                phase: 3,
                ioState: 1,
                providerProcessId: "guest-" + request.processId,
                systemProcessId: 4242,
                result: nil,
                outputEvent: nil,
                diagnostic: nil)
        case .processStop:
            return ProcessResult(
                processId: request.processId,
                phase: 4,
                ioState: 1,
                providerProcessId: "guest-" + request.processId,
                systemProcessId: 4242,
                result: nil,
                outputEvent: nil,
                diagnostic: nil)
        default:
            return failure(
                request,
                phase: 7,
                ioState: 0,
                code: "AppleVirtualization.ProcessOperationUnsupported",
                message: "Process operation \(operation.wireName) is not supported by the fake process bridge.",
                targetPath: "process.operation")
        }
    }

    private static func failure(
        _ request: ProcessRequest,
        phase: Int,
        ioState: Int,
        code: String,
        message: String,
        targetPath: String,
        severity: Int = 4
    ) -> ProcessResult {
        ProcessResult(
            processId: request.processId,
            phase: phase,
            ioState: ioState,
            providerProcessId: nil,
            systemProcessId: nil,
            result: nil,
            outputEvent: nil,
            diagnostic: VmConfigurationValidationDiagnostic(code: code, message: message, targetPath: targetPath, severity: severity))
    }

    private static func outputEvent(_ request: ProcessRequest) -> [String: Any] {
        let sequence = (request.afterOutputSequence ?? 0) + 1
        return [
            "ProcessId": request.processId,
            "Stream": 0,
            "Sequence": sequence,
            "ObservedAt": isoTimestamp(),
            "Bytes": "SFBELQo=",
            "Flags": 1
        ]
    }

    private static func processInvocationResult(_ request: ProcessRequest) -> [String: Any] {
        [
            "ProcessId": ["Value": request.processId],
            "SystemProcessId": 4242,
            "ProviderProcessId": "guest-" + request.processId,
            "ExitCode": 0,
            "CompletionKind": 1,
            "StartedAt": isoTimestamp(),
            "ExitedAt": isoTimestamp(),
            "Duration": "00:00:01",
            "Output": [
                "Stdout": [
                    "CapturedBytes": "",
                    "BytesObserved": 0,
                    "BytesCaptured": 0,
                    "BytesDiscarded": 0,
                    "Truncated": false
                ],
                "Stderr": [
                    "CapturedBytes": "",
                    "BytesObserved": 0,
                    "BytesCaptured": 0,
                    "BytesDiscarded": 0,
                    "Truncated": false
                ],
                "MergedStandardError": request.mergeStandardError,
                "OutputDrainTimedOut": false,
                "OutputDrainTimeout": "00:00:02"
            ],
            "Violations": [],
            "Diagnostics": []
        ]
    }

    private static func isoTimestamp() -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: Date())
    }
}

public enum VmConfigurationValidationPhase: Int {
    case requested = 0
    case structuralInputValidation = 1
    case sdkConfigurationBuild = 2
    case sdkConfigurationValidate = 3
    case sdkUnavailable = 4
    case completed = 5
}

public enum VmConfigurationValidationState: Int {
    case unknown = 0
    case passed = 1
    case failed = 2
    case unsupported = 3
}

public struct VmConfigurationValidationDiagnostic {
    public let code: String
    public let message: String
    public let targetPath: String
    public let severity: Int

    public init(code: String, message: String, targetPath: String, severity: Int = 4) {
        self.code = code
        self.message = message
        self.targetPath = targetPath
        self.severity = severity
    }

    public func toJson() -> [String: Any] {
        [
            "Severity": severity,
            "Code": ["Value": code],
            "Message": message,
            "ProviderId": ["Value": "hpd.execution.apple-virtualization"],
            "TargetPath": targetPath
        ]
    }
}

public struct VmConfigurationValidationResult {
    public let phase: VmConfigurationValidationPhase
    public let state: VmConfigurationValidationState
    public let passed: Bool
    public let diagnostics: [VmConfigurationValidationDiagnostic]
    public let preflightFacts: [VirtualizationPreflightFact]

    public init(
        phase: VmConfigurationValidationPhase,
        state: VmConfigurationValidationState,
        passed: Bool,
        diagnostics: [VmConfigurationValidationDiagnostic],
        preflightFacts: [VirtualizationPreflightFact]
    ) {
        self.phase = phase
        self.state = state
        self.passed = passed
        self.diagnostics = diagnostics
        self.preflightFacts = preflightFacts
    }
}

public struct VmConfigurationValidationRequest {
    public let hostId: String
    public let cpuCount: Int
    public let memorySizeBytes: UInt64
    public let guestImage: [String: Any]
    public let sharedDirectories: [[String: Any]]
    public let includeSerialConsole: Bool
    public let includeVirtioSocketPlaceholder: Bool

    public static func parse(from envelope: HelperEnvelope) -> VmConfigurationValidationRequest {
        let payload = envelope.raw["VmConfigurationValidationRequest"] as? [String: Any] ?? [:]
        return VmConfigurationValidationRequest(
            hostId: string(payload["HostId"]) ?? "unknown",
            cpuCount: int(payload["CpuCount"]) ?? 0,
            memorySizeBytes: uint64(payload["MemorySizeBytes"]) ?? 0,
            guestImage: payload["GuestImage"] as? [String: Any] ?? [:],
            sharedDirectories: payload["SharedDirectories"] as? [[String: Any]] ?? [],
            includeSerialConsole: bool(payload["IncludeSerialConsole"]) ?? true,
            includeVirtioSocketPlaceholder: bool(payload["IncludeVirtioSocketPlaceholder"]) ?? false
        )
    }

    public var bootLoader: String {
        Self.string(guestImage["BootLoader"]) ?? Self.int(guestImage["BootLoader"]).map { $0 == 1 ? "Efi" : "LinuxBootLoader" } ?? "LinuxBootLoader"
    }

    public var kernelPath: String? { Self.nonEmpty(Self.string(guestImage["KernelPath"])) }
    public var initrdPath: String? { Self.nonEmpty(Self.string(guestImage["InitrdPath"])) }
    public var kernelCommandLine: String? { Self.nonEmpty(Self.string(guestImage["KernelCommandLine"])) }
    public var machineIdentifierData: String? {
        Self.nonEmpty(Self.string(guestImage["MachineIdentifierData"]))
    }
    public var stableMacAddress: String? {
        Self.nonEmpty(Self.string(guestImage["StableMacAddress"]))
    }
    public var diskAttachments: [[String: Any]] {
        guestImage["DiskAttachments"] as? [[String: Any]] ?? []
    }
    public var efiVariableStorePath: String? { Self.nonEmpty(Self.string(guestImage["EfiVariableStorePath"])) }
    public var serialLogPath: String? { Self.nonEmpty(Self.string(guestImage["SerialLogPath"])) }

    public static func nonEmpty(_ value: String?) -> String? {
        guard let value, !value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return nil
        }
        return value
    }

    public static func string(_ value: Any?) -> String? {
        value as? String
    }

    public static func int(_ value: Any?) -> Int? {
        if let intValue = value as? Int {
            return intValue
        }
        if let number = value as? NSNumber {
            return number.intValue
        }
        if let string = value as? String {
            return Int(string)
        }
        return nil
    }

    public static func uint64(_ value: Any?) -> UInt64? {
        if let number = value as? NSNumber {
            return number.uint64Value
        }
        if let string = value as? String {
            return UInt64(string)
        }
        return nil
    }

    public static func bool(_ value: Any?) -> Bool? {
        if let boolValue = value as? Bool {
            return boolValue
        }
        if let number = value as? NSNumber {
            return number.boolValue
        }
        return nil
    }
}

public final class LocalVirtualizationAdapter: VirtualizationAdapter, @unchecked Sendable {
    public let allowsSyntheticAuthorityFallback = false

    private var hosts: [String: HostRecord] = [:]
    private lazy var endpointForwarders = EndpointForwarderManager { [weak self] targetAddress, targetPort in
        self?.guestAgentTcpTunnel(targetAddress: targetAddress, targetPort: targetPort)
    }
    private let vmQueue = DispatchQueue(
        label: "io.hpd.environment.apple-virtualization.vm")
    private let vmQueueKey = DispatchSpecificKey<UInt8>()
    private let lock = NSLock()
    private let hostPowerMonitor = HostPowerMonitor()

    public init() {
        vmQueue.setSpecific(key: vmQueueKey, value: 1)
    }

    private func runOnVmQueue<T>(_ body: () -> T) -> T {
        if DispatchQueue.getSpecific(key: vmQueueKey) != nil {
            return body()
        }

        return vmQueue.sync(execute: body)
    }

    public func preflight() -> VirtualizationPreflight {
        let host = Self.hostFacts()
        let entitlement = Self.entitlementFact()
        let signing = Self.signingFact()
        #if canImport(Virtualization)
        let supported = VZVirtualMachine.isSupported
        return VirtualizationPreflight(
            frameworkAvailable: true,
            virtualizationSupported: supported,
            entitlementVerified: entitlement.verified,
            facts: host + [
                VirtualizationPreflightFact(
                    name: "virtualization-framework",
                    state: .supported,
                    reason: "FrameworkImportAvailable",
                    message: "Virtualization.framework is available to hpd-vz.",
                    observedValue: "available",
                    severity: 2),
                VirtualizationPreflightFact(
                    name: "vzvirtualmachine-supported",
                    state: supported ? .supported : .unsupported,
                    reason: supported ? "VZVirtualMachineIsSupported" : "VZVirtualMachineIsSupportedFalse",
                    message: supported
                        ? "VZVirtualMachine.isSupported is true on this host."
                        : "VZVirtualMachine.isSupported is false on this host.",
                    observedValue: supported ? "true" : "false",
                    severity: supported ? 2 : 4),
                entitlement.fact,
                signing
            ] + Self.configurationFacts()
        )
        #else
        return VirtualizationPreflight(
            frameworkAvailable: false,
            virtualizationSupported: false,
            entitlementVerified: entitlement.verified,
            facts: host + [
                VirtualizationPreflightFact(
                    name: "virtualization-framework",
                    state: .unsupported,
                    reason: "FrameworkImportUnavailable",
                    message: "Virtualization.framework is unavailable to this hpd-vz build.",
                    observedValue: "unavailable",
                    severity: 4),
                VirtualizationPreflightFact(
                    name: "vzvirtualmachine-supported",
                    state: .unknown,
                    reason: "FrameworkImportUnavailable",
                    message: "VZVirtualMachine.isSupported cannot be inspected because Virtualization.framework is unavailable.",
                    severity: 3),
                entitlement.fact,
                signing
            ] + Self.configurationFacts()
        )
        #endif
    }

    public func validateVmConfiguration(_ request: VmConfigurationValidationRequest) -> VmConfigurationValidationResult {
        runOnVmQueue {
            validateVmConfigurationOnMainThread(request)
        }
    }

    private func validateVmConfigurationOnMainThread(_ request: VmConfigurationValidationRequest) -> VmConfigurationValidationResult {
        let structuralDiagnostics = Self.validateStructuralInputs(request)
        if !structuralDiagnostics.isEmpty {
            return VmConfigurationValidationResult(
                phase: .structuralInputValidation,
                state: .failed,
                passed: false,
                diagnostics: structuralDiagnostics,
                preflightFacts: preflight().facts
            )
        }

        #if canImport(Virtualization)
        do {
            let configuration = try Self.buildConfiguration(request)
            try configuration.validate()
            return VmConfigurationValidationResult(
                phase: .completed,
                state: .passed,
                passed: true,
                diagnostics: [],
                preflightFacts: preflight().facts + [Self.validationFact(state: .supported, reason: "VZConfigurationValidatePassed", message: "VZVirtualMachineConfiguration.validate() completed without starting a VM.")]
            )
        } catch {
            return VmConfigurationValidationResult(
                phase: .sdkConfigurationValidate,
                state: .failed,
                passed: false,
                diagnostics: [
                    VmConfigurationValidationDiagnostic(
                        code: "AppleVirtualization.VmConfigurationInvalid",
                        message: "VZVirtualMachineConfiguration.validate() failed: \(error)",
                        targetPath: "vmConfiguration")
                ],
                preflightFacts: preflight().facts + [Self.validationFact(state: .requiresRemediation, reason: "VZConfigurationValidateFailed", message: "VZVirtualMachineConfiguration.validate() failed without starting a VM.")]
            )
        }
        #else
        return VmConfigurationValidationResult(
            phase: .sdkUnavailable,
            state: .unsupported,
            passed: false,
            diagnostics: [
                VmConfigurationValidationDiagnostic(
                    code: "AppleVirtualization.VirtualizationFrameworkUnavailable",
                    message: "Virtualization.framework is unavailable to this hpd-vz build; VM configuration validation cannot be performed.",
                    targetPath: "virtualization.framework")
            ],
            preflightFacts: preflight().facts + [Self.validationFact(state: .unsupported, reason: "FrameworkImportUnavailable", message: "VM configuration validation is unavailable because Virtualization.framework cannot be imported.")]
        )
        #endif
    }

    public func startHost(_ request: HostLifecycleRequest) -> HostLifecycleResult {
        runOnVmQueue {
            startHostOnMainThread(request)
        }
    }

    private func startHostOnMainThread(_ request: HostLifecycleRequest) -> HostLifecycleResult {
        guard request.explicitRealMode else {
            return HostLifecycleResult(
                hostId: request.hostId,
                state: .failed,
                accepted: false,
                diagnostics: [Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.RealModeExplicitEnablementRequired",
                    message: "host.start requires ExplicitRealMode=true; no VM was created.",
                    targetPath: "HostLifecycleRequest.ExplicitRealMode")])
        }

        guard let vmRequest = request.vmConfiguration else {
            return HostLifecycleResult(
                hostId: request.hostId,
                state: .failed,
                accepted: false,
                diagnostics: [Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.HostStartConfigurationMissing",
                    message: "host.start requires a VM configuration validation request; no VM was created.",
                    targetPath: "HostLifecycleRequest.VmConfigurationValidationRequest")])
        }

        let structuralDiagnostics = Self.validateStructuralInputs(vmRequest)
        if !structuralDiagnostics.isEmpty {
            return HostLifecycleResult(hostId: request.hostId, state: .failed, accepted: false, diagnostics: structuralDiagnostics)
        }

        #if canImport(Virtualization)
        lock.lock()
        defer { lock.unlock() }

        if let existing = hosts[request.hostId] {
            guard existing.providerGeneration == request.providerGeneration else {
                return HostLifecycleResult(
                    hostId: request.hostId,
                    state: .failed,
                    accepted: false,
                    diagnostics: [Self.lifecycleDiagnostic(
                        code: "AppleVirtualization.HostProviderGenerationMismatch",
                        message: "The existing VM belongs to a different provider generation.",
                        targetPath: "host.start")])
            }
            switch HostStartLifecycleDecision.evaluate(
                state: existing.state,
                recordGeneration: existing.hostStartGeneration,
                requestGeneration: request.hostStartGeneration) {
            case .reuse:
                return HostLifecycleResult(
                    hostId: request.hostId,
                    state: existing.state,
                    accepted: true,
                    diagnostics: existing.diagnostics)
            case .reject:
                return HostLifecycleResult(
                    hostId: request.hostId,
                    state: existing.state,
                    accepted: false,
                    diagnostics: [Self.lifecycleDiagnostic(
                        code: existing.state == .stopping
                            ? "AppleVirtualization.HostStillStopping"
                            : "AppleVirtualization.HostStartGenerationMismatch",
                        message: existing.state == .stopping
                            ? "The VM cannot restart until its current stop operation completes."
                            : "The requested host-start generation \(request.hostStartGeneration) is not valid for the existing \(existing.state.rawValue) VM incarnation \(existing.hostStartGeneration).",
                        targetPath: "HostLifecycleRequest.HostStartGeneration")])
            case .replace:
                hosts.removeValue(forKey: request.hostId)
            }
        }

        do {
            let diskLeases = try Self.acquireDiskLeases(
                vmRequest)
            let configuration = try Self.buildConfiguration(vmRequest)
            try configuration.validate()
            let machine = VZVirtualMachine(
                configuration: configuration,
                queue: vmQueue)
            let delegate = HostVirtualMachineDelegate { [weak self] machine, state, diagnostic in
                self?.recordStop(hostId: request.hostId, machine: machine, state: state, diagnostic: diagnostic)
            }
            machine.delegate = delegate
            hosts[request.hostId] = HostRecord(
                machine: machine,
                delegate: delegate,
                diskLeases: diskLeases,
                state: .starting,
                diagnostics: [],
                providerGeneration: request.providerGeneration,
                hostStartGeneration: request.hostStartGeneration)
            machine.start { [weak self, weak machine] result in
                guard let machine else {
                    return
                }
                self?.recordStartCompletion(hostId: request.hostId, machine: machine, result: result)
            }
            return HostLifecycleResult(hostId: request.hostId, state: .starting, accepted: true)
        } catch {
            return HostLifecycleResult(
                hostId: request.hostId,
                state: .failed,
                accepted: false,
                diagnostics: [Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.HostStartFailed",
                    message: "VZVirtualMachine start setup failed: \(error)",
                    targetPath: "host.start")])
        }
        #else
        return HostLifecycleResult(
            hostId: request.hostId,
            state: .failed,
            accepted: false,
            diagnostics: [Self.lifecycleDiagnostic(
                code: "AppleVirtualization.VirtualizationFrameworkUnavailable",
                message: "Virtualization.framework is unavailable to this hpd-vz build; host.start cannot create a VM.",
                targetPath: "virtualization.framework")])
        #endif
    }

    public func hostStatus(_ request: HostLifecycleRequest) -> HostLifecycleResult {
        runOnVmQueue {
            hostStatusOnMainThread(request)
        }
    }

    private func hostStatusOnMainThread(_ request: HostLifecycleRequest) -> HostLifecycleResult {
        lock.lock()
        defer { lock.unlock() }
        guard let existing = hosts[request.hostId] else {
            return HostLifecycleResult(
                hostId: request.hostId,
                state: .notCreated,
                accepted: true,
                powerObservation: hostPowerMonitor.snapshot())
        }
        #if canImport(Virtualization)
        existing.refreshStateFromMachine()
        #endif
        return HostLifecycleResult(
            hostId: request.hostId,
            state: existing.state,
            accepted: true,
            diagnostics: existing.diagnostics,
            powerObservation: hostPowerMonitor.snapshot())
    }

    public func powerObservation() -> HostPowerObservation {
        hostPowerMonitor.snapshot()
    }

    public func acknowledgeWake(_ request: HostLifecycleRequest) -> HostLifecycleResult {
        runOnVmQueue {
            guard let observedGeneration = request.observedWakeGeneration else {
                return HostLifecycleResult(
                    hostId: request.hostId,
                    state: hostState(request.hostId),
                    accepted: false,
                    diagnostics: [Self.lifecycleDiagnostic(
                        code: "AppleVirtualization.WakeGenerationRequired",
                        message: "Wake reconciliation requires the exact observed wake generation.",
                        targetPath: "HostLifecycleRequest.ObservedWakeGeneration")],
                    powerObservation: hostPowerMonitor.snapshot())
            }
            let accepted = hostPowerMonitor.acknowledge(
                wakeGeneration: observedGeneration)
            return HostLifecycleResult(
                hostId: request.hostId,
                state: hostState(request.hostId),
                accepted: accepted,
                diagnostics: accepted ? [] : [Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.WakeGenerationMismatch",
                    message: "Wake reconciliation did not match the current fenced wake generation.",
                    targetPath: "HostLifecycleRequest.ObservedWakeGeneration")],
                powerObservation: hostPowerMonitor.snapshot())
        }
    }

    private func hostState(_ hostId: String) -> HostLifecycleState {
        lock.lock()
        defer { lock.unlock() }
        return hosts[hostId]?.state ?? .notCreated
    }

    public func requestStopHost(_ request: HostLifecycleRequest) -> HostLifecycleResult {
        #if canImport(Virtualization) && canImport(Darwin)
        let endpoint = GuestAgentTransportEndpoint(
            kind: .virtioSocket,
            port: DefaultGuestAgentVirtioSocketPort,
            address: nil,
            name: nil)
        let resolution = resolveRunningSocketDevice(
            hostId: request.hostId,
            endpoint: endpoint,
            requireVmRunning: true,
            providerGeneration: request.providerGeneration,
            hostStartGeneration: request.hostStartGeneration)
        guard let socketDevice = resolution.socketDevice else {
            if resolution.result?.reason == "VmNotCreated" {
                return HostLifecycleResult(
                    hostId: request.hostId,
                    state: .notCreated,
                    accepted: true)
            }
            return HostLifecycleResult(
                hostId: request.hostId,
                state: .running,
                accepted: false,
                diagnostics: [resolution.result?.diagnostic ?? Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.GuestShutdownHostUnavailable",
                    message: resolution.result?.message ?? "The VM is unavailable for guest-agent shutdown.",
                    targetPath: "host.requestStop.guestAgent",
                    severity: 4)])
        }

        let timeout = boundedTimeoutMilliseconds(
            min(request.gracePeriodMilliseconds ?? 30_000, 30_000))
        let connection = connectGuestAgentSocket(
            socketDevice: socketDevice,
            request: GuestAgentTransportProbeRequest(
                hostId: request.hostId,
                endpoint: endpoint,
                timeoutMilliseconds: timeout,
                explicitRealMode: true,
                requireVmRunning: true,
                scriptedStatus: nil))
        guard let socketConnection = connection.connection else {
            return HostLifecycleResult(
                hostId: request.hostId,
                state: .running,
                accepted: false,
                diagnostics: [connection.result.diagnostic ?? Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.GuestShutdownTransportUnavailable",
                    message: connection.result.message,
                    targetPath: "host.requestStop.guestAgent",
                    severity: 4)])
        }
        defer { socketConnection.close() }

        let fd = socketConnection.fileDescriptor
        _ = setNonBlocking(fd)
        let shutdownRequest: [String: Any] = [
            "ProtocolVersion": HelperProtocol.currentVersion,
            "MessageType": MessageType.request.rawValue,
            "Operation": 52,
            "RequestId": "guest-host-shutdown-\(UUID().uuidString)",
            "SequenceNumber": 1,
            "HostId": request.hostId,
            "ProviderGeneration": request.providerGeneration,
            "HostStartGeneration": request.hostStartGeneration,
            "HostShutdownRequest": [
                "HostId": request.hostId,
                "ProviderGeneration": request.providerGeneration,
                "HostStartGeneration": request.hostStartGeneration,
                "Reason": request.reason ?? "host lifecycle stop"
            ]
        ]
        guard writeJsonLine(
                  shutdownRequest,
                  fd: fd,
                  timeoutMilliseconds: timeout) else {
            return HostLifecycleResult(
                hostId: request.hostId,
                state: .running,
                accepted: false,
                diagnostics: [Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.GuestShutdownWriteFailed",
                    message: "The identity-bound host shutdown request could not be written to the HPD guest agent.",
                    targetPath: "host.requestStop.guestAgent",
                    severity: 4)])
        }
        guard let frame = readJsonLine(
                  fd: fd,
                  timeoutMilliseconds: timeout) else {
            return HostLifecycleResult(
                hostId: request.hostId,
                state: .running,
                accepted: false,
                diagnostics: [Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.GuestShutdownResponseTimeout",
                    message: "The HPD guest agent did not return a bounded host shutdown response.",
                    targetPath: "host.requestStop.guestAgent",
                    severity: 4)])
        }
        guard let response = parseJsonObject(frame) else {
            return HostLifecycleResult(
                hostId: request.hostId,
                state: .running,
                accepted: false,
                diagnostics: [Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.GuestShutdownResponseMalformed",
                    message: "The HPD guest agent returned a malformed host shutdown response.",
                    targetPath: "host.requestStop.guestAgent",
                    severity: 4)])
        }
        if let error = response["Error"] as? [String: Any] {
            let guestCode = string(error["Code"]) ?? "unknown"
            return HostLifecycleResult(
                hostId: request.hostId,
                state: .running,
                accepted: false,
                diagnostics: [Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.GuestShutdownRejected",
                    message: "The HPD guest agent rejected the identity-bound host shutdown request (\(guestCode)).",
                    targetPath: "host.requestStop.guestAgent",
                    severity: 4)])
        }
        guard let shutdown = response["HostShutdownResponse"] as? [String: Any],
              bool(shutdown["Accepted"]) == true,
              string(response["HostId"]) == request.hostId,
              uint64(response["ProviderGeneration"]) == request.providerGeneration,
              uint64(response["HostStartGeneration"]) == request.hostStartGeneration else {
            return HostLifecycleResult(
                hostId: request.hostId,
                state: .running,
                accepted: false,
                diagnostics: [Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.GuestShutdownIdentityMismatch",
                    message: "The HPD guest agent host shutdown response did not match the active host generations.",
                    targetPath: "host.requestStop.guestAgent",
                    severity: 4)])
        }

        return runOnVmQueue {
            lock.lock()
            defer { lock.unlock() }
            guard let existing = hosts[request.hostId] else {
                return HostLifecycleResult(
                    hostId: request.hostId,
                    state: .stopped,
                    accepted: true)
            }
            existing.refreshStateFromMachine()
            if existing.state == .running {
                existing.state = .stopping
            }
            return HostLifecycleResult(
                hostId: request.hostId,
                state: existing.state,
                accepted: true,
                diagnostics: existing.diagnostics)
        }
        #else
        return hostStatus(request)
        #endif
    }

    public func stopHost(_ request: HostLifecycleRequest) -> HostLifecycleResult {
        runOnVmQueue {
            stopHostOnMainThread(request)
        }
    }

    private func stopHostOnMainThread(_ request: HostLifecycleRequest) -> HostLifecycleResult {
        #if canImport(Virtualization)
        lock.lock()
        guard let existing = hosts[request.hostId] else {
            lock.unlock()
            return HostLifecycleResult(hostId: request.hostId, state: .notCreated, accepted: true)
        }

        if #available(macOS 12.0, *), existing.machine.canStop {
            existing.state = .stopping
            let machine = existing.machine
            machine.stop { [weak self, weak machine] error in
                guard let machine else {
                    return
                }
                self?.recordStop(hostId: request.hostId, machine: machine, state: error == nil ? .stopped : .failed, diagnostic: error.map {
                    Self.lifecycleDiagnostic(code: "AppleVirtualization.HostStopFailed", message: "VZVirtualMachine.stop() failed: \($0)", targetPath: "host.stop")
                })
            }
            let diagnostics = existing.diagnostics
            lock.unlock()
            return HostLifecycleResult(hostId: request.hostId, state: .stopping, accepted: true, diagnostics: diagnostics)
        }

        let state = existing.state
        lock.unlock()
        return HostLifecycleResult(hostId: request.hostId, state: state, accepted: false, diagnostics: [Self.lifecycleDiagnostic(code: "AppleVirtualization.HostStopNotAvailable", message: "The VM state does not currently allow destructive stop.", targetPath: "host.stop", severity: 3)])
        #else
        return hostStatus(request)
        #endif
    }

    public func deleteHost(_ request: HostLifecycleRequest) -> HostLifecycleResult {
        runOnVmQueue {
            deleteHostOnMainThread(request)
        }
    }

    private func deleteHostOnMainThread(_ request: HostLifecycleRequest) -> HostLifecycleResult {
        #if canImport(Virtualization)
        lock.lock()
        guard let existing = hosts[request.hostId] else {
            lock.unlock()
            return HostLifecycleResult(hostId: request.hostId, state: .notCreated, accepted: true)
        }

        guard HostDeletionGenerationDecision.evaluate(
            recordGeneration: existing.providerGeneration,
            requestGeneration: request.providerGeneration) == .current else {
            let state = existing.state
            lock.unlock()
            return HostLifecycleResult(
                hostId: request.hostId,
                state: state,
                accepted: false,
                diagnostics: [Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.HostDeleteProviderGenerationMismatch",
                    message: "The deletion request belongs to a stale provider generation and cannot affect the current VM incarnation.",
                    targetPath: "host.delete.providerGeneration")])
        }

        existing.refreshStateFromMachine()
        switch existing.state {
        case .stopped, .failed, .notCreated:
            hosts.removeValue(forKey: request.hostId)
            lock.unlock()
            return HostLifecycleResult(hostId: request.hostId, state: .notCreated, accepted: true)
        case .stopping:
            let diagnostics = existing.diagnostics
            lock.unlock()
            return HostLifecycleResult(hostId: request.hostId, state: .stopping, accepted: true, diagnostics: diagnostics)
        case .running, .starting:
            guard #available(macOS 12.0, *), existing.machine.canStop else {
                let state = existing.state
                lock.unlock()
                return HostLifecycleResult(
                    hostId: request.hostId,
                    state: state,
                    accepted: false,
                    diagnostics: [Self.lifecycleDiagnostic(
                        code: "AppleVirtualization.HostDeleteStopNotAvailable",
                        message: "The live VM cannot be deleted until Virtualization.framework permits a destructive stop.",
                        targetPath: "host.delete",
                        severity: 3)])
            }

            existing.state = .stopping
            let machine = existing.machine
            machine.stop { [weak self, weak machine] error in
                guard let machine else {
                    return
                }
                self?.recordStop(hostId: request.hostId, machine: machine, state: error == nil ? .stopped : .failed, diagnostic: error.map {
                    Self.lifecycleDiagnostic(code: "AppleVirtualization.HostDeleteStopFailed", message: "VZVirtualMachine.stop() failed during deletion: \($0)", targetPath: "host.delete")
                })
            }
            let diagnostics = existing.diagnostics
            lock.unlock()
            return HostLifecycleResult(hostId: request.hostId, state: .stopping, accepted: true, diagnostics: diagnostics)
        }
        #else
        lock.lock()
        hosts.removeValue(forKey: request.hostId)
        lock.unlock()
        return HostLifecycleResult(hostId: request.hostId, state: .notCreated, accepted: true)
        #endif
    }

    public func probeGuestAgentTransport(_ request: GuestAgentTransportProbeRequest) -> GuestAgentTransportProbeResult {
        guard request.explicitRealMode else {
            return GuestAgentTransportProbeResult(
                hostId: request.hostId,
                state: .notAttempted,
                endpoint: request.endpoint,
                vmRunning: false,
                reason: "RealModeExplicitEnablementRequired",
                message: "Guest-agent transport probe requires ExplicitRealMode=true; no transport was attempted.",
                diagnostic: Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.RealModeExplicitEnablementRequired",
                    message: "Guest-agent transport probe requires ExplicitRealMode=true; no transport was attempted.",
                    targetPath: "GuestAgentTransportProbeRequest.ExplicitRealMode"))
        }

        #if canImport(Virtualization)
        let resolution = resolveRunningSocketDevice(
            hostId: request.hostId,
            endpoint: request.endpoint,
            requireVmRunning: request.requireVmRunning)
        if let result = resolution.result {
            return result
        }

        guard let socketDevice = resolution.socketDevice else {
            return GuestAgentTransportProbeResult(
                hostId: request.hostId,
                state: .unsupported,
                endpoint: request.endpoint,
                vmRunning: true,
                reason: "SocketDeviceMissing",
                message: "The running VM has no VZVirtioSocketDevice; configure IncludeVirtioSocketPlaceholder before boot.")
        }

        let connection = connectGuestAgentSocket(socketDevice: socketDevice, request: request)
        connection.connection?.close()
        return connection.result
        #else
        return GuestAgentTransportProbeResult(
            hostId: request.hostId,
            state: .unsupported,
            endpoint: request.endpoint,
            vmRunning: true,
            reason: "FrameworkImportUnavailable",
            message: "Virtualization.framework is unavailable to this hpd-vz build; guest-agent transport cannot be probed.")
        #endif
    }

    public func probeGuestAgentReadiness(_ request: GuestAgentReadinessProbeRequest) -> GuestAgentReadinessProbeResult {
        let timeoutMilliseconds = max(request.timeoutMilliseconds, 100)
        let deadline = Date().addingTimeInterval(TimeInterval(timeoutMilliseconds) / 1000.0)
        var lastResult: GuestAgentReadinessProbeResult?

        while true {
            let result = probeGuestAgentReadinessOnce(request)
            if result.verifiedReady || !isRetryableReadinessState(result.state) {
                return result
            }

            lastResult = result
            let remaining = millisecondsUntil(deadline)
            if remaining <= 0 {
                return lastResult ?? result
            }

            Thread.sleep(forTimeInterval: min(0.5, Double(remaining) / 1000.0))
        }
    }

    private func isRetryableReadinessState(_ state: GuestAgentReadinessState) -> Bool {
        switch state {
        case .transportNotConnected, .handshaking, .notReady, .timeout, .disconnected, .failed:
            return true
        case .notAttempted, .ready, .incompatibleProtocol, .incompatibleAgentVersion, .missingCapability, .malformedFrame, .guestAgentError:
            return false
        }
    }

    private func probeGuestAgentReadinessOnce(_ request: GuestAgentReadinessProbeRequest) -> GuestAgentReadinessProbeResult {
        guard request.explicitRealMode else {
            return GuestAgentReadinessProbeResult(
                hostId: request.hostId,
                state: .notAttempted,
                transportState: .notAttempted,
                endpoint: request.endpoint,
                vmRunning: false,
                transportConnected: false,
                verifiedReady: false,
                protocolVersion: nil,
                agentVersion: nil,
                guestBootId: nil,
                guestBootGeneration: 0,
                guestAgentGeneration: 0,
                runtimeFilesystemUuid: nil,
                appDataFilesystemUuid: nil,
                capabilities: nil,
                missingCapabilities: [],
                reason: "RealModeExplicitEnablementRequired",
                message: "Guest-agent readiness requires ExplicitRealMode=true; no transport was attempted.",
                diagnostic: Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.RealModeExplicitEnablementRequired",
                    message: "Guest-agent readiness requires ExplicitRealMode=true; no transport was attempted.",
                    targetPath: "GuestAgentReadinessProbeRequest.ExplicitRealMode"))
        }

        #if canImport(Virtualization)
        let resolution = resolveRunningSocketDevice(
            hostId: request.hostId,
            endpoint: request.endpoint,
            requireVmRunning: true)
        if let transportResult = resolution.result {
            return readinessTransportFailure(request, transportResult)
        }

        guard let socketDevice = resolution.socketDevice else {
            let transport = GuestAgentTransportProbeResult(
                hostId: request.hostId,
                state: .unsupported,
                endpoint: request.endpoint,
                vmRunning: true,
                reason: "SocketDeviceMissing",
                message: "The running VM has no VZVirtioSocketDevice; configure IncludeVirtioSocketPlaceholder before boot.")
            return readinessTransportFailure(request, transport)
        }

        let transportRequest = GuestAgentTransportProbeRequest(
            hostId: request.hostId,
            endpoint: request.endpoint,
            timeoutMilliseconds: request.timeoutMilliseconds,
            explicitRealMode: request.explicitRealMode,
            requireVmRunning: true,
            scriptedStatus: nil)
        let connection = connectGuestAgentSocket(socketDevice: socketDevice, request: transportRequest)
        guard let socketConnection = connection.connection else {
            return readinessTransportFailure(request, connection.result)
        }
        defer { socketConnection.close() }

        let handshake = performGuestAgentReadinessHandshake(connection: socketConnection, request: request)
        let missing = request.requiredCapabilities.filter { !(handshake.capabilities?.contains($0) ?? false) }
        let finalState: GuestAgentReadinessState = missing.isEmpty ? handshake.state : .missingCapability
        let finalDiagnostic = missing.isEmpty ? handshake.diagnostic : Self.lifecycleDiagnostic(
            code: "AppleVirtualization.GuestAgentCapabilityMissing",
            message: "Guest agent is missing required capabilities: \(missing.joined(separator: ", ")).",
            targetPath: "guestAgent.capabilities",
            severity: 3)

        return GuestAgentReadinessProbeResult(
            hostId: request.hostId,
            state: finalState,
            transportState: handshake.transportState,
            endpoint: request.endpoint,
            vmRunning: true,
            transportConnected: handshake.transportConnected,
            verifiedReady: finalState == .ready,
            protocolVersion: handshake.protocolVersion,
            agentVersion: handshake.agentVersion,
            guestBootId: handshake.guestBootId,
            guestBootGeneration: handshake.guestBootGeneration,
            guestAgentGeneration: handshake.guestAgentGeneration,
            runtimeFilesystemUuid: handshake.runtimeFilesystemUuid,
            appDataFilesystemUuid: handshake.appDataFilesystemUuid,
            capabilities: handshake.capabilities,
            missingCapabilities: missing,
            reason: missing.isEmpty ? handshake.reason : "RequiredCapabilityMissing",
            message: missing.isEmpty ? handshake.message : "Guest agent connected, but readiness failed because required capabilities are missing.",
            diagnostic: finalDiagnostic)
        #else
        let transport = GuestAgentTransportProbeResult(
            hostId: request.hostId,
            state: .unsupported,
            endpoint: request.endpoint,
            vmRunning: true,
            reason: "FrameworkImportUnavailable",
            message: "Virtualization.framework is unavailable to this hpd-vz build; guest-agent readiness cannot be probed.")
        return readinessTransportFailure(request, transport)
        #endif
    }

    #if canImport(Virtualization)
    private func resolveRunningSocketDevice(
        hostId: String,
        endpoint: GuestAgentTransportEndpoint,
        requireVmRunning: Bool,
        providerGeneration: UInt64? = nil,
        hostStartGeneration: UInt64? = nil
    ) -> (socketDevice: VZVirtioSocketDevice?, result: GuestAgentTransportProbeResult?) {
        runOnVmQueue {
            resolveRunningSocketDeviceOnMainThread(
                hostId: hostId,
                endpoint: endpoint,
                requireVmRunning: requireVmRunning,
                providerGeneration: providerGeneration,
                hostStartGeneration: hostStartGeneration)
        }
    }

    private func resolveRunningSocketDeviceOnMainThread(
        hostId: String,
        endpoint: GuestAgentTransportEndpoint,
        requireVmRunning: Bool,
        providerGeneration: UInt64? = nil,
        hostStartGeneration: UInt64? = nil
    ) -> (socketDevice: VZVirtioSocketDevice?, result: GuestAgentTransportProbeResult?) {
        guard endpoint.kind == .virtioSocket else {
            return (nil, GuestAgentTransportProbeResult(
                hostId: hostId,
                state: .unsupported,
                endpoint: endpoint,
                vmRunning: false,
                reason: "TransportKindUnsupported",
                message: "Only VZVirtioSocketDevice guest-agent transport is supported by the real helper."))
        }

        lock.lock()
        guard let existing = hosts[hostId] else {
            lock.unlock()
            return (nil, GuestAgentTransportProbeResult(
                hostId: hostId,
                state: requireVmRunning ? .waitingForVmRunning : .notAttempted,
                endpoint: endpoint,
                vmRunning: false,
                reason: "VmNotCreated",
                message: "Guest-agent transport probe was not attempted because the VM host has not been created."))
        }

        existing.refreshStateFromMachine()
        let socketDevice = existing.machine.socketDevices.compactMap { $0 as? VZVirtioSocketDevice }.first
        if let hostStartGeneration,
           HostStartGenerationDecision.evaluate(
               recordGeneration: existing.hostStartGeneration,
               requestGeneration: hostStartGeneration) == .stale {
            lock.unlock()
            return (nil, GuestAgentTransportProbeResult(
                hostId: hostId,
                state: .failed,
                endpoint: endpoint,
                vmRunning: existing.state == .running,
                reason: "HostStartGenerationStale",
                message: "The requested host route belongs to a stale VM incarnation.",
                diagnostic: Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.ProcessHostStartGenerationStale",
                    message: "The current VM host-start generation is \(existing.hostStartGeneration), not \(hostStartGeneration).",
                    targetPath: "ProcessHost.HostStartGeneration",
                    severity: 4)))
        }
        if let providerGeneration {
            let route = EngineHostRouter.resolve(
                hostId: hostId,
                providerGeneration: providerGeneration,
                hosts: [
                    hostId: EngineHostRouteState(
                        running: existing.state == .running,
                        providerGeneration: existing.providerGeneration,
                        socketAvailable: socketDevice != nil)
                ])
            switch route {
            case .staleProviderGeneration:
                lock.unlock()
                return (nil, GuestAgentTransportProbeResult(
                    hostId: hostId,
                    state: .failed,
                    endpoint: endpoint,
                    vmRunning: false,
                    reason: "ProviderGenerationStale",
                    message: "The requested host belongs to a different provider generation.",
                    diagnostic: Self.lifecycleDiagnostic(
                        code: "AppleVirtualization.EngineStatusStaleProviderGeneration",
                        message: "The requested host belongs to provider generation \(existing.providerGeneration), not \(providerGeneration).",
                        targetPath: "ProviderGeneration",
                        severity: 4)))
            case .socketMissing:
                lock.unlock()
                return (nil, GuestAgentTransportProbeResult(
                    hostId: hostId,
                    state: .failed,
                    endpoint: endpoint,
                    vmRunning: true,
                    reason: "SocketDeviceMissing",
                    message: "The requested VM does not own a VZVirtioSocketDevice.",
                    diagnostic: Self.lifecycleDiagnostic(
                        code: "AppleVirtualization.EngineStatusSocketDeviceMissing",
                        message: "The requested VM does not own a VZVirtioSocketDevice.",
                        targetPath: "engine.guestAgentSocket",
                        severity: 4)))
            case .stopped:
                break
            case .resolved:
                lock.unlock()
                return (socketDevice, nil)
            case .unknownHost:
                break
            }
        }

        guard existing.state == .running else {
            lock.unlock()
            return (nil, GuestAgentTransportProbeResult(
                hostId: hostId,
                state: .waitingForVmRunning,
                endpoint: endpoint,
                vmRunning: false,
                reason: "VmNotRunning",
                message: "Guest-agent transport probe is waiting for the VM to reach the running state."))
        }

        lock.unlock()
        return (socketDevice, nil)
    }

    private func connectGuestAgentSocket(
        socketDevice: VZVirtioSocketDevice,
        request: GuestAgentTransportProbeRequest
    ) -> (connection: VZVirtioSocketConnection?, result: GuestAgentTransportProbeResult) {
        guard let port = request.endpoint.port else {
            return (nil, GuestAgentTransportProbeResult(
                hostId: request.hostId,
                state: .failed,
                endpoint: request.endpoint,
                vmRunning: true,
                reason: "GuestAgentPortMissing",
                message: "Guest-agent virtio-socket endpoint did not include a destination port.",
                diagnostic: Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.GuestAgentTransportPortMissing",
                    message: "Guest-agent virtio-socket endpoint did not include a destination port.",
                    targetPath: "guestAgent.transport.endpoint.port")))
        }

        let timeout = boundedTimeoutMilliseconds(request.timeoutMilliseconds)
        let deadline = DispatchTime.now() + .milliseconds(timeout)
        var connectionResult: Result<VZVirtioSocketConnection, Error>?
        var resetAttempts = 0

        while true {
            let semaphore = DispatchSemaphore(value: 0)
            connectionResult = nil
            runOnVmQueue {
                socketDevice.connect(toPort: port) { result in
                    connectionResult = result
                    semaphore.signal()
                }
            }

            if semaphore.wait(timeout: deadline) == .timedOut {
                return (nil, GuestAgentTransportProbeResult(
                    hostId: request.hostId,
                    state: .timeout,
                    endpoint: request.endpoint,
                    vmRunning: true,
                    reason: "GuestAgentTransportTimeout",
                    message: "Timed out connecting to the HPD guest-agent virtio-socket port.",
                    diagnostic: Self.lifecycleDiagnostic(
                        code: "AppleVirtualization.GuestAgentTransportTimeout",
                        message: "Timed out connecting to the HPD guest-agent virtio-socket port.",
                        targetPath: "guestAgent.transport")))
            }

            if case .failure(let error) = connectionResult,
               isTransientGuestAgentConnectionReset(error),
               resetAttempts < 3,
               DispatchTime.now() < deadline {
                resetAttempts += 1
                Thread.sleep(forTimeInterval: 0.1)
                continue
            }
            break
        }

        switch connectionResult {
        case .success(let connection):
            guard connection.fileDescriptor >= 0 else {
                return (nil, GuestAgentTransportProbeResult(
                    hostId: request.hostId,
                    state: .failed,
                    endpoint: request.endpoint,
                    vmRunning: true,
                    reason: "GuestAgentTransportClosed",
                    message: "Guest-agent virtio-socket connection was closed before use.",
                    diagnostic: Self.lifecycleDiagnostic(
                        code: "AppleVirtualization.GuestAgentTransportClosed",
                        message: "Guest-agent virtio-socket connection was closed before use.",
                        targetPath: "guestAgent.transport")))
            }

            return (connection, GuestAgentTransportProbeResult(
                hostId: request.hostId,
                state: .connected,
                endpoint: request.endpoint,
                vmRunning: true,
                reason: "GuestAgentTransportConnected",
                message: "Connected to the HPD guest-agent virtio-socket port."))
        case .failure(let error):
            return (nil, GuestAgentTransportProbeResult(
                hostId: request.hostId,
                state: .refused,
                endpoint: request.endpoint,
                vmRunning: true,
                reason: "GuestAgentTransportRefused",
                message: "Guest-agent virtio-socket connection failed: \(error).",
                diagnostic: Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.GuestAgentTransportRefused",
                    message: "Guest-agent virtio-socket connection failed: \(error).",
                    targetPath: "guestAgent.transport",
                    severity: 3)))
        case .none:
            return (nil, GuestAgentTransportProbeResult(
                hostId: request.hostId,
                state: .failed,
                endpoint: request.endpoint,
                vmRunning: true,
                reason: "GuestAgentTransportNoResult",
                message: "Guest-agent virtio-socket connection completed without a result.",
                diagnostic: Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.GuestAgentTransportNoResult",
                    message: "Guest-agent virtio-socket connection completed without a result.",
                    targetPath: "guestAgent.transport")))
        }
    }

    private func isTransientGuestAgentConnectionReset(_ error: Error) -> Bool {
        let error = error as NSError
        return error.domain == NSPOSIXErrorDomain && error.code == ECONNRESET
    }
    #endif

    private func readinessTransportFailure(
        _ request: GuestAgentReadinessProbeRequest,
        _ transport: GuestAgentTransportProbeResult
    ) -> GuestAgentReadinessProbeResult {
        let state: GuestAgentReadinessState = transport.state == .timeout ? .timeout : .transportNotConnected
        return GuestAgentReadinessProbeResult(
            hostId: request.hostId,
            state: state,
            transportState: transport.state,
            endpoint: request.endpoint,
            vmRunning: transport.vmRunning,
            transportConnected: false,
            verifiedReady: false,
            protocolVersion: nil,
            agentVersion: nil,
            guestBootId: nil,
            guestBootGeneration: 0,
            guestAgentGeneration: 0,
            runtimeFilesystemUuid: nil,
            appDataFilesystemUuid: nil,
            capabilities: nil,
            missingCapabilities: [],
            reason: transport.reason,
            message: transport.message,
            diagnostic: transport.diagnostic ?? Self.lifecycleDiagnostic(
                code: "AppleVirtualization.GuestAgentTransportNotConnected",
                message: "Guest-agent readiness was not attempted because transport is not connected.",
                targetPath: "guestAgent.readiness",
                severity: 3))
    }

    #if canImport(Virtualization) && canImport(Darwin)
    private func performGuestAgentReadinessHandshake(
        connection: VZVirtioSocketConnection,
        request: GuestAgentReadinessProbeRequest
    ) -> GuestAgentHandshakeResult {
        let fd = connection.fileDescriptor
        guard fd >= 0 else {
            return handshakeFailure(
                state: .disconnected,
                transportState: .connected,
                reason: "GuestAgentTransportClosed",
                message: "Guest-agent virtio-socket connection was closed before handshake.",
                code: "AppleVirtualization.GuestAgentDisconnected",
                targetPath: "guestAgent.transport")
        }

        let timeout = boundedTimeoutMilliseconds(request.timeoutMilliseconds)
        _ = setNonBlocking(fd)

        let helloRequest: [String: Any] = [
            "ProtocolVersion": request.expectedProtocolVersion,
            "MessageType": 0,
            "Operation": 0,
            "RequestId": "guest-hello-\(UUID().uuidString)",
            "SequenceNumber": 1,
            "HostId": request.hostId
        ]
        guard writeJsonLine(helloRequest, fd: fd, timeoutMilliseconds: timeout) else {
            return handshakeFailure(state: .timeout, transportState: .connected, reason: "GuestAgentHelloWriteTimeout", message: "Timed out writing guest-agent hello request.", code: "AppleVirtualization.GuestAgentReadinessTimeout", targetPath: "guestAgent.hello")
        }
        guard let helloFrame = readJsonLine(fd: fd, timeoutMilliseconds: timeout) else {
            return handshakeFailure(state: .timeout, transportState: .connected, reason: "GuestAgentHelloReadTimeout", message: "Timed out reading guest-agent hello response.", code: "AppleVirtualization.GuestAgentReadinessTimeout", targetPath: "guestAgent.hello")
        }
        guard let hello = parseJsonObject(helloFrame) else {
            return handshakeFailure(state: .malformedFrame, transportState: .connected, reason: "GuestAgentHelloMalformed", message: "Guest-agent hello response was not valid JSON.", code: "AppleVirtualization.GuestAgentMalformedFrame", targetPath: "guestAgent.hello")
        }
        if let error = hello["Error"] as? [String: Any] {
            return handshakeFailure(state: .guestAgentError, transportState: .connected, reason: string(error["Code"]) ?? "GuestAgentHelloError", message: string(error["Message"]) ?? "Guest agent returned an error during hello.", code: string(error["Code"]) ?? "AppleVirtualization.GuestAgentError", targetPath: "guestAgent.hello")
        }

        let helloPayload = (hello["Hello"] as? [String: Any]) ?? hello
        let protocolVersion = string(helloPayload["ProtocolVersion"]) ?? string(hello["ProtocolVersion"])
        let agentVersion = string(helloPayload["AgentVersion"])
        let capabilities = parseCapabilities(helloPayload["Capabilities"])
        let guestBootId = string(helloPayload["GuestBootId"]) ?? string(hello["GuestBootId"])
        let guestBootGeneration = uint64(helloPayload["GuestBootGeneration"]) ?? uint64(hello["GuestBootGeneration"]) ?? 0
        let guestAgentGeneration = uint64(helloPayload["GuestAgentGeneration"]) ?? uint64(hello["GuestAgentGeneration"]) ?? 0

        if protocolVersion != request.expectedProtocolVersion {
            return GuestAgentHandshakeResult(
                state: .incompatibleProtocol,
                transportState: .connected,
                transportConnected: true,
                protocolVersion: protocolVersion,
                agentVersion: agentVersion,
                guestBootId: guestBootId,
                guestBootGeneration: guestBootGeneration,
                guestAgentGeneration: guestAgentGeneration,
                runtimeFilesystemUuid: nil,
                appDataFilesystemUuid: nil,
                capabilities: capabilities,
                reason: "GuestAgentProtocolMismatch",
                message: "Guest-agent protocol version did not match expected version.",
                diagnostic: Self.lifecycleDiagnostic(code: "AppleVirtualization.GuestAgentProtocolMismatch", message: "Guest-agent protocol version did not match expected version.", targetPath: "guestAgent.hello"))
        }

        if let expectedAgentVersion = request.expectedAgentVersion, agentVersion != expectedAgentVersion {
            return GuestAgentHandshakeResult(
                state: .incompatibleAgentVersion,
                transportState: .connected,
                transportConnected: true,
                protocolVersion: protocolVersion,
                agentVersion: agentVersion,
                guestBootId: guestBootId,
                guestBootGeneration: guestBootGeneration,
                guestAgentGeneration: guestAgentGeneration,
                runtimeFilesystemUuid: nil,
                appDataFilesystemUuid: nil,
                capabilities: capabilities,
                reason: "GuestAgentVersionMismatch",
                message: "Guest-agent version did not match expected version.",
                diagnostic: Self.lifecycleDiagnostic(code: "AppleVirtualization.GuestAgentVersionMismatch", message: "Guest-agent version did not match expected version.", targetPath: "guestAgent.hello"))
        }

        var readyRequest: [String: Any] = [
            "ProtocolVersion": request.expectedProtocolVersion,
            "MessageType": 0,
            "Operation": 2,
            "RequestId": "guest-ready-\(UUID().uuidString)",
            "SequenceNumber": 2,
            "HostId": request.hostId
        ]
        if let hostUtc = request.hostUtcUnixMilliseconds {
            readyRequest["ClockReconciliationRequest"] = [
                "HostUtcUnixMilliseconds": hostUtc,
                "MaximumClockSkewMilliseconds":
                    max(0, request.maximumClockSkewMilliseconds),
                "CorrectGuestClock": request.correctGuestClock
            ]
        }
        guard writeJsonLine(readyRequest, fd: fd, timeoutMilliseconds: timeout) else {
            return handshakeFailure(state: .timeout, transportState: .connected, reason: "GuestAgentReadyWriteTimeout", message: "Timed out writing guest-agent readiness request.", code: "AppleVirtualization.GuestAgentReadinessTimeout", targetPath: "guestAgent.ready")
        }
        guard let readyFrame = readJsonLine(fd: fd, timeoutMilliseconds: timeout) else {
            return handshakeFailure(state: .timeout, transportState: .connected, reason: "GuestAgentReadyReadTimeout", message: "Timed out reading guest-agent readiness response.", code: "AppleVirtualization.GuestAgentReadinessTimeout", targetPath: "guestAgent.ready")
        }
        guard let ready = parseJsonObject(readyFrame) else {
            return handshakeFailure(state: .malformedFrame, transportState: .connected, reason: "GuestAgentReadyMalformed", message: "Guest-agent readiness response was not valid JSON.", code: "AppleVirtualization.GuestAgentMalformedFrame", targetPath: "guestAgent.ready")
        }
        if let error = ready["Error"] as? [String: Any] {
            return handshakeFailure(state: .guestAgentError, transportState: .connected, reason: string(error["Code"]) ?? "GuestAgentReadyError", message: string(error["Message"]) ?? "Guest agent returned an error during readiness.", code: string(error["Code"]) ?? "AppleVirtualization.GuestAgentError", targetPath: "guestAgent.ready")
        }

        let readyPayload = (ready["Ready"] as? [String: Any]) ?? ready
        let isReady = bool(readyPayload["IsReady"]) ?? false
        return GuestAgentHandshakeResult(
            state: isReady ? .ready : .notReady,
            transportState: .connected,
            transportConnected: true,
            protocolVersion: protocolVersion,
            agentVersion: agentVersion,
            guestBootId: string(readyPayload["GuestBootId"]) ?? guestBootId,
            guestBootGeneration: uint64(readyPayload["GuestBootGeneration"]) ?? guestBootGeneration,
            guestAgentGeneration: uint64(readyPayload["GuestAgentGeneration"]) ?? guestAgentGeneration,
            runtimeFilesystemUuid:
                string(readyPayload["RuntimeFilesystemUuid"]),
            appDataFilesystemUuid:
                string(readyPayload["AppDataFilesystemUuid"]),
            capabilities: capabilities,
            reason: isReady ? "GuestAgentReady" : "GuestAgentNotReady",
            message: isReady ? "Guest-agent hello and readiness checks passed over virtio-socket." : "Guest agent responded over virtio-socket but reported not ready.",
            diagnostic: isReady ? nil : Self.lifecycleDiagnostic(code: "AppleVirtualization.GuestAgentNotReady", message: "Guest agent responded over virtio-socket but reported not ready.", targetPath: "guestAgent.ready", severity: 3))
    }
    #endif

    private func handshakeFailure(
        state: GuestAgentReadinessState,
        transportState: GuestAgentTransportState,
        reason: String,
        message: String,
        code: String,
        targetPath: String
    ) -> GuestAgentHandshakeResult {
        GuestAgentHandshakeResult(
            state: state,
            transportState: transportState,
            transportConnected: transportState == .connected,
            protocolVersion: nil,
            agentVersion: nil,
            guestBootId: nil,
            guestBootGeneration: 0,
            guestAgentGeneration: 0,
            runtimeFilesystemUuid: nil,
            appDataFilesystemUuid: nil,
            capabilities: nil,
            reason: reason,
            message: message,
            diagnostic: Self.lifecycleDiagnostic(code: code, message: message, targetPath: targetPath))
    }

    private func boundedTimeoutMilliseconds(_ value: Int) -> Int {
        min(max(value, 100), 30_000)
    }

    #if canImport(Darwin)
    private func setNonBlocking(_ fd: Int32) -> Bool {
        let flags = fcntl(fd, F_GETFL, 0)
        if flags < 0 {
            return false
        }
        return fcntl(fd, F_SETFL, flags | O_NONBLOCK) == 0
    }

    private func writeJsonLine(_ value: [String: Any], fd: Int32, timeoutMilliseconds: Int) -> Bool {
        guard JSONSerialization.isValidJSONObject(value),
              let json = try? JSONSerialization.data(withJSONObject: value, options: []) else {
            return false
        }

        var bytes = Array(json)
        bytes.append(0x0A)
        let deadline = Date().addingTimeInterval(TimeInterval(timeoutMilliseconds) / 1000.0)
        var offset = 0
        return bytes.withUnsafeBytes { raw in
            guard let base = raw.baseAddress else {
                return false
            }

            while offset < bytes.count {
                let remaining = millisecondsUntil(deadline)
                if remaining <= 0 || !pollFd(fd, events: Int16(POLLOUT), timeoutMilliseconds: remaining) {
                    return false
                }

                let written = Darwin.write(fd, base.advanced(by: offset), bytes.count - offset)
                if written > 0 {
                    offset += written
                } else if errno == EAGAIN || errno == EWOULDBLOCK || errno == EINTR {
                    continue
                } else {
                    return false
                }
            }

            return true
        }
    }

    private func readJsonLine(fd: Int32, timeoutMilliseconds: Int, maxBytes: Int = 65_536) -> Data? {
        let deadline = Date().addingTimeInterval(TimeInterval(timeoutMilliseconds) / 1000.0)
        var result = [UInt8]()
        var buffer = [UInt8](repeating: 0, count: 512)

        while result.count < maxBytes {
            let remaining = millisecondsUntil(deadline)
            if remaining <= 0 || !pollFd(fd, events: Int16(POLLIN), timeoutMilliseconds: remaining) {
                return nil
            }

            let capacity = buffer.count
            let count = buffer.withUnsafeMutableBytes { raw in
                Darwin.read(fd, raw.baseAddress, capacity)
            }

            if count > 0 {
                for index in 0..<count {
                    if buffer[index] == 0x0A {
                        return Data(result)
                    }
                    result.append(buffer[index])
                }
            } else if count == 0 {
                return nil
            } else if errno == EAGAIN || errno == EWOULDBLOCK || errno == EINTR {
                continue
            } else {
                return nil
            }
        }

        return nil
    }

    private func pollFd(_ fd: Int32, events: Int16, timeoutMilliseconds: Int) -> Bool {
        var item = pollfd(fd: fd, events: events, revents: 0)
        let result = poll(&item, 1, Int32(timeoutMilliseconds))
        return result > 0 && (item.revents & events) != 0
    }

    private func millisecondsUntil(_ deadline: Date) -> Int {
        max(0, Int(deadline.timeIntervalSinceNow * 1000.0))
    }
    #endif

    private func parseJsonObject(_ data: Data) -> [String: Any]? {
        (try? JSONSerialization.jsonObject(with: data, options: [])) as? [String: Any]
    }

    private func parseCapabilities(_ value: Any?) -> GuestAgentCapabilities? {
        guard let payload = value as? [String: Any] else {
            return nil
        }

        return GuestAgentCapabilities(
            processStart: bool(payload["ProcessStart"]) ?? false,
            processStdin: bool(payload["ProcessStdin"]) ?? false,
            processSignal: bool(payload["ProcessSignal"]) ?? false,
            processStop: bool(payload["ProcessStop"]) ?? false,
            processReadOutput: bool(payload["ProcessReadOutput"]) ?? false,
            pty: bool(payload["Pty"]) ?? false,
            processResize: bool(payload["ProcessResize"]) ?? false,
            projectionMount: bool(payload["ProjectionMount"]) ?? false,
            projectionObserve: bool(payload["ProjectionObserve"]) ?? false,
            projectionSync: bool(payload["ProjectionSync"]) ?? false,
            projectionFinalize: bool(payload["ProjectionFinalize"]) ?? false,
            projectionPromote: bool(payload["ProjectionPromote"]) ?? false,
            authorityProjection: bool(payload["AuthorityProjection"]) ?? false,
            authorityRevocation: bool(payload["AuthorityRevocation"]) ?? false,
            engineStatus: bool(payload["EngineStatus"]) ?? false,
            engineProvisioning: bool(payload["EngineProvisioning"]) ?? false,
            limitations: payload["Limitations"] as? [String] ?? [])
    }

    private func string(_ value: Any?) -> String? {
        if let value = value as? String {
            return value
        }
        if let value = value as? NSNumber {
            return value.stringValue
        }
        return nil
    }

    private func bool(_ value: Any?) -> Bool? {
        if let value = value as? Bool {
            return value
        }
        if let value = value as? NSNumber {
            return value.boolValue
        }
        if let value = value as? String {
            return value == "true" ? true : (value == "false" ? false : nil)
        }
        return nil
    }

    private func uint64(_ value: Any?) -> UInt64? {
        if let value = value as? UInt64 {
            return value
        }
        if let value = value as? NSNumber {
            return value.uint64Value
        }
        if let value = value as? String {
            return UInt64(value)
        }
        return nil
    }

    public func mountProjection(_ request: ProjectionRequest) -> ProjectionResult {
        projectionRequiresReadiness(request)
    }

    public func projectionStatus(_ request: ProjectionRequest) -> ProjectionResult {
        projectionRequiresReadiness(request)
    }

    public func unmountProjection(_ request: ProjectionRequest) -> ProjectionResult {
        projectionRequiresReadiness(request)
    }

    public func observeProjection(_ request: ProjectionRequest) -> ProjectionResult {
        projectionRequiresReadiness(request)
    }

    public func syncProjection(_ request: ProjectionSyncRequest) -> ProjectionSyncResult {
        ProjectionContentStateFactory.syncResult(request)
    }

    public func finalizeProjection(_ request: ProjectionFinalizationRequest) -> ProjectionFinalizationResult {
        ProjectionContentStateFactory.finalizationResult(request)
    }

    public func enumerateProjectionChanges(_ request: ProjectionChangeEnumerationRequest) -> ProjectionChangeEnumerationResult {
        ProjectionContentStateFactory.changeEnumerationResult(request)
    }

    public func promoteProjection(_ request: ProjectionPromotionRequest) -> ProjectionPromotionResult {
        ProjectionContentStateFactory.promotionResult(request)
    }

    public func networkStatus(_ request: NetworkStatusRequest) -> NetworkStatusResult {
        let status = hostStatus(HostLifecycleRequest(
            hostId: request.hostId,
            providerGeneration: 0,
            hostStartGeneration: 0,
            explicitRealMode: request.explicitRealMode,
            reason: nil,
            gracePeriodMilliseconds: nil,
            observedWakeGeneration: nil,
            vmConfiguration: nil))
        let vmRunning = status.state == .running
        if request.includeGuestObservation, vmRunning {
            #if canImport(Virtualization) && canImport(Darwin)
            if let guest = guestAgentNetworkStatus(request) {
                return guest
            }
            #endif
        }

        return NetworkStatusFactory.result(request, vmRunning: vmRunning)
    }

    #if canImport(Virtualization) && canImport(Darwin)
    private func guestAgentNetworkStatus(_ request: NetworkStatusRequest) -> NetworkStatusResult? {
        let endpoint = GuestAgentTransportEndpoint(
            kind: .virtioSocket,
            port: DefaultGuestAgentVirtioSocketPort,
            address: nil,
            name: nil)
        let resolution = resolveRunningSocketDevice(
            hostId: request.hostId,
            endpoint: endpoint,
            requireVmRunning: true)
        guard let socketDevice = resolution.socketDevice else {
            return nil
        }

        let transportRequest = GuestAgentTransportProbeRequest(
            hostId: request.hostId,
            endpoint: endpoint,
            timeoutMilliseconds: 10_000,
            explicitRealMode: true,
            requireVmRunning: true,
            scriptedStatus: nil)
        let connection = connectGuestAgentSocket(socketDevice: socketDevice, request: transportRequest)
        guard let socketConnection = connection.connection else {
            return nil
        }
        defer {
            socketConnection.close()
        }

        let fd = socketConnection.fileDescriptor
        guard fd >= 0 else {
            return nil
        }
        _ = setNonBlocking(fd)
        let timeout = 10_000
        let networkRequest: [String: Any] = [
            "ProtocolVersion": HelperProtocol.currentVersion,
            "MessageType": 0,
            "Operation": 29,
            "RequestId": "guest-network-\(UUID().uuidString)",
            "SequenceNumber": 1,
            "HostId": request.hostId,
            "NetworkStatusRequest": [
                "HostId": request.hostId,
                "IncludeRoutes": true,
                "IncludeListeners": request.maxListeners > 0,
                "MaxInterfaces": request.maxInterfaces,
                "MaxRoutes": request.maxRoutes,
                "MaxListeners": request.maxListeners
            ]
        ]

        guard writeJsonLine(networkRequest, fd: fd, timeoutMilliseconds: timeout),
              let frame = readJsonLine(fd: fd, timeoutMilliseconds: timeout),
              let response = parseJsonObject(frame),
              response["Error"] == nil,
              let guestStatus = response["NetworkStatus"] as? [String: Any] else {
            return nil
        }

        let defaultCapabilities = NetworkCapabilityFlags.ipv4 | NetworkCapabilityFlags.natEgress
        return NetworkStatusResult(
            hostId: request.hostId,
            state: .ready,
            defaultAttachment: .nat,
            requestedAttachment: request.requestedAttachment,
            realizedCapabilities: request.requestedAttachment == .nat ? defaultCapabilities : 0,
            discoveryCapabilities: 0,
            vmRunning: true,
            guestAgentReady: true,
            virtioSocketConfigured: true,
            attachmentCapabilities: NetworkStatusFactory.attachmentFacts(),
            guestNetworkStatus: guestStatus,
            limitations: NetworkStatusFactory.baseLimitations(),
            conditions: [],
            diagnostics: [])
    }
    #endif

    public func publishEndpoint(_ request: EndpointPublicationRequest) -> EndpointPublicationResult {
        endpointForwarders.publish(request)
    }

    public func releaseEndpoint(_ request: EndpointPublicationRequest) -> EndpointPublicationResult {
        endpointForwarders.release(request)
    }

    public func engineStatus(_ payload: [String: Any]) -> [String: Any]? {
        #if canImport(Virtualization) && canImport(Darwin)
        guard let hostId = VmConfigurationValidationRequest.string(payload["HostId"]),
              !hostId.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return engineStatusError(
                code: "AppleVirtualization.EngineStatusHostMissing",
                message: "EngineStatusRequest.HostId is required.",
                targetPath: "EngineStatusRequest.HostId")
        }
        guard let providerGeneration = VmConfigurationValidationRequest.uint64(payload["ProviderGeneration"]),
              providerGeneration > 0 else {
            return engineStatusError(
                code: "AppleVirtualization.EngineStatusProviderGenerationMissing",
                message: "Engine status requires a positive provider generation.",
                targetPath: "ProviderGeneration")
        }
        guard let engineId = VmConfigurationValidationRequest.string(payload["EngineId"]),
              !engineId.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return engineStatusError(
                code: "AppleVirtualization.EngineStatusEngineMissing",
                message: "EngineStatusRequest.EngineId is required.",
                targetPath: "EngineStatusRequest.EngineId")
        }

        let endpoint = GuestAgentTransportEndpoint(
            kind: .virtioSocket,
            port: DefaultGuestAgentVirtioSocketPort,
            address: nil,
            name: nil)
        let resolution = resolveRunningSocketDevice(
            hostId: hostId,
            endpoint: endpoint,
            requireVmRunning: true,
            providerGeneration: providerGeneration)
        guard let socketDevice = resolution.socketDevice else {
            let result = resolution.result
            return engineStatusError(
                code: result?.diagnostic?.code ?? "AppleVirtualization.EngineStatusHostUnavailable",
                message: result?.diagnostic?.message ?? result?.message ?? "The requested VM is unavailable for engine observation.",
                targetPath: result?.diagnostic?.targetPath ?? "EngineStatusRequest.HostId")
        }

        let transportRequest = GuestAgentTransportProbeRequest(
            hostId: hostId,
            endpoint: endpoint,
            timeoutMilliseconds: 10_000,
            explicitRealMode: true,
            requireVmRunning: true,
            scriptedStatus: nil)
        let connection = connectGuestAgentSocket(socketDevice: socketDevice, request: transportRequest)
        guard let socketConnection = connection.connection else {
            return engineStatusError(
                code: connection.result.diagnostic?.code ?? "AppleVirtualization.EngineStatusGuestAgentUnavailable",
                message: connection.result.diagnostic?.message ?? connection.result.message,
                targetPath: connection.result.diagnostic?.targetPath ?? "engine.guestAgentTransport")
        }
        defer { socketConnection.close() }

        let fd = socketConnection.fileDescriptor
        guard fd >= 0 else {
            return engineStatusError(
                code: "AppleVirtualization.EngineStatusGuestAgentTransportClosed",
                message: "The requested VM guest-agent connection closed before engine observation.",
                targetPath: "engine.guestAgentTransport")
        }

        _ = setNonBlocking(fd)
        let request: [String: Any] = [
            "ProtocolVersion": HelperProtocol.currentVersion,
            "MessageType": 0,
            "Operation": Operation.engineStatus.rawValue,
            "RequestId": "guest-engine-status-\(UUID().uuidString)",
            "SequenceNumber": 1,
            "HostId": hostId,
            "ProviderGeneration": providerGeneration,
            "EngineStatusRequest": payload
        ]
        guard writeJsonLine(request, fd: fd, timeoutMilliseconds: 10_000),
              let frame = readJsonLine(fd: fd, timeoutMilliseconds: 10_000),
              let response = parseJsonObject(frame) else {
            return engineStatusError(
                code: "AppleVirtualization.EngineStatusGuestAgentMalformedResponse",
                message: "The requested VM guest agent returned no valid engine status response.",
                targetPath: "engine.guestAgentResponse")
        }
        let identityValidation = EngineResponseIdentityValidator.validate(
            response: response,
            hostId: hostId,
            engineId: engineId,
            providerGeneration: providerGeneration,
            hostStartGeneration: VmConfigurationValidationRequest.uint64(payload["HostStartGeneration"]) ?? 0)
        if identityValidation == .hostMismatch {
            return engineStatusError(
                code: "AppleVirtualization.EngineStatusHostMismatch",
                message: "The envelope, engine status, and guest engine status must all identify the requested VM.",
                targetPath: "engine.guestAgentResponse")
        }
        if identityValidation == .engineMismatch {
            return engineStatusError(
                code: "AppleVirtualization.EngineStatusEngineMismatch",
                message: "The engine status and guest engine status must both identify the requested engine.",
                targetPath: "engine.guestAgentResponse.EngineStatusResponse.EngineId")
        }
        if identityValidation == .generationMismatch {
            return engineStatusError(
                code: "AppleVirtualization.EngineStatusGenerationMismatch",
                message: "The guest engine response provider or host-start generation did not match the request.",
                targetPath: "engine.guestAgentResponse.GuestEngineStatus.Generation")
        }
        return response
        #else
        return nil
        #endif
    }

    public func storage(_ payload: [String: Any]) -> [String: Any]? {
        #if canImport(Virtualization) && canImport(Darwin)
        guard let hostId =
                VmConfigurationValidationRequest.string(
                    payload["HostId"]),
              !hostId.trimmingCharacters(
                    in: .whitespacesAndNewlines).isEmpty,
              let providerGeneration =
                VmConfigurationValidationRequest.uint64(
                    payload["ProviderGeneration"]),
              providerGeneration > 0 else {
            return engineStatusError(
                code: "AppleVirtualization.StorageGenerationMissing",
                message: "Storage requires a host identity and positive provider generation.",
                targetPath: "StorageRequest")
        }
        let endpoint = GuestAgentTransportEndpoint(
            kind: .virtioSocket,
            port: DefaultGuestAgentVirtioSocketPort,
            address: nil,
            name: nil)
        let resolution = resolveRunningSocketDevice(
            hostId: hostId,
            endpoint: endpoint,
            requireVmRunning: true,
            providerGeneration: providerGeneration)
        guard let socketDevice = resolution.socketDevice else {
            return engineStatusError(
                code: resolution.result?.diagnostic?.code ??
                    "AppleVirtualization.StorageHostUnavailable",
                message: resolution.result?.diagnostic?.message ??
                    "The requested VM is unavailable for storage realization.",
                targetPath: "StorageRequest.HostId")
        }
        let transportRequest = GuestAgentTransportProbeRequest(
            hostId: hostId,
            endpoint: endpoint,
            timeoutMilliseconds: 10_000,
            explicitRealMode: true,
            requireVmRunning: true,
            scriptedStatus: nil)
        let connection = connectGuestAgentSocket(
            socketDevice: socketDevice,
            request: transportRequest)
        guard let socketConnection = connection.connection else {
            return engineStatusError(
                code: connection.result.diagnostic?.code ??
                    "AppleVirtualization.StorageGuestAgentUnavailable",
                message: connection.result.diagnostic?.message ??
                    connection.result.message,
                targetPath: "storage.guestAgentTransport")
        }
        defer { socketConnection.close() }
        let descriptor = socketConnection.fileDescriptor
        guard descriptor >= 0 else {
            return engineStatusError(
                code: "AppleVirtualization.StorageGuestAgentTransportClosed",
                message: "The guest-agent connection closed before the storage operation.",
                targetPath: "storage.guestAgentTransport")
        }
        _ = setNonBlocking(descriptor)
        let request: [String: Any] = [
            "ProtocolVersion": HelperProtocol.currentVersion,
            "MessageType": 0,
            "Operation": Operation.storage.rawValue,
            "RequestId": "guest-storage-\(UUID().uuidString)",
            "SequenceNumber": 1,
            "HostId": hostId,
            "ProviderGeneration": providerGeneration,
            "StorageRequest": payload
        ]
        let action = VmConfigurationValidationRequest.uint64(
            payload["Action"])
        let responseTimeoutMilliseconds = action == 10
            ? 300_000
            : 10_000
        guard writeJsonLine(
                request,
                fd: descriptor,
                timeoutMilliseconds: 10_000),
              let frame = readJsonLine(
                fd: descriptor,
                timeoutMilliseconds: responseTimeoutMilliseconds),
              let response = parseJsonObject(frame) else {
            return engineStatusError(
                code: "AppleVirtualization.StorageGuestAgentMalformedResponse",
                message: "The guest agent returned no valid bounded storage response.",
                targetPath: "storage.guestAgentResponse")
        }
        if let storage =
                response["StorageResponse"] as? [String: Any] {
            guard VmConfigurationValidationRequest.string(
                    storage["HostId"]) == hostId,
                  VmConfigurationValidationRequest.uint64(
                    storage["ProviderGeneration"]) ==
                    providerGeneration,
                  VmConfigurationValidationRequest.uint64(
                    storage["HostStartGeneration"]) ==
                    (VmConfigurationValidationRequest.uint64(
                        payload["HostStartGeneration"]) ?? 0) else {
                return engineStatusError(
                    code: "AppleVirtualization.StorageGenerationMismatch",
                    message: "The guest storage response did not match the requested host incarnation.",
                    targetPath: "storage.guestAgentResponse")
            }
        }
        return response
        #else
        return nil
        #endif
    }

    private func engineStatusError(code: String, message: String, targetPath: String) -> [String: Any] {
        [
            "Error": [
                "Code": code,
                "Message": message,
                "TargetPath": targetPath,
                "Retryable": false
            ]
        ]
    }

    private func guestAgentTcpTunnel(
        targetAddress: String,
        targetPort: UInt16
    ) -> Int32? {
        #if canImport(Virtualization) && canImport(Darwin)
        let resolution = resolveAnyRunningSocketDevice()
        guard let socketDevice = resolution.socketDevice else {
            return nil
        }
        let endpoint = GuestAgentTransportEndpoint(
            kind: .virtioSocket,
            port: DefaultGuestAgentVirtioSocketPort,
            address: nil,
            name: nil)
        let transportRequest = GuestAgentTransportProbeRequest(
            hostId: resolution.hostId ?? "unknown-host",
            endpoint: endpoint,
            timeoutMilliseconds: 10_000,
            explicitRealMode: true,
            requireVmRunning: true,
            scriptedStatus: nil)
        let connection = connectGuestAgentSocket(
            socketDevice: socketDevice,
            request: transportRequest)
        guard let socketConnection = connection.connection else {
            return nil
        }
        defer { socketConnection.close() }
        let fd = socketConnection.fileDescriptor
        guard fd >= 0 else {
            return nil
        }
        let request: [String: Any] = [
            "ProtocolVersion": HelperProtocol.currentVersion,
            "MessageType": 0,
            "Operation": 51,
            "RequestId": "guest-tcp-tunnel-\(UUID().uuidString)",
            "SequenceNumber": 1,
            "HostId": resolution.hostId ?? "unknown-host",
            "TcpTunnelRequest": [
                "TargetAddress": targetAddress,
                "TargetPort": Int(targetPort)
            ]
        ]
        guard writeJsonLine(
                  request,
                  fd: fd,
                  timeoutMilliseconds: 10_000),
              let frame = readJsonLine(
                  fd: fd,
                  timeoutMilliseconds: 10_000),
              let response = parseJsonObject(frame),
              response["Error"] == nil,
              response["TcpTunnelReady"] != nil else {
            return nil
        }
        let tunnelFd = Darwin.dup(fd)
        return tunnelFd >= 0 ? tunnelFd : nil
        #else
        return nil
        #endif
    }

    public func authorityBinding(_ payload: [String: Any], operation: Operation) -> [String: Any]? {
        #if canImport(Virtualization) && canImport(Darwin)
        let resolution = resolveAnyRunningSocketDevice()
        guard let socketDevice = resolution.socketDevice else {
            return nil
        }

        let transportRequest = GuestAgentTransportProbeRequest(
            hostId: resolution.hostId ?? "unknown-host",
            endpoint: GuestAgentTransportEndpoint(kind: .virtioSocket, port: DefaultGuestAgentVirtioSocketPort, address: nil, name: nil),
            timeoutMilliseconds: 30_000,
            explicitRealMode: true,
            requireVmRunning: true,
            scriptedStatus: nil)
        let connection = connectGuestAgentSocket(socketDevice: socketDevice, request: transportRequest)
        guard let socketConnection = connection.connection else {
            return nil
        }
        defer { socketConnection.close() }

        let fd = socketConnection.fileDescriptor
        guard fd >= 0 else {
            return nil
        }

        _ = setNonBlocking(fd)
        let request: [String: Any] = [
            "ProtocolVersion": HelperProtocol.currentVersion,
            "MessageType": 0,
            "Operation": operation.rawValue,
            "RequestId": "guest-authority-\(UUID().uuidString)",
            "SequenceNumber": 1,
            "AuthorityBindingRequest": payload
        ]
        guard writeJsonLine(request, fd: fd, timeoutMilliseconds: 30_000),
              let frame = readJsonLine(fd: fd, timeoutMilliseconds: 30_000),
              let response = parseJsonObject(frame) else {
            return nil
        }

        return response
        #else
        return nil
        #endif
    }

    public func startProcess(_ request: ProcessRequest) -> ProcessResult {
        guestAgentProcess(request, operation: .processStart)
    }

    public func processStatus(_ request: ProcessRequest) -> ProcessResult {
        guestAgentProcess(request, operation: .processStatus)
    }

    public func waitProcess(_ request: ProcessRequest) -> ProcessResult {
        guestAgentProcess(request, operation: .processWait)
    }

    public func writeProcessStdin(_ request: ProcessRequest) -> ProcessResult {
        guestAgentProcess(request, operation: .processStdin)
    }

    public func closeProcessStdin(_ request: ProcessRequest) -> ProcessResult {
        guestAgentProcess(request, operation: .processCloseStdin)
    }

    public func signalProcess(_ request: ProcessRequest) -> ProcessResult {
        guestAgentProcess(request, operation: .processSignal)
    }

    public func stopProcess(_ request: ProcessRequest) -> ProcessResult {
        guestAgentProcess(request, operation: .processStop)
    }

    public func readProcessOutput(_ request: ProcessRequest) -> ProcessResult {
        guestAgentProcess(request, operation: .processReadOutput)
    }

    private func guestAgentProcess(_ request: ProcessRequest, operation: Operation) -> ProcessResult {
        #if canImport(Virtualization) && canImport(Darwin)
        guard let hostId = request.hostId, !hostId.isEmpty else {
            return ProcessResult(
                processId: request.processId,
                phase: 7,
                ioState: 0,
                providerProcessId: nil,
                systemProcessId: nil,
                result: nil,
                outputEvent: nil,
                diagnostic: Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.ProcessHostIdentityMissing",
                    message: "Guest process operations require the execution unit's assigned host identity.",
                    targetPath: "ProcessRequest.HostId",
                    severity: 4))
        }
        let endpoint = GuestAgentTransportEndpoint(
            kind: .virtioSocket,
            port: DefaultGuestAgentVirtioSocketPort,
            address: nil,
            name: nil)
        let resolution = resolveRunningSocketDevice(
            hostId: hostId,
            endpoint: endpoint,
            requireVmRunning: true,
            providerGeneration: request.providerGeneration,
            hostStartGeneration: request.hostStartGeneration)
        guard let socketDevice = resolution.socketDevice else {
            return ProcessResult(
                processId: request.processId,
                phase: 7,
                ioState: 0,
                providerProcessId: nil,
                systemProcessId: nil,
                result: nil,
                outputEvent: nil,
                diagnostic: resolution.result?.diagnostic ?? Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.ProcessHostUnavailable",
                    message: resolution.result?.message ?? "The assigned VM is not available for guest process execution.",
                    targetPath: "ProcessRequest.HostId",
                    severity: 4))
        }

        let transportRequest = GuestAgentTransportProbeRequest(
            hostId: hostId,
            endpoint: endpoint,
            timeoutMilliseconds: request.timeoutMilliseconds ?? 30_000,
            explicitRealMode: true,
            requireVmRunning: true,
            scriptedStatus: nil)
        let connection = connectGuestAgentSocket(socketDevice: socketDevice, request: transportRequest)
        guard let socketConnection = connection.connection else {
            return ProcessResult(
                processId: request.processId,
                phase: 7,
                ioState: 0,
                providerProcessId: nil,
                systemProcessId: nil,
                result: nil,
                outputEvent: nil,
                diagnostic: connection.result.diagnostic ?? Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.GuestAgentTransportNotConnected",
                    message: connection.result.message,
                    targetPath: "process.guestAgentTransport",
                    severity: 3))
        }
        defer { socketConnection.close() }

        let fd = socketConnection.fileDescriptor
        guard fd >= 0 else {
            return ProcessResult(
                processId: request.processId,
                phase: 7,
                ioState: 0,
                providerProcessId: nil,
                systemProcessId: nil,
                result: nil,
                outputEvent: nil,
                diagnostic: Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.GuestAgentTransportClosed",
                    message: "Guest-agent virtio-socket connection was closed before process \(operation.wireName).",
                    targetPath: "process.guestAgentTransport"))
        }

        let timeout = boundedTimeoutMilliseconds(request.timeoutMilliseconds ?? 30_000)
        _ = setNonBlocking(fd)
        guard writeJsonLine(request.toGuestPayload(operation: operation), fd: fd, timeoutMilliseconds: timeout) else {
            return ProcessResult(
                processId: request.processId,
                phase: 7,
                ioState: 0,
                providerProcessId: nil,
                systemProcessId: nil,
                result: nil,
                outputEvent: nil,
                diagnostic: Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.GuestAgentProcessWriteTimeout",
                    message: "Timed out writing guest-agent process \(operation.wireName) request.",
                    targetPath: "process.guestAgentWrite"))
        }

        guard let frame = readJsonLine(fd: fd, timeoutMilliseconds: timeout),
              let response = parseJsonObject(frame) else {
            return ProcessResult(
                processId: request.processId,
                phase: 7,
                ioState: 0,
                providerProcessId: nil,
                systemProcessId: nil,
                result: nil,
                outputEvent: nil,
                diagnostic: Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.GuestAgentProcessReadFailed",
                    message: "Guest-agent process \(operation.wireName) response was missing or malformed.",
                    targetPath: "process.guestAgentRead"))
        }

        let observedBootId = string(response["GuestBootId"])
        let observedHostId = string(response["HostId"])
        let observedProviderGeneration = VmConfigurationValidationRequest.uint64(response["ProviderGeneration"])
        let observedHostStartGeneration = VmConfigurationValidationRequest.uint64(response["HostStartGeneration"])
        let observedGuestBootGeneration = VmConfigurationValidationRequest.uint64(response["GuestBootGeneration"])
        let observedGuestAgentGeneration = VmConfigurationValidationRequest.uint64(response["GuestAgentGeneration"])
        guard observedHostId == hostId,
              observedProviderGeneration == request.providerGeneration,
              observedHostStartGeneration == request.hostStartGeneration,
              request.guestBootGeneration == 0 || observedGuestBootGeneration == request.guestBootGeneration,
              request.guestAgentGeneration == 0 || observedGuestAgentGeneration == request.guestAgentGeneration,
              request.guestBootId == nil || observedBootId == request.guestBootId else {
            return ProcessResult(
                processId: request.processId,
                phase: 7,
                ioState: 0,
                providerProcessId: nil,
                systemProcessId: nil,
                result: nil,
                outputEvent: nil,
                diagnostic: Self.lifecycleDiagnostic(
                    code: "AppleVirtualization.ProcessResponseIdentityMismatch",
                    message: "The guest process response identity did not match the request (host \(observedHostId ?? "missing")/\(hostId), provider \(observedProviderGeneration.map(String.init) ?? "missing")/\(request.providerGeneration), host-start \(observedHostStartGeneration.map(String.init) ?? "missing")/\(request.hostStartGeneration), guest-boot-id \(observedBootId ?? "missing")/\(request.guestBootId ?? "unconstrained"), guest-boot \(observedGuestBootGeneration.map(String.init) ?? "missing")/\(request.guestBootGeneration), guest-agent \(observedGuestAgentGeneration.map(String.init) ?? "missing")/\(request.guestAgentGeneration)).",
                    targetPath: "process.guestAgentResponse",
                    severity: 4))
        }
        return processResultFromGuestResponse(response, fallbackProcessId: request.processId)
        #else
        return ProcessStateFactory.result(request.withScriptedReadinessState(.ready), operation: operation)
        #endif
    }

    #if canImport(Virtualization)
    // Legacy provider-internal callers that do not yet carry resource ownership.
    // Process operations must never use this resolver.
    private func resolveAnyRunningSocketDevice() -> (hostId: String?, socketDevice: VZVirtioSocketDevice?, diagnostic: VmConfigurationValidationDiagnostic?) {
        runOnVmQueue {
            lock.lock()
            defer { lock.unlock() }
            for (hostId, existing) in hosts {
                existing.refreshStateFromMachine()
                guard existing.state == .running else { continue }
                if let socketDevice = existing.machine.socketDevices.compactMap({ $0 as? VZVirtioSocketDevice }).first {
                    return (hostId, socketDevice, nil)
                }
            }
            return (nil, nil, Self.lifecycleDiagnostic(
                code: "AppleVirtualization.GuestAgentSocketDeviceMissing",
                message: "No running VM with a VZVirtioSocketDevice is available.",
                targetPath: "guestAgentSocket",
                severity: 3))
        }
    }
    #endif

    private func processResultFromGuestResponse(_ response: [String: Any], fallbackProcessId: String) -> ProcessResult {
        if let error = response["Error"] as? [String: Any] {
            return ProcessResult(
                processId: fallbackProcessId,
                phase: 7,
                ioState: 0,
                providerProcessId: nil,
                systemProcessId: nil,
                result: nil,
                outputEvent: nil,
                diagnostic: Self.lifecycleDiagnostic(
                    code: string(error["Code"]) ?? "AppleVirtualization.GuestAgentProcessError",
                    message: string(error["Message"]) ?? "Guest agent returned an error for process execution.",
                    targetPath: "process.guestAgent"))
        }

        let status = response["ProcessStatusResponse"] as? [String: Any] ?? [:]
        return ProcessResult(
            processId: string(status["ProcessId"]) ?? fallbackProcessId,
            phase: VmConfigurationValidationRequest.int(status["ProcessPhase"]) ?? 7,
            ioState: VmConfigurationValidationRequest.int(status["IoState"]) ?? 0,
            providerProcessId: string(status["ProviderProcessId"]),
            systemProcessId: VmConfigurationValidationRequest.int(status["SystemProcessId"]),
            result: status["Result"] as? [String: Any],
            outputEvent: response["ProcessOutputEvent"] as? [String: Any],
            diagnostic: nil)
    }

    private func projectionRequiresReadiness(_ request: ProjectionRequest) -> ProjectionResult {
        ProjectionStateFactory.notReady(request, diagnostic: Self.lifecycleDiagnostic(
            code: "AppleVirtualization.GuestAgentNotReady",
            message: "Projection lifecycle calls require verified guest-agent readiness before helper-to-guest projection messages are sent.",
            targetPath: "projection.guestAgentReady",
            severity: 3))
    }

    private func processRequiresReadiness(_ request: ProcessRequest, operation: Operation) -> ProcessResult {
        ProcessResult(
            processId: request.processId,
            phase: 7,
            ioState: 0,
            providerProcessId: nil,
            systemProcessId: nil,
            result: nil,
            outputEvent: nil,
            diagnostic: Self.lifecycleDiagnostic(
                code: "AppleVirtualization.GuestAgentNotReady",
                message: "Process \(operation.wireName) requires verified guest-agent readiness before helper-to-guest process messages are sent.",
                targetPath: "process.guestAgentReady",
                severity: 3))
    }

    private func recordStartCompletion(hostId: String, machine: VZVirtualMachine, result: Result<Void, Error>) {
        lock.lock()
        defer { lock.unlock() }
        guard let existing = hosts[hostId], existing.machine === machine else {
            return
        }
        switch result {
        case .success:
            existing.state = .running
            existing.diagnostics = []
        case .failure(let error):
            existing.state = .failed
            existing.diagnostics = [Self.lifecycleDiagnostic(code: "AppleVirtualization.HostStartFailed", message: "VZVirtualMachine.start() failed: \(error)", targetPath: "host.start")]
        }
    }

    private func recordStop(hostId: String, machine: VZVirtualMachine, state: HostLifecycleState, diagnostic: VmConfigurationValidationDiagnostic?) {
        lock.lock()
        defer { lock.unlock() }
        guard let existing = hosts[hostId], existing.machine === machine else {
            return
        }
        existing.state = state
        existing.diagnostics = diagnostic.map { [$0] } ?? []
    }

    private static func lifecycleDiagnostic(code: String, message: String, targetPath: String, severity: Int = 4) -> VmConfigurationValidationDiagnostic {
        VmConfigurationValidationDiagnostic(code: code, message: message, targetPath: targetPath, severity: severity)
    }

    private final class HostRecord {
        #if canImport(Virtualization)
        let machine: VZVirtualMachine
        let delegate: HostVirtualMachineDelegate
        let diskLeases: [ExclusiveDiskLease]
        #endif
        var state: HostLifecycleState
        var diagnostics: [VmConfigurationValidationDiagnostic]
        let providerGeneration: UInt64
        let hostStartGeneration: UInt64

        #if canImport(Virtualization)
        init(
            machine: VZVirtualMachine,
            delegate: HostVirtualMachineDelegate,
            diskLeases: [ExclusiveDiskLease],
            state: HostLifecycleState,
            diagnostics: [VmConfigurationValidationDiagnostic],
            providerGeneration: UInt64,
            hostStartGeneration: UInt64
        ) {
            self.machine = machine
            self.delegate = delegate
            self.diskLeases = diskLeases
            self.state = state
            self.diagnostics = diagnostics
            self.providerGeneration = providerGeneration
            self.hostStartGeneration = hostStartGeneration
        }

        func refreshStateFromMachine() {
            let observed: HostLifecycleState
            switch machine.state {
            case .starting, .restoring, .resuming:
                observed = .starting
            case .running, .paused, .pausing, .saving:
                observed = .running
            case .stopping:
                observed = .stopping
            case .stopped:
                observed = .stopped
            case .error:
                observed = .failed
            @unknown default:
                observed = .failed
            }
            state = HostLifecycleObservationDecision.reconcile(
                current: state,
                observed: observed)
        }
        #endif
    }

    #if canImport(Virtualization)
    private final class HostVirtualMachineDelegate: NSObject, VZVirtualMachineDelegate {
        private let onStop: (VZVirtualMachine, HostLifecycleState, VmConfigurationValidationDiagnostic?) -> Void

        init(onStop: @escaping (VZVirtualMachine, HostLifecycleState, VmConfigurationValidationDiagnostic?) -> Void) {
            self.onStop = onStop
        }

        func guestDidStop(_ virtualMachine: VZVirtualMachine) {
            onStop(virtualMachine, .stopped, nil)
        }

        func virtualMachine(_ virtualMachine: VZVirtualMachine, didStopWithError error: Error) {
            onStop(virtualMachine, .failed, LocalVirtualizationAdapter.lifecycleDiagnostic(
                code: "AppleVirtualization.HostStoppedWithError",
                message: "VZVirtualMachine stopped with error: \(error)",
                targetPath: "virtualMachine.delegate"))
        }
    }
    #endif

    fileprivate static func diskRole(_ value: Any?) -> String? {
        if let text = value as? String {
            switch text.lowercased() {
            case "system": return "system"
            case "runtime": return "runtime"
            case "appdata", "app-data": return "app-data"
            default: return nil
            }
        }
        switch VmConfigurationValidationRequest.int(value) {
        case 0: return "system"
        case 1: return "runtime"
        case 2: return "app-data"
        default: return nil
        }
    }

    fileprivate static func diskRoleOrder(_ value: Any?) -> Int {
        switch diskRole(value) {
        case "system": return 0
        case "runtime": return 1
        case "app-data": return 2
        default: return Int.max
        }
    }

    fileprivate static func diskCachingMode(_ value: Any?) -> String {
        if let text = value as? String {
            return text.lowercased()
        }
        return VmConfigurationValidationRequest.int(value) == 1 ? "uncached" : "cached"
    }

    fileprivate static func diskSynchronizationMode(_ value: Any?) -> String {
        if let text = value as? String {
            return text.lowercased()
        }
        return VmConfigurationValidationRequest.int(value) == 1 ? "fsync" : "full"
    }

    fileprivate static func validateStructuralInputs(_ request: VmConfigurationValidationRequest) -> [VmConfigurationValidationDiagnostic] {
        var diagnostics: [VmConfigurationValidationDiagnostic] = []
        if request.cpuCount <= 0 {
            diagnostics.append(VmConfigurationValidationDiagnostic(code: "AppleVirtualization.VmConfigurationCpuCountInvalid", message: "CPU count must be greater than zero.", targetPath: "CpuCount"))
        }
        if request.memorySizeBytes == 0 {
            diagnostics.append(VmConfigurationValidationDiagnostic(code: "AppleVirtualization.VmConfigurationMemorySizeInvalid", message: "Memory size must be greater than zero.", targetPath: "MemorySizeBytes"))
        }
        let roles = request.diskAttachments.compactMap { diskRole($0["Role"]) }
        if request.diskAttachments.count != 3 || Set(roles).count != 3 ||
            !["system", "runtime", "app-data"].allSatisfy({ roles.contains($0) }) {
            diagnostics.append(VmConfigurationValidationDiagnostic(code: "AppleVirtualization.VmConfigurationDiskSetInvalid", message: "Exactly one system, runtime, and App-data disk attachment is required.", targetPath: "GuestImage.DiskAttachments"))
        }
        for (index, disk) in request.diskAttachments.enumerated() {
            if VmConfigurationValidationRequest.nonEmpty(VmConfigurationValidationRequest.string(disk["DiskImagePath"])) == nil {
                diagnostics.append(VmConfigurationValidationDiagnostic(code: "AppleVirtualization.VmConfigurationDiskImageMissing", message: "Every disk attachment requires a path.", targetPath: "GuestImage.DiskAttachments[\(index)].DiskImagePath"))
            }
        }

        if request.bootLoader.caseInsensitiveCompare("Efi") == .orderedSame {
            if request.efiVariableStorePath == nil {
                diagnostics.append(VmConfigurationValidationDiagnostic(code: "AppleVirtualization.VmConfigurationBootInputMissing", message: "EFI boot loader validation requires an EFI variable store path.", targetPath: "GuestImage.EfiVariableStorePath"))
            }
        } else if request.kernelPath == nil {
            diagnostics.append(VmConfigurationValidationDiagnostic(code: "AppleVirtualization.VmConfigurationBootInputMissing", message: "Linux boot loader validation requires a kernel path.", targetPath: "GuestImage.KernelPath"))
        }

        if request.includeSerialConsole && request.serialLogPath == nil {
            diagnostics.append(VmConfigurationValidationDiagnostic(code: "AppleVirtualization.VmConfigurationSerialLogMissing", message: "Serial console validation requires a serial log path when serial console is included.", targetPath: "GuestImage.SerialLogPath", severity: 3))
        }

        for (index, share) in request.sharedDirectories.enumerated() {
            let tag = VmConfigurationValidationRequest.nonEmpty(VmConfigurationValidationRequest.string(share["Tag"]))
            let hostPath = VmConfigurationValidationRequest.nonEmpty(VmConfigurationValidationRequest.string(share["HostPath"]))
            if tag == nil || !isValidVirtiofsTag(tag!) {
                diagnostics.append(VmConfigurationValidationDiagnostic(code: "AppleVirtualization.VmConfigurationVirtiofsTagInvalid", message: "Virtiofs shared-directory tag must be non-empty and contain only letters, digits, underscore, hyphen, or period.", targetPath: "SharedDirectories[\(index)].Tag"))
            }
            if hostPath == nil {
                diagnostics.append(VmConfigurationValidationDiagnostic(code: "AppleVirtualization.VmConfigurationVirtiofsPathInvalid", message: "Virtiofs shared-directory host path must be non-empty.", targetPath: "SharedDirectories[\(index)].HostPath"))
            }
        }

        return diagnostics
    }

    fileprivate static func isValidVirtiofsTag(_ tag: String) -> Bool {
        guard !tag.isEmpty, tag.count <= 36 else {
            return false
        }
        return tag.unicodeScalars.allSatisfy { scalar in
            CharacterSet.alphanumerics.contains(scalar) || scalar == "_" || scalar == "-" || scalar == "."
        }
    }

    fileprivate static func validationFact(state: PreflightFactState, reason: String, message: String) -> VirtualizationPreflightFact {
        VirtualizationPreflightFact(
            name: "vm-configuration-validation",
            state: state,
            reason: reason,
            message: message,
            severity: state == .supported ? 2 : 4)
    }

    #if canImport(Virtualization)
    private static func acquireDiskLeases(
        _ request: VmConfigurationValidationRequest
    ) throws -> [ExclusiveDiskLease] {
        let paths = try request.diskAttachments
            .map { disk -> String in
                guard let path =
                    VmConfigurationValidationRequest.nonEmpty(
                        VmConfigurationValidationRequest.string(
                            disk["DiskImagePath"]))
                else {
                    throw DiskLeaseError.invalidPath
                }
                return URL(fileURLWithPath: path)
                    .resolvingSymlinksInPath()
                    .standardizedFileURL.path
            }
            .sorted()
        guard Set(paths).count == paths.count else {
            throw DiskLeaseError.duplicatePath
        }
        return try paths.map {
            try ExclusiveDiskLease(path: $0)
        }
    }

    private static func buildConfiguration(_ request: VmConfigurationValidationRequest) throws -> VZVirtualMachineConfiguration {
        let configuration = VZVirtualMachineConfiguration()
        configuration.cpuCount = request.cpuCount
        configuration.memorySize = request.memorySizeBytes
        let platform = VZGenericPlatformConfiguration()
        if #available(macOS 13.0, *) {
            guard
                let encoded = request.machineIdentifierData,
                let data = Data(base64Encoded: encoded),
                let machineIdentifier =
                    VZGenericMachineIdentifier(
                        dataRepresentation: data)
            else {
                throw ValidationBuildError.unsupported(
                    "A valid persisted generic machine identifier is required.")
            }
            platform.machineIdentifier = machineIdentifier
        }
        configuration.platform = platform

        if request.bootLoader.caseInsensitiveCompare("Efi") == .orderedSame {
            if #available(macOS 13.0, *) {
                let bootLoader = VZEFIBootLoader()
                bootLoader.variableStore = VZEFIVariableStore(url: URL(fileURLWithPath: request.efiVariableStorePath!))
                configuration.bootLoader = bootLoader
            } else {
                throw ValidationBuildError.unsupported("VZEFIBootLoader requires macOS 13.0 or newer.")
            }
        } else {
            let bootLoader = VZLinuxBootLoader(kernelURL: URL(fileURLWithPath: request.kernelPath!))
            if let initrdPath = request.initrdPath {
                bootLoader.initialRamdiskURL = URL(fileURLWithPath: initrdPath)
            }
            if let commandLine = request.kernelCommandLine {
                bootLoader.commandLine = commandLine
            }
            configuration.bootLoader = bootLoader
        }

        configuration.storageDevices = try request.diskAttachments
            .sorted { diskRoleOrder($0["Role"]) < diskRoleOrder($1["Role"]) }
            .map { disk in
                let path = VmConfigurationValidationRequest.nonEmpty(
                    VmConfigurationValidationRequest.string(disk["DiskImagePath"]))!
                let readOnly = VmConfigurationValidationRequest.bool(disk["ReadOnly"]) ?? false
                let cachingMode: VZDiskImageCachingMode =
                    diskCachingMode(disk["CachingMode"]) == "uncached" ? .uncached : .cached
                let synchronizationMode: VZDiskImageSynchronizationMode =
                    diskSynchronizationMode(disk["SynchronizationMode"]) == "fsync" ? .fsync : .full
                let attachment = try VZDiskImageStorageDeviceAttachment(
                    url: URL(fileURLWithPath: path),
                    readOnly: readOnly,
                    cachingMode: cachingMode,
                    synchronizationMode: synchronizationMode)
                let device =
                    VZVirtioBlockDeviceConfiguration(attachment: attachment)
                if #available(macOS 12.3, *) {
                    let identifier: String
                    switch diskRole(disk["Role"]) {
                    case "system": identifier = "hpd-system"
                    case "runtime": identifier = "hpd-runtime"
                    case "app-data": identifier = "hpd-app-data"
                    default:
                        throw ValidationBuildError.unsupported(
                            "Every block device requires a recognized storage role.")
                    }
                    try VZVirtioBlockDeviceConfiguration
                        .validateBlockDeviceIdentifier(identifier)
                    device.blockDeviceIdentifier = identifier
                }
                return device
            }

        let network = VZVirtioNetworkDeviceConfiguration()
        network.attachment = VZNATNetworkDeviceAttachment()
        guard
            let stableMacAddress = request.stableMacAddress,
            let macAddress = VZMACAddress(string: stableMacAddress),
            macAddress.isLocallyAdministeredAddress,
            macAddress.isUnicastAddress
        else {
            throw ValidationBuildError.unsupported(
                "A valid persisted locally administered unicast MAC address is required.")
        }
        network.macAddress = macAddress
        configuration.networkDevices = [network]

        if request.includeSerialConsole, let serialLogPath = request.serialLogPath {
            let serial = VZVirtioConsoleDeviceSerialPortConfiguration()
            serial.attachment = try VZFileSerialPortAttachment(url: URL(fileURLWithPath: serialLogPath), append: true)
            configuration.serialPorts = [serial]
        }

        if !request.sharedDirectories.isEmpty {
            configuration.directorySharingDevices = request.sharedDirectories.map { share in
                let tag = VmConfigurationValidationRequest.string(share["Tag"])!
                let hostPath = VmConfigurationValidationRequest.string(share["HostPath"])!
                let readOnly = VmConfigurationValidationRequest.bool(share["ReadOnly"]) ?? true
                let device = VZVirtioFileSystemDeviceConfiguration(tag: tag)
                device.share = VZSingleDirectoryShare(directory: VZSharedDirectory(url: URL(fileURLWithPath: hostPath), readOnly: readOnly))
                return device
            }
        }

        if request.includeVirtioSocketPlaceholder {
            configuration.socketDevices = [VZVirtioSocketDeviceConfiguration()]
        }

        return configuration
    }

    private enum ValidationBuildError: Error {
        case unsupported(String)
    }
    #endif

    private static func hostFacts() -> [VirtualizationPreflightFact] {
        [
            VirtualizationPreflightFact(
                name: "host-os",
                state: .supported,
                reason: "HostOSObservable",
                message: "Host operating system was observed by hpd-vz.",
                observedValue: ProcessInfo.processInfo.operatingSystemVersionString,
                severity: 2),
            VirtualizationPreflightFact(
                name: "host-architecture",
                state: .supported,
                reason: "HostArchitectureObservable",
                message: "Host architecture was observed by hpd-vz.",
                observedValue: hostArchitecture(),
                severity: 2),
        ]
    }

    private static func configurationFacts() -> [VirtualizationPreflightFact] {
        [
            VirtualizationPreflightFact(
                name: "vm-boot-inputs",
                state: .requiresConfiguration,
                reason: "BootInputsMissing",
                message: "No kernel, initrd, EFI, disk image, or provisioning inputs were provided to helper preflight; no VM configuration was validated.",
                severity: 3),
            VirtualizationPreflightFact(
                name: "guest-agent-provisioning",
                state: .requiresConfiguration,
                reason: "GuestAgentProvisioningMissing",
                message: "HPD guest readiness, projection verification, and process execution require a guest agent in the image; helper health is not guest readiness.",
                severity: 3),
            VirtualizationPreflightFact(
                name: "helper-health-not-guest-readiness",
                state: .supported,
                reason: "ReadinessBoundaryPreserved",
                message: "hpd-vz health proves only the helper protocol loop and does not imply RuntimeHost Ready or HPD guest readiness.",
                severity: 2),
        ]
    }

    private static func hostArchitecture() -> String {
        var systemInfo = utsname()
        uname(&systemInfo)
        return withUnsafePointer(to: &systemInfo.machine) {
            $0.withMemoryRebound(to: CChar.self, capacity: 1) {
                String(validatingCString: $0) ?? "unknown"
            }
        }
    }

    private static func entitlementFact() -> (verified: Bool, fact: VirtualizationPreflightFact) {
        #if canImport(Security)
        guard let task = SecTaskCreateFromSelf(nil) else {
            return (false, VirtualizationPreflightFact(
                name: "virtualization-entitlement",
                state: .unknown,
                reason: "SecTaskUnavailable",
                message: "The running helper entitlement task could not be inspected through public Security APIs.",
                severity: 3))
        }

        let value = SecTaskCopyValueForEntitlement(task, "com.apple.security.virtualization" as CFString, nil)
        if let boolValue = value as? Bool, boolValue {
            return (true, VirtualizationPreflightFact(
                name: "virtualization-entitlement",
                state: .supported,
                reason: "EntitlementPresent",
                message: "The running helper has com.apple.security.virtualization in its code-signing entitlements.",
                observedValue: "true",
                severity: 2))
        }

        return (false, VirtualizationPreflightFact(
            name: "virtualization-entitlement",
            state: .requiresRemediation,
            reason: "EntitlementMissing",
            message: "The running helper does not expose com.apple.security.virtualization in its inspectable code-signing entitlements.",
            observedValue: "false",
            severity: 4))
        #else
        return (false, VirtualizationPreflightFact(
            name: "virtualization-entitlement",
            state: .unknown,
            reason: "SecurityFrameworkUnavailable",
            message: "Entitlement inspectability is unavailable because the Security framework cannot be imported.",
            severity: 3))
        #endif
    }

    private static func signingFact() -> VirtualizationPreflightFact {
        #if canImport(Security)
        guard SecTaskCreateFromSelf(nil) != nil else {
            return VirtualizationPreflightFact(
                name: "helper-signing",
                state: .unknown,
                reason: "SecTaskUnavailable",
                message: "Helper code-signing state could not be inspected through public Security APIs.",
                severity: 3)
        }

        return VirtualizationPreflightFact(
            name: "helper-signing",
            state: .unknown,
            reason: "RequiresRuntimeVerification",
            message: "Helper code-signing identity is not treated as passed by generic preflight; release packaging must verify it explicitly.",
            severity: 3)
        #else
        return VirtualizationPreflightFact(
            name: "helper-signing",
            state: .unknown,
            reason: "SecurityFrameworkUnavailable",
            message: "Helper code-signing inspectability is unavailable because the Security framework cannot be imported.",
            severity: 3)
        #endif
    }
}

public struct FakeVirtualizationAdapter: VirtualizationAdapter {
    public let allowsSyntheticAuthorityFallback = true

    private let preflightResult: VirtualizationPreflight
    private let powerObservationResult: HostPowerObservation

    public init(preflightResult: VirtualizationPreflight = .init(
        frameworkAvailable: true,
        virtualizationSupported: true,
        entitlementVerified: false,
        facts: [
            VirtualizationPreflightFact(
                name: "helper-protocol-compatibility",
                state: .supported,
                reason: "FakeHelperProtocolCompatible",
                message: "Fake hpd-vz speaks the current helper protocol.",
                observedValue: HelperProtocol.currentVersion,
                severity: 2),
            VirtualizationPreflightFact(
                name: "host-os",
                state: .supported,
                reason: "FakeHostOSObservable",
                message: "Fake hpd-vz is running in local smoke mode.",
                observedValue: ProcessInfo.processInfo.operatingSystemVersionString,
                severity: 2),
            VirtualizationPreflightFact(
                name: "host-architecture",
                state: .supported,
                reason: "FakeHostArchitectureObservable",
                message: "Fake hpd-vz is running in local smoke mode.",
                observedValue: "fake",
                severity: 2),
            VirtualizationPreflightFact(
                name: "virtualization-framework",
                state: .supported,
                reason: "FakeFrameworkAvailable",
                message: "Fake mode reports framework availability for protocol smoke only.",
                observedValue: "fake",
                severity: 2),
            VirtualizationPreflightFact(
                name: "vzvirtualmachine-supported",
                state: .supported,
                reason: "FakeVZVirtualMachineSupported",
                message: "Fake mode reports VZ support for protocol smoke only.",
                observedValue: "fake",
                severity: 2),
            VirtualizationPreflightFact(
                name: "virtualization-entitlement",
                state: .unknown,
                reason: "FakeSigningNotInspectable",
                message: "Fake helper smoke mode does not prove signing or entitlement state.",
                severity: 3),
            VirtualizationPreflightFact(
                name: "helper-signing",
                state: .unknown,
                reason: "FakeSigningNotInspectable",
                message: "Fake helper smoke mode does not prove helper signing state.",
                severity: 3),
            VirtualizationPreflightFact(
                name: "vm-boot-inputs",
                state: .requiresConfiguration,
                reason: "BootInputsMissing",
                message: "No kernel, initrd, EFI, disk image, or provisioning inputs were provided to helper preflight.",
                severity: 3),
            VirtualizationPreflightFact(
                name: "guest-agent-provisioning",
                state: .requiresConfiguration,
                reason: "GuestAgentProvisioningMissing",
                message: "HPD guest readiness, projection verification, and process execution require a guest agent in the image.",
                severity: 3),
            VirtualizationPreflightFact(
                name: "helper-health-not-guest-readiness",
                state: .supported,
                reason: "ReadinessBoundaryPreserved",
                message: "hpd-vz health proves only the helper protocol loop and does not imply RuntimeHost Ready or HPD guest readiness.",
                severity: 2),
        ]
    ), powerObservation: HostPowerObservation = .active) {
        self.preflightResult = preflightResult
        self.powerObservationResult = powerObservation
    }

    public func preflight() -> VirtualizationPreflight {
        preflightResult
    }

    public func validateVmConfiguration(_ request: VmConfigurationValidationRequest) -> VmConfigurationValidationResult {
        let diagnostics = LocalVirtualizationAdapter.validateStructuralInputs(request)
        if !diagnostics.isEmpty {
            return VmConfigurationValidationResult(
                phase: .structuralInputValidation,
                state: .failed,
                passed: false,
                diagnostics: diagnostics,
                preflightFacts: preflightResult.facts
            )
        }

        return VmConfigurationValidationResult(
            phase: .completed,
            state: .passed,
            passed: true,
            diagnostics: [],
            preflightFacts: preflightResult.facts + [
                LocalVirtualizationAdapter.validationFact(
                    state: .supported,
                    reason: "FakeVZConfigurationValidatePassed",
                    message: "Fake hpd-vz validated the VM configuration request structurally without starting a VM.")
            ]
        )
    }

    public func startHost(_ request: HostLifecycleRequest) -> HostLifecycleResult {
        HostLifecycleResult(
            hostId: request.hostId,
            state: .failed,
            accepted: false,
            diagnostics: [VmConfigurationValidationDiagnostic(
                code: "AppleVirtualization.HelperOperationNotImplemented",
                message: "Fake hpd-vz does not boot real VMs.",
                targetPath: "host.start")])
    }

    public func hostStatus(_ request: HostLifecycleRequest) -> HostLifecycleResult {
        HostLifecycleResult(hostId: request.hostId, state: .notCreated, accepted: true)
    }

    public func requestStopHost(_ request: HostLifecycleRequest) -> HostLifecycleResult {
        HostLifecycleResult(hostId: request.hostId, state: .notCreated, accepted: true)
    }

    public func stopHost(_ request: HostLifecycleRequest) -> HostLifecycleResult {
        HostLifecycleResult(hostId: request.hostId, state: .notCreated, accepted: true)
    }

    public func deleteHost(_ request: HostLifecycleRequest) -> HostLifecycleResult {
        HostLifecycleResult(hostId: request.hostId, state: .notCreated, accepted: true)
    }

    public func acknowledgeWake(_ request: HostLifecycleRequest) -> HostLifecycleResult {
        HostLifecycleResult(hostId: request.hostId, state: .notCreated, accepted: true)
    }

    public func powerObservation() -> HostPowerObservation {
        powerObservationResult
    }

    public func probeGuestAgentTransport(_ request: GuestAgentTransportProbeRequest) -> GuestAgentTransportProbeResult {
        let state = request.scriptedStatus ?? .unsupported
        let vmRunning = state != .waitingForVmRunning && state != .notAttempted
        let reason: String
        let message: String
        let diagnostic: VmConfigurationValidationDiagnostic?

        switch state {
        case .notAttempted:
            reason = "FakeNotAttempted"
            message = "Fake guest-agent transport probe was not attempted."
            diagnostic = nil
        case .waitingForVmRunning:
            reason = "FakeWaitingForVmRunning"
            message = "Fake guest-agent transport probe is waiting for VM running."
            diagnostic = nil
        case .connecting:
            reason = "FakeConnecting"
            message = "Fake guest-agent transport probe is connecting."
            diagnostic = nil
        case .connected:
            reason = "FakeConnected"
            message = "Fake guest-agent transport probe connected; this is not guest readiness."
            diagnostic = nil
        case .refused:
            reason = "FakeRefused"
            message = "Fake guest-agent transport probe was refused."
            diagnostic = VmConfigurationValidationDiagnostic(code: "AppleVirtualization.GuestAgentTransportRefused", message: message, targetPath: "guestAgent.transport")
        case .timeout:
            reason = "FakeTimeout"
            message = "Fake guest-agent transport probe timed out."
            diagnostic = VmConfigurationValidationDiagnostic(code: "AppleVirtualization.GuestAgentTransportTimeout", message: message, targetPath: "guestAgent.transport")
        case .unsupported:
            reason = "FakeUnsupported"
            message = "Fake guest-agent transport probe reports unsupported transport."
            diagnostic = VmConfigurationValidationDiagnostic(code: "AppleVirtualization.GuestAgentTransportUnsupported", message: message, targetPath: "guestAgent.transport", severity: 3)
        case .failed:
            reason = "FakeFailed"
            message = "Fake guest-agent transport probe failed."
            diagnostic = VmConfigurationValidationDiagnostic(code: "AppleVirtualization.GuestAgentTransportFailed", message: message, targetPath: "guestAgent.transport")
        }

        return GuestAgentTransportProbeResult(
            hostId: request.hostId,
            state: state,
            endpoint: request.endpoint,
            vmRunning: vmRunning,
            reason: reason,
            message: message,
            diagnostic: diagnostic)
    }

    public func probeGuestAgentReadiness(_ request: GuestAgentReadinessProbeRequest) -> GuestAgentReadinessProbeResult {
        let state = request.scriptedState ?? .transportNotConnected
        let transportState: GuestAgentTransportState = state == .notAttempted ? .notAttempted : (state == .transportNotConnected ? .refused : .connected)
        let capabilities = GuestAgentCapabilities.firstSlice
        let missing = request.requiredCapabilities.filter { !capabilities.contains($0) || state == .missingCapability }
        let protocolVersion = state == .incompatibleProtocol ? "0.0" : request.expectedProtocolVersion
        let agentVersion = state == .incompatibleAgentVersion ? "wrong-version" : (request.expectedAgentVersion ?? "0.1.0-test")
        let ready = state == .ready && missing.isEmpty
        let diagnostic: VmConfigurationValidationDiagnostic?
        let reason: String
        let message: String

        switch state {
        case .notAttempted:
            reason = "FakeReadinessNotAttempted"
            message = "Fake guest-agent readiness was not attempted."
            diagnostic = nil
        case .transportNotConnected:
            reason = "FakeTransportNotConnected"
            message = "Fake guest-agent readiness requires connected transport."
            diagnostic = VmConfigurationValidationDiagnostic(code: "AppleVirtualization.GuestAgentTransportNotConnected", message: message, targetPath: "guestAgent.readiness", severity: 3)
        case .handshaking:
            reason = "FakeHandshaking"
            message = "Fake guest-agent readiness handshake is still in progress."
            diagnostic = nil
        case .ready:
            reason = "FakeReady"
            message = "Fake guest-agent hello, health, ready, version, generation, and capability checks passed."
            diagnostic = nil
        case .notReady:
            reason = "FakeNotReady"
            message = "Fake guest agent responded but is not ready."
            diagnostic = VmConfigurationValidationDiagnostic(code: "AppleVirtualization.GuestAgentNotReady", message: message, targetPath: "guestAgent.ready", severity: 3)
        case .incompatibleProtocol:
            reason = "FakeProtocolMismatch"
            message = "Fake guest-agent protocol version is incompatible."
            diagnostic = VmConfigurationValidationDiagnostic(code: "AppleVirtualization.GuestAgentProtocolMismatch", message: message, targetPath: "guestAgent.hello")
        case .incompatibleAgentVersion:
            reason = "FakeAgentVersionMismatch"
            message = "Fake guest-agent version is incompatible."
            diagnostic = VmConfigurationValidationDiagnostic(code: "AppleVirtualization.GuestAgentVersionMismatch", message: message, targetPath: "guestAgent.hello")
        case .missingCapability:
            reason = "FakeMissingCapability"
            message = "Fake guest agent is missing a required capability."
            diagnostic = VmConfigurationValidationDiagnostic(code: "AppleVirtualization.GuestAgentCapabilityMissing", message: message, targetPath: "guestAgent.capabilities", severity: 3)
        case .malformedFrame:
            reason = "FakeMalformedFrame"
            message = "Fake guest agent returned a malformed frame during readiness."
            diagnostic = VmConfigurationValidationDiagnostic(code: "AppleVirtualization.GuestAgentMalformedFrame", message: message, targetPath: "guestAgent.frame")
        case .guestAgentError:
            reason = "FakeGuestAgentError"
            message = "Fake guest agent returned a structured error during readiness."
            diagnostic = VmConfigurationValidationDiagnostic(code: "AppleVirtualization.GuestAgentError", message: message, targetPath: "guestAgent.ready")
        case .disconnected:
            reason = "FakeDisconnected"
            message = "Fake guest-agent transport disconnected during readiness."
            diagnostic = VmConfigurationValidationDiagnostic(code: "AppleVirtualization.GuestAgentDisconnected", message: message, targetPath: "guestAgent.transport")
        case .timeout:
            reason = "FakeTimeout"
            message = "Fake guest-agent readiness timed out."
            diagnostic = VmConfigurationValidationDiagnostic(code: "AppleVirtualization.GuestAgentReadinessTimeout", message: message, targetPath: "guestAgent.readiness")
        case .failed:
            reason = "FakeFailed"
            message = "Fake guest-agent readiness failed."
            diagnostic = VmConfigurationValidationDiagnostic(code: "AppleVirtualization.GuestAgentReadinessFailed", message: message, targetPath: "guestAgent.readiness")
        }

        return GuestAgentReadinessProbeResult(
            hostId: request.hostId,
            state: missing.isEmpty ? state : .missingCapability,
            transportState: transportState,
            endpoint: request.endpoint,
            vmRunning: transportState != .notAttempted && transportState != .waitingForVmRunning,
            transportConnected: transportState == .connected,
            verifiedReady: ready,
            protocolVersion: protocolVersion,
            agentVersion: agentVersion,
            guestBootId: ready ? "guest-boot-1" : nil,
            guestBootGeneration: ready ? 1 : 0,
            guestAgentGeneration: ready ? 1 : 0,
            runtimeFilesystemUuid:
                ready ? request.expectedRuntimeFilesystemUuid : nil,
            appDataFilesystemUuid:
                ready ? request.expectedAppDataFilesystemUuid : nil,
            capabilities: capabilities,
            missingCapabilities: missing,
            reason: reason,
            message: message,
            diagnostic: diagnostic)
    }

    public func mountProjection(_ request: ProjectionRequest) -> ProjectionResult {
        ProjectionStateFactory.result(request, operation: .projectionMount)
    }

    public func projectionStatus(_ request: ProjectionRequest) -> ProjectionResult {
        ProjectionStateFactory.result(request, operation: .projectionStatus)
    }

    public func unmountProjection(_ request: ProjectionRequest) -> ProjectionResult {
        ProjectionStateFactory.result(request, operation: .projectionUnmount)
    }

    public func observeProjection(_ request: ProjectionRequest) -> ProjectionResult {
        ProjectionStateFactory.result(request, operation: .projectionObserve)
    }

    public func syncProjection(_ request: ProjectionSyncRequest) -> ProjectionSyncResult {
        ProjectionContentStateFactory.syncResult(request)
    }

    public func finalizeProjection(_ request: ProjectionFinalizationRequest) -> ProjectionFinalizationResult {
        ProjectionContentStateFactory.finalizationResult(request)
    }

    public func enumerateProjectionChanges(_ request: ProjectionChangeEnumerationRequest) -> ProjectionChangeEnumerationResult {
        ProjectionContentStateFactory.changeEnumerationResult(request)
    }

    public func promoteProjection(_ request: ProjectionPromotionRequest) -> ProjectionPromotionResult {
        ProjectionContentStateFactory.promotionResult(request)
    }

    public func networkStatus(_ request: NetworkStatusRequest) -> NetworkStatusResult {
        NetworkStatusFactory.result(request)
    }

    public func publishEndpoint(_ request: EndpointPublicationRequest) -> EndpointPublicationResult {
        EndpointPublicationStateFactory.publish(request)
    }

    public func releaseEndpoint(_ request: EndpointPublicationRequest) -> EndpointPublicationResult {
        EndpointPublicationStateFactory.release(request)
    }

    public func authorityBinding(_ payload: [String: Any], operation: Operation) -> [String: Any]? {
        nil
    }

    public func engineStatus(_ payload: [String: Any]) -> [String: Any]? {
        nil
    }

    public func storage(_ payload: [String: Any]) -> [String: Any]? {
        nil
    }

    public func startProcess(_ request: ProcessRequest) -> ProcessResult {
        ProcessStateFactory.result(request, operation: .processStart)
    }

    public func processStatus(_ request: ProcessRequest) -> ProcessResult {
        ProcessStateFactory.result(request, operation: .processStatus)
    }

    public func waitProcess(_ request: ProcessRequest) -> ProcessResult {
        ProcessStateFactory.result(request, operation: .processWait)
    }

    public func writeProcessStdin(_ request: ProcessRequest) -> ProcessResult {
        ProcessStateFactory.result(request, operation: .processStdin)
    }

    public func closeProcessStdin(_ request: ProcessRequest) -> ProcessResult {
        ProcessStateFactory.result(request, operation: .processCloseStdin)
    }

    public func signalProcess(_ request: ProcessRequest) -> ProcessResult {
        ProcessStateFactory.result(request, operation: .processSignal)
    }

    public func stopProcess(_ request: ProcessRequest) -> ProcessResult {
        ProcessStateFactory.result(request, operation: .processStop)
    }

    public func readProcessOutput(_ request: ProcessRequest) -> ProcessResult {
        ProcessStateFactory.result(request, operation: .processReadOutput)
    }
}
