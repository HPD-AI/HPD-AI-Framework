import Foundation

public enum HelperProtocol {
    public static let currentVersion = "1.0"
    public static let helperName = "hpd-vz"
    public static let helperVersion = "0.1.0"
    public static let helloResponseSchema = "hpd.execution.apple-virtualization.helper.hello.response.v1"
    public static let healthResponseSchema = "hpd.execution.apple-virtualization.helper.health.response.v1"
    public static let hostResponseSchema = "hpd.execution.apple-virtualization.helper.host.response.v1"
    public static let guestAgentTransportResponseSchema = "hpd.execution.apple-virtualization.helper.guest-agent.transport.response.v1"
    public static let guestAgentReadinessResponseSchema = "hpd.execution.apple-virtualization.helper.guest-agent.readiness.response.v1"
    public static let projectionResponseSchema = "hpd.execution.apple-virtualization.helper.projection.response.v1"
    public static let projectionSyncResponseSchema = "hpd.execution.apple-virtualization.helper.projection.sync.response.v1"
    public static let projectionFinalizationResponseSchema = "hpd.execution.apple-virtualization.helper.projection.finalization.response.v1"
    public static let networkStatusResponseSchema = "hpd.execution.apple-virtualization.helper.network.status.response.v1"
    public static let endpointPublicationResponseSchema = "hpd.execution.apple-virtualization.helper.endpoint.publication.response.v1"
    public static let authorityBindingResponseSchema = "hpd.execution.apple-virtualization.helper.authority.binding.response.v1"
    public static let engineStatusRequestSchema = "hpd.execution.apple-virtualization.helper.engine.status.request.v1"
    public static let engineStatusResponseSchema = "hpd.execution.apple-virtualization.helper.engine.status.response.v1"
    public static let engineProvisionRequestSchema = "hpd.execution.apple-virtualization.helper.engine.provision.request.v1"
    public static let engineProvisionResponseSchema = "hpd.execution.apple-virtualization.helper.engine.provision.response.v1"
    public static let unitResponseSchema = "hpd.execution.apple-virtualization.helper.unit.response.v1"
    public static let processResponseSchema = "hpd.execution.apple-virtualization.helper.process.response.v1"
    public static let processOutputEventSchema = "hpd.execution.apple-virtualization.helper.process.output.v1"
    public static let vmConfigurationValidationResponseSchema = "hpd.execution.apple-virtualization.helper.vm-configuration.validation.response.v1"
    public static let errorSchema = "hpd.execution.apple-virtualization.helper.error.v1"
}

public enum MessageType: Int {
    case request = 0
    case response = 1
    case event = 2
}

public enum ResponseStatus: Int {
    case ok = 0
    case accepted = 1
    case error = 2
}

public enum PreflightFactState: Int {
    case unknown = 0
    case supported = 1
    case unsupported = 2
    case requiresConfiguration = 3
    case requiresRemediation = 4
}

public enum Operation: Int, CaseIterable {
    case hello = 0
    case capabilitiesGet = 1
    case preflightRun = 2
    case activationStatus = 3
    case healthProbe = 4
    case shutdown = 5
    case hostEnsure = 6
    case hostStart = 7
    case hostStatus = 8
    case hostRequestStop = 9
    case hostStop = 10
    case hostDelete = 11
    case guestControlWaitReady = 12
    case guestControlStatus = 13
    case projectionConfigure = 14
    case projectionMount = 15
    case projectionStatus = 16
    case projectionRelease = 17
    case unitEnsure = 18
    case unitStatus = 19
    case unitStop = 20
    case unitDelete = 21
    case processStart = 22
    case processStdin = 23
    case processCloseStdin = 24
    case processSignal = 25
    case processStop = 26
    case processWait = 27
    case processReadOutput = 28
    case endpointStatus = 29
    case endpointUnsupported = 30
    case processResize = 31
    case vmConfigurationValidate = 32
    case guestAgentTransportProbe = 33
    case guestAgentReadinessProbe = 34
    case projectionUnmount = 35
    case projectionObserve = 36
    case projectionSync = 37
    case projectionFinalize = 38
    case projectionEnumerateChanges = 39
    case projectionPromote = 40
    case networkStatus = 41
    case endpointPublish = 42
    case endpointRelease = 43
    case authorityBind = 44
    case authorityStatus = 45
    case authorityRevoke = 46
    case engineStatus = 47
    case engineProvision = 48

