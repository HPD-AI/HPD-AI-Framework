import Foundation

public final class HelperService {
    private let adapter: VirtualizationAdapter
    private var sequenceNumber: Int64 = 0
    private let providerGeneration: UInt64 = 1

    public init(adapter: VirtualizationAdapter) {
        self.adapter = adapter
    }

    public func handle(_ request: HelperEnvelope) -> [String: Any] {
        guard request.protocolVersion == HelperProtocol.currentVersion else {
            return errorResponse(
                for: request,
                operation: request.operation,
                code: "AppleVirtualization.HelperProtocolMismatch",
                message: "Helper protocol '\(HelperProtocol.currentVersion)' is not compatible with requested protocol '\(request.protocolVersion)'.",
                retryable: false,
                failedPhase: "Activation"
            )
        }

        guard let operation = request.operation else {
            return errorResponse(
                for: request,
                operation: nil,
                code: "AppleVirtualization.UnknownOperation",
                message: "The helper request did not contain a recognized operation.",
                retryable: false,
                failedPhase: "Protocol"
            )
        }

        switch operation {
        case .hello:
            return helloResponse(for: request)
        case .healthProbe:
            return healthResponse(for: request)
        case .preflightRun:
            return preflightResponse(for: request)
        case .vmConfigurationValidate:
            return vmConfigurationValidationResponse(for: request)
        case .hostStart:
            return hostLifecycleResponse(for: request, operation: .hostStart, result: adapter.startHost(HostLifecycleRequest.parse(from: request)))
        case .hostStatus:
            return hostLifecycleResponse(for: request, operation: .hostStatus, result: adapter.hostStatus(HostLifecycleRequest.parse(from: request)))
        case .hostRequestStop:
            return hostLifecycleResponse(for: request, operation: .hostRequestStop, result: adapter.requestStopHost(HostLifecycleRequest.parse(from: request)))
        case .hostStop:
            return hostLifecycleResponse(for: request, operation: .hostStop, result: adapter.stopHost(HostLifecycleRequest.parse(from: request)))
        case .hostDelete:
            return hostLifecycleResponse(for: request, operation: .hostDelete, result: adapter.deleteHost(HostLifecycleRequest.parse(from: request)))
        case .guestAgentTransportProbe:
            return guestAgentTransportResponse(for: request, result: adapter.probeGuestAgentTransport(GuestAgentTransportProbeRequest.parse(from: request)))
        case .guestAgentReadinessProbe:
            return guestAgentReadinessResponse(for: request, result: adapter.probeGuestAgentReadiness(GuestAgentReadinessProbeRequest.parse(from: request)))
        case .projectionMount:
            return projectionResponse(for: request, operation: .projectionMount, result: adapter.mountProjection(ProjectionRequest.parse(from: request)))
        case .projectionStatus:
            return projectionResponse(for: request, operation: .projectionStatus, result: adapter.projectionStatus(ProjectionRequest.parse(from: request)))
        case .projectionUnmount:
            return projectionResponse(for: request, operation: .projectionUnmount, result: adapter.unmountProjection(ProjectionRequest.parse(from: request)))
        case .projectionObserve:
            return projectionResponse(for: request, operation: .projectionObserve, result: adapter.observeProjection(ProjectionRequest.parse(from: request)))
        case .projectionSync:
            return projectionSyncResponse(for: request, result: adapter.syncProjection(ProjectionSyncRequest.parse(from: request)))
        case .projectionFinalize:
            return projectionFinalizationResponse(for: request, result: adapter.finalizeProjection(ProjectionFinalizationRequest.parse(from: request)))
        case .projectionEnumerateChanges:
            return projectionChangeEnumerationResponse(for: request, result: adapter.enumerateProjectionChanges(ProjectionChangeEnumerationRequest.parse(from: request)))
        case .projectionPromote:
            return projectionPromotionResponse(for: request, result: adapter.promoteProjection(ProjectionPromotionRequest.parse(from: request)))
        case .networkStatus:
            return networkStatusResponse(for: request, result: adapter.networkStatus(NetworkStatusRequest.parse(from: request)))
        case .endpointPublish:
            return endpointPublicationResponse(for: request, operation: .endpointPublish, result: adapter.publishEndpoint(EndpointPublicationRequest.parse(from: request)))
        case .endpointRelease:
            return endpointPublicationResponse(for: request, operation: .endpointRelease, result: adapter.releaseEndpoint(EndpointPublicationRequest.parse(from: request)))
        case .authorityBind, .authorityStatus, .authorityRevoke:
            return authorityBindingResponse(for: request, operation: operation)
        case .engineStatus:
            return engineStatusResponse(for: request)
        case .engineProvision:
            return engineProvisioningResponse(for: request)
        case .processStart:
            return processResponse(for: request, operation: .processStart, result: adapter.startProcess(ProcessRequest.parse(from: request)))
        case .processStdin:
            return processResponse(for: request, operation: .processStdin, result: adapter.writeProcessStdin(ProcessRequest.parse(from: request)))
        case .processCloseStdin:
            return processResponse(for: request, operation: .processCloseStdin, result: adapter.closeProcessStdin(ProcessRequest.parse(from: request)))
        case .processSignal:
            return processResponse(for: request, operation: .processSignal, result: adapter.signalProcess(ProcessRequest.parse(from: request)))
        case .processStop:
            return processResponse(for: request, operation: .processStop, result: adapter.stopProcess(ProcessRequest.parse(from: request)))
        case .processWait:
            return processResponse(for: request, operation: .processWait, result: adapter.waitProcess(ProcessRequest.parse(from: request)))
        case .processReadOutput:
            return processResponse(for: request, operation: .processReadOutput, result: adapter.readProcessOutput(ProcessRequest.parse(from: request)))
        default:
            return errorResponse(
                for: request,
                operation: operation,
                code: "AppleVirtualization.HelperOperationNotImplemented",
                message: "The hpd-vz helper skeleton has not implemented \(operation.wireName).",
                retryable: false,
                failedPhase: "HelperSkeleton"
            )
        }
    }