    public var wireName: String {
        switch self {
        case .hello: "hello"
        case .capabilitiesGet: "capabilities.get"
        case .preflightRun: "preflight.run"
        case .activationStatus: "activation.status"
        case .healthProbe: "health.probe"
        case .shutdown: "shutdown"
        case .hostEnsure: "host.ensure"
        case .hostStart: "host.start"
        case .hostStatus: "host.status"
        case .hostRequestStop: "host.requestStop"
        case .hostStop: "host.stop"
        case .hostDelete: "host.delete"
        case .guestControlWaitReady: "guestControl.waitReady"
        case .guestControlStatus: "guestControl.status"
        case .projectionConfigure: "projection.configure"
        case .projectionMount: "projection.mount"
        case .projectionStatus: "projection.status"
        case .projectionRelease: "projection.release"
        case .unitEnsure: "unit.ensure"
        case .unitStatus: "unit.status"
        case .unitStop: "unit.stop"
        case .unitDelete: "unit.delete"
        case .processStart: "process.start"
        case .processStdin: "process.stdin"
        case .processCloseStdin: "process.closeStdin"
        case .processSignal: "process.signal"
        case .processStop: "process.stop"
        case .processWait: "process.wait"
        case .processReadOutput: "process.readOutput"
        case .endpointStatus: "endpoint.status"
        case .endpointUnsupported: "endpoint.unsupported"
        case .processResize: "process.resize"
        case .vmConfigurationValidate: "vmConfiguration.validate"
        case .guestAgentTransportProbe: "guestAgent.transportProbe"
        case .guestAgentReadinessProbe: "guestAgent.readinessProbe"
        case .projectionUnmount: "projection.unmount"
        case .projectionObserve: "projection.observe"
        case .projectionSync: "projection.sync"
        case .projectionFinalize: "projection.finalize"
        case .projectionEnumerateChanges: "projection.enumerateChanges"
        case .projectionPromote: "projection.promote"
        case .networkStatus: "network.status"
        case .endpointPublish: "endpoint.publish"
        case .endpointRelease: "endpoint.release"
        case .authorityBind: "authority.bind"
        case .authorityStatus: "authority.status"
        case .authorityRevoke: "authority.revoke"
        case .engineStatus: "engine.status"
        case .engineProvision: "engine.provision"
        }
    }

    public static func parse(_ value: Any?) -> Operation? {
        if let intValue = value as? Int {
            return Operation(rawValue: intValue)
        }

        if let number = value as? NSNumber {
            return Operation(rawValue: number.intValue)
        }

        guard let stringValue = value as? String else {
            return nil
        }

        if let intValue = Int(stringValue) {
            return Operation(rawValue: intValue)
        }

        return Operation.allCases.first { operation in
            operation.wireName == stringValue || String(describing: operation) == stringValue
        }
    }
}

public struct HelperEnvelope {
    public var raw: [String: Any]

    public init(raw: [String: Any]) {
        self.raw = raw
    }

    public var requestId: String? {
        raw["RequestId"] as? String
    }

    public var protocolVersion: String {
        raw["ProtocolVersion"] as? String ?? HelperProtocol.currentVersion
    }

    public var sequenceNumber: Int64 {
        if let number = raw["SequenceNumber"] as? NSNumber {
            return number.int64Value
        }

        return 0
    }

    public var operation: Operation? {
        Operation.parse(raw["Operation"])
    }
}

public enum ProtocolCodec {
    public static func decodeLine(_ data: Data) throws -> HelperEnvelope {
        let object = try JSONSerialization.jsonObject(with: data, options: [])
        guard let dictionary = object as? [String: Any] else {
            throw ProtocolError.invalidFrame("Expected a JSON object envelope.")
        }

        return HelperEnvelope(raw: dictionary)
    }

    public static func encodeLine(_ envelope: [String: Any]) throws -> Data {
        var data = try JSONSerialization.data(withJSONObject: envelope, options: [.sortedKeys])
        data.append(0x0A)
        return data
    }
}

public enum ProtocolError: Error, Equatable {
    case invalidFrame(String)
}