    public func malformedFrameResponse(message: String) -> [String: Any] {
        errorResponse(
            for: HelperEnvelope(raw: [
                "Operation": Operation.endpointUnsupported.rawValue,
                "ProtocolVersion": HelperProtocol.currentVersion
            ]),
            operation: .endpointUnsupported,
            code: "AppleVirtualization.MalformedFrame",
            message: message,
            retryable: false,
            failedPhase: "Protocol"
        )
    }

    private func helloResponse(for request: HelperEnvelope) -> [String: Any] {
        let preflight = adapter.preflight()
        let facts = preflight.facts.map { $0.toJson() }
        let checks = preflight.facts.map { $0.toCheck() }
        return responseBase(for: request, operation: .hello, status: .ok, schema: HelperProtocol.helloResponseSchema)
            .merging([
                "HelloResponse": [
                    "HelperName": HelperProtocol.helperName,
                    "HelperVersion": HelperProtocol.helperVersion,
                    "ProtocolVersion": HelperProtocol.currentVersion,
                    "ProviderGeneration": providerGeneration,
                    "ProtocolCompatible": true,
                    "VirtualizationFrameworkAvailable": preflight.frameworkAvailable,
                    "VirtualizationEntitlementVerified": preflight.entitlementVerified,
                    "PreflightFacts": facts,
                    "PreflightChecks": checks
                ]
            ]) { _, new in new }
    }

    private func preflightResponse(for request: HelperEnvelope) -> [String: Any] {
        let preflight = adapter.preflight()
        return responseBase(for: request, operation: .preflightRun, status: .ok, schema: "hpd.execution.apple-virtualization.helper.preflight.response.v1")
            .merging([
                "PreflightRunResponse": [
                    "Facts": preflight.facts.map { $0.toJson() },
                    "Checks": preflight.facts.map { $0.toCheck() },
                    "Permissions": []
                ]
            ]) { _, new in new }
    }

    private func healthResponse(for request: HelperEnvelope) -> [String: Any] {
        let preflight = adapter.preflight()
        let ready = preflight.frameworkAvailable
        return responseBase(for: request, operation: .healthProbe, status: .ok, schema: HelperProtocol.healthResponseSchema)
            .merging([
                "HealthProbeResponse": [
                    "Ready": ready,
                    "Detail": ready
                        ? "hpd-vz protocol loop is ready; this is not HPD guest readiness."
                        : "hpd-vz protocol loop is running, but Virtualization.framework is unavailable."
                ]
            ]) { _, new in new }
    }

    private func vmConfigurationValidationResponse(for request: HelperEnvelope) -> [String: Any] {
        let validationRequest = VmConfigurationValidationRequest.parse(from: request)
        let result = adapter.validateVmConfiguration(validationRequest)
        return responseBase(
            for: request,
            operation: .vmConfigurationValidate,
            status: result.passed ? .ok : .error,
            schema: HelperProtocol.vmConfigurationValidationResponseSchema
        )
        .merging([
            "VmConfigurationValidationResponse": [
                "Phase": result.phase.rawValue,
                "State": result.state.rawValue,
                "Passed": result.passed,
                "HostRunning": false,
                "HpdReady": false,
                "Diagnostics": result.diagnostics.map { $0.toJson() },
                "PreflightFacts": result.preflightFacts.map { $0.toJson() }
            ]
        ]) { _, new in new }
    }

    private func hostLifecycleResponse(
        for request: HelperEnvelope,
        operation: Operation,
        result: HostLifecycleResult
    ) -> [String: Any] {
        let status: ResponseStatus = result.accepted
            ? (result.state == .starting || result.state == .stopping ? .accepted : .ok)
            : .error
        var response = responseBase(
            for: request,
            operation: operation,
            status: status,
            schema: HelperProtocol.hostResponseSchema
        )
        .merging([
            "HostStatusResponse": [
                "HostId": result.hostId,
                "HostPhase": hostPhase(for: result.state),
                "Phase": resourcePhase(for: result.state),
                "GuestControlReachable": false,
                "Conditions": [],
                "Diagnostics": result.diagnostics.map { $0.toJson() }
            ]
        ]) { _, new in new }

        if status == .error {
            let first = result.diagnostics.first
            response["Error"] = [
                "Code": first?.code ?? "AppleVirtualization.HostLifecycleFailed",
                "Message": first?.message ?? "The host lifecycle operation failed.",
                "Operation": operation.wireName,
                "Retryable": false,
                "FailedPhase": "HostLifecycle",
                "Severity": first?.severity ?? 4
            ]
        }

        return response
    }

    private func guestAgentTransportResponse(
        for request: HelperEnvelope,
        result: GuestAgentTransportProbeResult
    ) -> [String: Any] {
        let status: ResponseStatus
        switch result.state {
        case .failed:
            status = .error
        default:
            status = .ok
        }

        var payload: [String: Any] = [
            "HostId": result.hostId,
            "State": result.state.rawValue,
            "Endpoint": result.endpoint.toJson(),
            "VmRunning": result.vmRunning,
            "Connected": result.state == .connected,
            "GuestReady": false,
            "Reason": result.reason,
            "Message": result.message,
            "Conditions": []
        ]

        if let diagnostic = result.diagnostic {
            payload["Error"] = [
                "Code": diagnostic.code,
                "Message": diagnostic.message,
                "Operation": Operation.guestAgentTransportProbe.wireName,
                "Retryable": result.state == .timeout || result.state == .refused,
                "FailedPhase": "GuestAgentTransport",
                "Severity": diagnostic.severity
            ]
        }

        var response = responseBase(
            for: request,
            operation: .guestAgentTransportProbe,
            status: status,
            schema: HelperProtocol.guestAgentTransportResponseSchema
        )
        .merging([
            "GuestAgentTransportProbeResponse": payload
        ]) { _, new in new }

        if status == .error, let diagnostic = result.diagnostic {
            response["Error"] = [
                "Code": diagnostic.code,
                "Message": diagnostic.message,
                "Operation": Operation.guestAgentTransportProbe.wireName,
                "Retryable": false,
                "FailedPhase": "GuestAgentTransport",
                "Severity": diagnostic.severity
            ]
        }

        return response
    }

    private func guestAgentReadinessResponse(
        for request: HelperEnvelope,
        result: GuestAgentReadinessProbeResult
    ) -> [String: Any] {
        let status: ResponseStatus
        switch result.state {
        case .malformedFrame, .guestAgentError, .disconnected, .timeout, .failed:
            status = .error
        default:
            status = .ok
        }

        var payload: [String: Any] = [
            "HostId": result.hostId,
            "State": result.state.rawValue,
            "TransportState": result.transportState.rawValue,
            "Endpoint": result.endpoint.toJson(),
            "VmRunning": result.vmRunning,
            "TransportConnected": result.transportConnected,
            "VerifiedReady": result.verifiedReady,
            "GuestBootGeneration": result.guestBootGeneration,
            "GuestAgentGeneration": result.guestAgentGeneration,
            "MissingCapabilities": result.missingCapabilities,
            "Reason": result.reason,
            "Message": result.message,
            "Conditions": []
        ]

        if let protocolVersion = result.protocolVersion {
            payload["ProtocolVersion"] = protocolVersion
        }
        if let agentVersion = result.agentVersion {
            payload["AgentVersion"] = agentVersion
        }
        if let guestBootId = result.guestBootId {
            payload["GuestBootId"] = guestBootId
        }
        if let capabilities = result.capabilities {
            payload["Capabilities"] = capabilities.toJson()
        }
        if let diagnostic = result.diagnostic {
            payload["Error"] = [
                "Code": diagnostic.code,
                "Message": diagnostic.message,
                "Operation": Operation.guestAgentReadinessProbe.wireName,
                "Retryable": result.state == .timeout || result.state == .disconnected || result.state == .transportNotConnected,
                "FailedPhase": "GuestAgentReadiness",
                "Severity": diagnostic.severity
            ]
        }

        var response = responseBase(
            for: request,
            operation: .guestAgentReadinessProbe,
            status: status,
            schema: HelperProtocol.guestAgentReadinessResponseSchema
        )
        .merging([
            "GuestAgentReadinessProbeResponse": payload
        ]) { _, new in new }

        if status == .error, let diagnostic = result.diagnostic {
            response["Error"] = [
                "Code": diagnostic.code,
                "Message": diagnostic.message,
                "Operation": Operation.guestAgentReadinessProbe.wireName,
                "Retryable": result.state == .timeout || result.state == .disconnected,
                "FailedPhase": "GuestAgentReadiness",
                "Severity": diagnostic.severity
            ]
        }

        return response
    }

    private func engineStatusResponse(for request: HelperEnvelope) -> [String: Any] {
        let statusRequest = EngineStatusRequestPayload.parse(from: request)
        let statusPayload = EngineStatusPayload.fromRequest(statusRequest)
        let timestamp = Self.timestamp()

        return responseBase(for: request, operation: .engineStatus, status: .ok, schema: HelperProtocol.engineStatusResponseSchema)
            .merging([
                "EventKind": statusPayload.observationState == 5
                    ? EngineProtocolEventKind.engineDegraded
                    : EngineProtocolEventKind.engineObserved,
                "EngineStatusResponse": statusPayload.responseJson(
                    guestAgentReady: statusRequest.includeGuestObservation,
                    timestamp: timestamp)
            ]) { _, new in new }
    }

    private func engineProvisioningResponse(for request: HelperEnvelope) -> [String: Any] {
        let provisioningRequest = EngineProvisioningRequestPayload.parse(from: request)
        let provisioning = EngineProvisioningResponsePayload.plan(for: provisioningRequest)
        let timestamp = Self.timestamp()

        return responseBase(for: request, operation: .engineProvision, status: .ok, schema: HelperProtocol.engineProvisionResponseSchema)
            .merging([
                "EventKind": provisioning.phase == 3
                    ? EngineProtocolEventKind.engineDegraded
                    : EngineProtocolEventKind.engineProvisioned,
                "EngineProvisioningResponse": provisioning.toJson(timestamp: timestamp)
            ]) { _, new in new }
    }

    private func projectionResponse(
        for request: HelperEnvelope,
        operation: Operation,
        result: ProjectionResult
    ) -> [String: Any] {
        let status: ResponseStatus = result.diagnostic?.code == "AppleVirtualization.GuestProjectionMalformedResponse" ? .error : .ok
        var payload: [String: Any] = [
            "ProjectionId": result.projectionId,
            "ProjectionPhase": result.phase,
            "EffectiveRealization": result.effectiveRealization,
            "EffectiveWriteEffect": result.effectiveWriteEffect,
            "EffectiveCoherence": result.effectiveCoherence,
            "GuestAgentReady": result.guestAgentReady,
            "HostShareConfigured": result.hostShareConfigured,
            "FrameworkShareAccepted": result.frameworkShareAccepted,
            "VerifiedByGuestAgent": result.verifiedByGuestAgent,
            "Conditions": [],
            "Diagnostics": result.diagnostic.map { [$0.toJson()] } ?? []
        ]

        if let guestStatus = result.guestStatus {
            payload["GuestProjectionStatus"] = guestStatus
        }
        if let unmountResult = result.unmountResult {
            payload["GuestProjectionUnmountResult"] = unmountResult
        }
        if let observeResult = result.observeResult {
            payload["GuestProjectionObserveResult"] = observeResult
        }

        var response = responseBase(
            for: request,
            operation: operation,
            status: status,
            schema: HelperProtocol.projectionResponseSchema
        )
        .merging([
            "ProjectionStatusResponse": payload
        ]) { _, new in new }

        if status == .error, let diagnostic = result.diagnostic {
            response["Error"] = [
                "Code": diagnostic.code,
                "Message": diagnostic.message,
                "Operation": operation.wireName,
                "Retryable": true,
                "FailedPhase": "GuestProjection",
                "Severity": diagnostic.severity
            ]
        }

        return response
    }

    private func processResponse(
        for request: HelperEnvelope,
        operation: Operation,
        result: ProcessResult
    ) -> [String: Any] {
        let status: ResponseStatus = result.diagnostic == nil ? .ok : .error
        var payload: [String: Any] = [
            "ProcessId": result.processId,
            "ProcessPhase": result.phase,
            "IoState": result.ioState,
            "Conditions": []
        ]

        if let providerProcessId = result.providerProcessId {
            payload["ProviderProcessId"] = providerProcessId
        }
        if let systemProcessId = result.systemProcessId {
            payload["SystemProcessId"] = systemProcessId
        }
        if let processResult = result.result {
            payload["Result"] = processResult
        }

        var response = responseBase(
            for: request,
            operation: operation,
            status: status,
            schema: HelperProtocol.processResponseSchema
        )
        .merging([
            "ProcessStatusResponse": payload
        ]) { _, new in new }

        if let outputEvent = result.outputEvent {
            response["PayloadSchema"] = ["Value": HelperProtocol.processOutputEventSchema]
            response["ProcessOutputEvent"] = outputEvent
        }

        if let diagnostic = result.diagnostic {
            response["Error"] = [
                "Code": diagnostic.code,
                "Message": diagnostic.message,
                "Operation": operation.wireName,
                "Retryable": true,
                "FailedPhase": "GuestProcess",
                "Severity": diagnostic.severity
            ]
        }

        return response
    }

    private func projectionSyncResponse(
        for request: HelperEnvelope,
        result: ProjectionSyncResult
    ) -> [String: Any] {
        var response = responseBase(
            for: request,
            operation: .projectionSync,
            status: result.diagnostic == nil ? .ok : .error,
            schema: HelperProtocol.projectionSyncResponseSchema
        )
        .merging([
            "ProjectionSyncResult": result.toJson()
        ]) { _, new in new }

        if let diagnostic = result.diagnostic {
            response["Error"] = errorPayload(
                diagnostic,
                operation: .projectionSync,
                retryable: diagnostic.code == "AppleVirtualization.GuestAgentNotReady" ||
                    diagnostic.code == "AppleVirtualization.ProjectionNotVerified" ||
                    diagnostic.code == "AppleVirtualization.ProjectionStaleGeneration",
                failedPhase: "ProjectionSync")
        }

        return response
    }

    private func projectionFinalizationResponse(
        for request: HelperEnvelope,
        result: ProjectionFinalizationResult
    ) -> [String: Any] {
        var response = responseBase(
            for: request,
            operation: .projectionFinalize,
            status: result.diagnostic == nil ? .ok : .error,
            schema: HelperProtocol.projectionFinalizationResponseSchema
        )
        .merging([
            "ProjectionFinalizationResult": result.toJson()
        ]) { _, new in new }

        if let diagnostic = result.diagnostic {
            response["Error"] = errorPayload(
                diagnostic,
                operation: .projectionFinalize,
                retryable: diagnostic.code == "AppleVirtualization.GuestAgentNotReady" ||
                    diagnostic.code == "AppleVirtualization.ProjectionNotVerified" ||
                    diagnostic.code == "AppleVirtualization.ProjectionStaleGeneration",
                failedPhase: "ProjectionFinalization")
        }

        return response
    }

    private func projectionChangeEnumerationResponse(
        for request: HelperEnvelope,
        result: ProjectionChangeEnumerationResult
    ) -> [String: Any] {
        responseBase(
            for: request,
            operation: .projectionEnumerateChanges,
            status: .ok,
            schema: HelperProtocol.projectionSyncResponseSchema
        )
        .merging([
            "ProjectionChangeEnumerationResult": result.toJson()
        ]) { _, new in new }
    }

    private func projectionPromotionResponse(
        for request: HelperEnvelope,
        result: ProjectionPromotionResult
    ) -> [String: Any] {
        var response = responseBase(
            for: request,
            operation: .projectionPromote,
            status: result.diagnostic == nil ? .ok : .error,
            schema: HelperProtocol.projectionSyncResponseSchema
        )
        .merging([
            "ProjectionPromotionResult": result.toJson()
        ]) { _, new in new }

        if let diagnostic = result.diagnostic {
            response["Error"] = errorPayload(
                diagnostic,
                operation: .projectionPromote,
                retryable: diagnostic.code == "AppleVirtualization.GuestAgentNotReady" ||
                    diagnostic.code == "AppleVirtualization.ProjectionNotVerified" ||
                    diagnostic.code == "AppleVirtualization.ProjectionStaleGeneration",
                failedPhase: "ProjectionPromotion")
        }

        return response
    }

    private func networkStatusResponse(
        for request: HelperEnvelope,
        result: NetworkStatusResult
    ) -> [String: Any] {
        responseBase(
            for: request,
            operation: .networkStatus,
            status: .ok,
            schema: HelperProtocol.networkStatusResponseSchema
        )
        .merging([
            "NetworkStatusResponse": result.toJson()
        ]) { _, new in new }
    }

    private func endpointPublicationResponse(
        for request: HelperEnvelope,
        operation: Operation,
        result: EndpointPublicationResult
    ) -> [String: Any] {
        responseBase(
            for: request,
            operation: operation,
            status: .ok,
            schema: HelperProtocol.endpointPublicationResponseSchema
        )
        .merging([
            "EndpointPublicationResponse": result.toJson()
        ]) { _, new in new }
    }

    private func authorityBindingResponse(for request: HelperEnvelope, operation: Operation) -> [String: Any] {
        let payload = request.raw["AuthorityBindingRequest"] as? [String: Any] ?? [:]
        if let guestResponse = adapter.authorityBinding(payload, operation: operation) {
            if let error = guestResponse["Error"] as? [String: Any] {
                let code = VmConfigurationValidationRequest.string(error["Code"]) ?? "AppleVirtualization.AuthorityBindingGuestAgentFailed"
                let message = VmConfigurationValidationRequest.string(error["Message"]) ?? "Guest agent failed authority binding operation."
                return errorResponse(
                    for: request,
                    operation: operation,
                    code: code,
                    message: message,
                    retryable: false,
                    failedPhase: "GuestAuthority")
            }

            if let authorityResponse = guestResponse["AuthorityBindingResponse"] as? [String: Any] {
                return responseBase(for: request, operation: operation, status: .ok, schema: HelperProtocol.authorityBindingResponseSchema)
                    .merging(["AuthorityBindingResponse": authorityResponse]) { _, new in new }
            }
        }

        if !adapter.allowsSyntheticAuthorityFallback {
            return errorResponse(
                for: request,
                operation: operation,
                code: "AppleVirtualization.AuthorityBindingGuestAgentFailed",
                message: "Authority operation requires a running guest agent; no guest authority response was available.",
                retryable: true,
                failedPhase: "GuestAuthority")
        }

        let bindingId = VmConfigurationValidationRequest.string(payload["BindingId"]) ?? "unknown"
        let source = payload["Source"] as? [String: Any] ?? [:]
        let target = payload["Target"] as? [String: Any] ?? [:]
        let projection = payload["Projection"] as? [String: Any] ?? [:]
        let sourceKind = VmConfigurationValidationRequest.int(source["Kind"]) ?? 8
        let targetKind = VmConfigurationValidationRequest.int(target["Kind"]) ?? 4
        let projectionKind = VmConfigurationValidationRequest.int(projection["Kind"]) ?? 6
        let authorityClass = VmConfigurationValidationRequest.int(payload["EffectiveAuthorityClass"]) ??
            VmConfigurationValidationRequest.int(source["AuthorityClass"]) ?? 14
        let redaction = VmConfigurationValidationRequest.int(payload["Redaction"]) ?? 1
        let auditCorrelationId = VmConfigurationValidationRequest.string(payload["AuditCorrelationId"]) ?? "authority-\(bindingId)"
        let revoked = operation == .authorityRevoke
        let revocationEvidence = revoked ? authorityRevocationEvidence(payload: payload, projection: projection) : []
        let phase = revoked ? 4 : 2
        let revocationStatus = revoked ? authorityRevocationStatus(evidence: revocationEvidence) : 1
        var boundAuthority: [String: Any] = [
            "BindingId": bindingId,
            "SourceKind": sourceKind,
            "ProjectionKind": projectionKind,
            "Direction": VmConfigurationValidationRequest.int(payload["Direction"]) ?? 0,
            "EffectiveAuthorityClass": authorityClass,
            "Redaction": redaction,
            "BoundAt": Self.timestamp(),
            "RotationGeneration": 0,
            "RevocationStatus": revocationStatus,
            "AuditCorrelationId": auditCorrelationId,
        ]
        if let socketPath = authorityPath(projection["TargetSocketPath"]) {
            boundAuthority["TargetSocketPath"] = ["Value": socketPath]
        }
        if let environmentName = VmConfigurationValidationRequest.string(projection["EnvironmentVariableName"]) {
            boundAuthority["EnvironmentVariableName"] = environmentName
        }
        if let endpointKind = VmConfigurationValidationRequest.int(source["SensitiveEndpointKind"]) {
            boundAuthority["SensitiveEndpointKind"] = endpointKind
        }

        let audit: [String: Any] = [
            "Kind": revoked ? 5 : 0,
            "SourceKind": sourceKind,
            "TargetKind": targetKind,
            "Timestamp": Self.timestamp(),
            "CorrelationId": auditCorrelationId,
        ]

        return responseBase(for: request, operation: operation, status: .ok, schema: HelperProtocol.authorityBindingResponseSchema)
            .merging([
                "AuthorityBindingResponse": [
                    "BindingId": bindingId,
                    "BindingPhase": phase,
                    "BoundAuthority": boundAuthority,
                    "RevocationStatus": revocationStatus,
                    "RevocationEvidence": revocationEvidence,
                    "AuditEvents": [audit],
                    "AuditEventsTruncated": false,
                    "Limitations": [],
                    "Conditions": [],
                    "Diagnostics": []
                ]
            ]) { _, new in new }
    }

    private func authorityRevocationEvidence(payload: [String: Any], projection: [String: Any]) -> [[String: Any]] {
        let now = Self.timestamp()
        let port = VmConfigurationValidationRequest.int(payload["VirtioSocketPort"])
        let descriptor = VmConfigurationValidationRequest.int(payload["ObservedFileDescriptor"])
        let guestSocketPath = authorityPath(projection["TargetSocketPath"])

        if let descriptor {
            return [authorityEvidence(
                kind: descriptor == -1 ? 2 : 5,
                observed: true,
                observedAt: now,
                port: port,
                fileDescriptor: descriptor,
                guestSocketPath: guestSocketPath,
                detail: descriptor == -1
                    ? "VZVirtioSocketConnection.fileDescriptor was observed closed."
                    : "VZVirtioSocketConnection.fileDescriptor was still open after revoke.")]
        }

        if let guestSocketPresent = VmConfigurationValidationRequest.bool(payload["GuestSocketPresent"]) {
            return [authorityEvidence(
                kind: guestSocketPresent ? 6 : 3,
                observed: true,
                observedAt: now,
                port: port,
                fileDescriptor: nil,
                guestSocketPath: guestSocketPath,
                detail: guestSocketPresent
                    ? "Guest socket was still present after revoke."
                    : "Guest socket was absent after revoke.")]
        }

        return [authorityEvidence(
            kind: 7,
            observed: true,
            observedAt: now,
            port: port,
            fileDescriptor: nil,
            guestSocketPath: guestSocketPath,
            detail: "hpd-vz did not have observable listener, connection descriptor, or guest socket state for this revoke.")]
    }

    private func authorityEvidence(
        kind: Int,
        observed: Bool,
        observedAt: String,
        port: Int?,
        fileDescriptor: Int?,
        guestSocketPath: String?,
        detail: String
    ) -> [String: Any] {
        var evidence: [String: Any] = [
            "EvidenceProtocolVersion": "v1",
            "Kind": kind,
            "Observed": observed,
            "Detail": detail,
            "ObservedAt": observedAt
        ]
        if let port, port >= 0 {
            evidence["VirtioSocketPort"] = port
        }
        if let fileDescriptor {
            evidence["FileDescriptor"] = fileDescriptor
        }
        if let guestSocketPath {
            evidence["GuestSocketPath"] = ["Value": guestSocketPath]
        }
        return evidence
    }

    private func authorityPath(_ value: Any?) -> String? {
        if let path = VmConfigurationValidationRequest.string(value) {
            return path
        }
        if let object = value as? [String: Any] {
            return VmConfigurationValidationRequest.string(object["Value"]) ??
                VmConfigurationValidationRequest.string(object["Path"])
        }
        return nil
    }

    private func authorityRevocationStatus(evidence: [[String: Any]]) -> Int {
        var sawVerified = false
        var sawUnsupported = false

        for item in evidence.prefix(4) {
            guard VmConfigurationValidationRequest.bool(item["Observed"]) == true else {
                continue
            }

            switch VmConfigurationValidationRequest.int(item["Kind"]) {
            case 4, 5, 6:
                return 3
            case 1, 2, 3:
                sawVerified = true
            case 7:
                sawUnsupported = true
            default:
                continue
            }
        }

        if sawVerified {
            return 2
        }

        return sawUnsupported ? 4 : 1
    }

    private func errorResponse(
        for request: HelperEnvelope,
        operation: Operation?,
        code: String,
        message: String,
        retryable: Bool,
        failedPhase: String
    ) -> [String: Any] {
        responseBase(
            for: request,
            operation: operation,
            status: .error,
            schema: HelperProtocol.errorSchema
        )
        .merging([
            "Error": [
                "Code": code,
                "Message": message,
                "Operation": operation?.wireName ?? "unknown",
                "Retryable": retryable,
                "FailedPhase": failedPhase,
                "Severity": 4
            ]
        ]) { _, new in new }
    }

    private func errorPayload(
        _ diagnostic: VmConfigurationValidationDiagnostic,
        operation: Operation,
        retryable: Bool,
        failedPhase: String
    ) -> [String: Any] {
        [
            "Code": diagnostic.code,
            "Message": diagnostic.message,
            "Operation": operation.wireName,
            "Retryable": retryable,
            "FailedPhase": failedPhase,
            "Severity": diagnostic.severity
        ]
    }

    private func responseBase(
        for request: HelperEnvelope,
        operation: Operation?,
        status: ResponseStatus,
        schema: String
    ) -> [String: Any] {
        sequenceNumber += 1
        var response: [String: Any] = [
            "ProtocolVersion": HelperProtocol.currentVersion,
            "MessageType": MessageType.response.rawValue,
            "Operation": operation?.rawValue ?? request.raw["Operation"] ?? Operation.endpointUnsupported.rawValue,
            "SequenceNumber": sequenceNumber,
            "Timestamp": Self.timestamp(),
            "ResponseStatus": status.rawValue,
            "ProviderGeneration": providerGeneration,
            "PayloadSchema": ["Value": schema]
        ]

        if let requestId = request.requestId {
            response["RequestId"] = requestId
            response["CausationId"] = requestId
        }

        return response
    }

    private static func timestamp() -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: Date())
    }

    private func hostPhase(for state: HostLifecycleState) -> Int {
        switch state {
        case .notCreated:
            return 9
        case .starting:
            return 4
        case .running:
            return 5
        case .stopping:
            return 8
        case .stopped:
            return 9
        case .failed:
            return 13
        }
    }

    private func resourcePhase(for state: HostLifecycleState) -> Int {
        switch state {
        case .notCreated, .stopped:
            return 1
        case .starting, .stopping:
            return 2
        case .running:
            return 3
        case .failed:
            return 5
        }
    }
}
