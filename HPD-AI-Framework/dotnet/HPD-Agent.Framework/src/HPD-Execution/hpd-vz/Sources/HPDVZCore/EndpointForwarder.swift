import Foundation

#if canImport(Darwin)
import Darwin
#endif

public final class EndpointForwarderManager {
    public typealias GuestProxy = @Sendable (_ targetAddress: String, _ targetPort: UInt16, _ requestBytes: [UInt8]) -> [UInt8]?

    private let lock = NSLock()
    private var forwarders: [String: EndpointForwarder] = [:]
    private let guestProxy: GuestProxy?

    public init(guestProxy: GuestProxy? = nil) {
        self.guestProxy = guestProxy
    }

    public func publish(_ request: EndpointPublicationRequest) -> EndpointPublicationResult {
        guard request.listenerKind == 0, request.transport == 0, request.exposureScope == 0 else {
            return EndpointPublicationStateFactory.publish(request)
        }

        guard let targetAddress = request.targetAddress, !targetAddress.isEmpty, let targetPort = request.targetPort else {
            return EndpointPublicationResult(
                endpointId: request.endpointId,
                endpointPhase: 4,
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
                limitations: [NetworkLimitationPayload(
                    feature: 9,
                    mode: 2,
                    reasonCode: "AppleVirtualization.EndpointTargetAddressMissing",
                    message: "Endpoint publication requires a resolved guest target address and port.")],
                diagnostics: [diagnostic(
                    code: "AppleVirtualization.EndpointTargetAddressMissing",
                    message: "Endpoint publication requires a resolved guest target address and port.",
                    targetPath: "endpoint.target")])
        }

        lock.lock()
        let existing = forwarders.removeValue(forKey: request.endpointId)
        lock.unlock()
        existing?.stop()

        do {
            let forwarder = try EndpointForwarder(
                endpointId: request.endpointId,
                listenAddress: request.listenerAddress,
                requestedPort: request.requestedPort,
                allowEphemeralPort: request.allowEphemeralPort,
                targetAddress: targetAddress,
                targetPort: targetPort,
                guestProxy: guestProxy)
            try forwarder.start()
            lock.lock()
            forwarders[request.endpointId] = forwarder
            lock.unlock()

            return EndpointPublicationResult(
                endpointId: request.endpointId,
                endpointPhase: 2,
                listenerKind: request.listenerKind,
                transport: request.transport,
                exposureScope: request.exposureScope,
                boundAddress: forwarder.boundAddress,
                boundPort: forwarder.boundPort,
                hpdOwned: true,
                routeHealthy: true,
                resolvedAddress: targetAddress,
                resolvedPort: targetPort,
                resolvedSocketPath: request.targetSocketPath,
                limitations: [],
                diagnostics: [])
        } catch {
            return EndpointPublicationResult(
                endpointId: request.endpointId,
                endpointPhase: 4,
                listenerKind: request.listenerKind,
                transport: request.transport,
                exposureScope: request.exposureScope,
                boundAddress: nil,
                boundPort: nil,
                hpdOwned: false,
                routeHealthy: false,
                resolvedAddress: targetAddress,
                resolvedPort: targetPort,
                resolvedSocketPath: request.targetSocketPath,
                limitations: [NetworkLimitationPayload(
                    feature: 9,
                    mode: 2,
                    reasonCode: "AppleVirtualization.EndpointForwarderFailed",
                    message: "The helper could not create the host-local endpoint forwarder.")],
                diagnostics: [diagnostic(
                    code: "AppleVirtualization.EndpointForwarderFailed",
                    message: "The helper could not create the host-local endpoint forwarder: \(error)",
                    targetPath: "endpoint.listener")])
        }
    }

    public func release(_ request: EndpointPublicationRequest) -> EndpointPublicationResult {
        lock.lock()
        let forwarder = forwarders.removeValue(forKey: request.endpointId)
        lock.unlock()
        forwarder?.stop()
        return EndpointPublicationStateFactory.release(request)
    }

    private static func diagnostic(code: String, message: String, targetPath: String) -> [String: Any] {
        [
            "Severity": 4,
            "Code": ["Value": code],
            "Message": message,
            "ProviderId": ["Value": "hpd.execution.apple-virtualization"],
            "TargetPath": targetPath
        ]
    }

    private func diagnostic(code: String, message: String, targetPath: String) -> [String: Any] {
        Self.diagnostic(code: code, message: message, targetPath: targetPath)
    }
}

private final class EndpointForwarder: @unchecked Sendable {
    let endpointId: String
    let boundAddress: String
    private(set) var boundPort: UInt16?

    private let targetAddress: String
    private let targetPort: UInt16
    private let guestProxy: EndpointForwarderManager.GuestProxy?
    private let lock = NSLock()
    private var listenerFd: Int32 = -1
    private var stopped = false
    private var activeSockets: Set<Int32> = []

    init(
        endpointId: String,
        listenAddress: String?,
        requestedPort: UInt16?,
        allowEphemeralPort: Bool,
        targetAddress: String,
        targetPort: UInt16,
        guestProxy: EndpointForwarderManager.GuestProxy?
    ) throws {
        self.endpointId = endpointId
        self.boundAddress = listenAddress?.isEmpty == false ? listenAddress! : "127.0.0.1"
        self.boundPort = requestedPort ?? (allowEphemeralPort ? 0 : nil)
        self.targetAddress = targetAddress
        self.targetPort = targetPort
        self.guestProxy = guestProxy
        if self.boundPort == nil {
            throw ForwarderError.portRequired
        }
    }

    func start() throws {
        #if canImport(Darwin)
        let fd = Darwin.socket(AF_INET, SOCK_STREAM, IPPROTO_TCP)
        guard fd >= 0 else {
            throw ForwarderError.posix("socket", errno)
        }

        var yes: Int32 = 1
        _ = setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, &yes, socklen_t(MemoryLayout<Int32>.size))
        #if os(macOS)
        _ = setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &yes, socklen_t(MemoryLayout<Int32>.size))
        #endif

        var address = try sockaddrIn(boundAddress, boundPort ?? 0)
        let bindResult = withUnsafePointer(to: &address) {
            $0.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                Darwin.bind(fd, $0, socklen_t(MemoryLayout<sockaddr_in>.size))
            }
        }
        guard bindResult == 0 else {
            let code = errno
            Darwin.close(fd)
            throw ForwarderError.posix("bind", code)
        }

        guard Darwin.listen(fd, 64) == 0 else {
            let code = errno
            Darwin.close(fd)
            throw ForwarderError.posix("listen", code)
        }

        var bound = sockaddr_in()
        var length = socklen_t(MemoryLayout<sockaddr_in>.size)
        let nameResult = withUnsafeMutablePointer(to: &bound) {
            $0.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                Darwin.getsockname(fd, $0, &length)
            }
        }
        if nameResult == 0 {
            boundPort = UInt16(bigEndian: bound.sin_port)
        }

        listenerFd = fd
        DispatchQueue.global(qos: .utility).async { [weak self] in
            self?.acceptLoop()
        }
        #else
        throw ForwarderError.unsupported
        #endif
    }

    func stop() {
        lock.lock()
        stopped = true
        let listener = listenerFd
        listenerFd = -1
        let sockets = activeSockets
        activeSockets.removeAll()
        lock.unlock()

        if listener >= 0 {
            Darwin.shutdown(listener, SHUT_RDWR)
            Darwin.close(listener)
        }
        for socket in sockets {
            Darwin.shutdown(socket, SHUT_RDWR)
            Darwin.close(socket)
        }
    }

    private func acceptLoop() {
        #if canImport(Darwin)
        while true {
            lock.lock()
            let shouldStop = stopped
            let fd = listenerFd
            lock.unlock()
            if shouldStop || fd < 0 {
                return
            }

            var peer = sockaddr_in()
            var length = socklen_t(MemoryLayout<sockaddr_in>.size)
            let client = withUnsafeMutablePointer(to: &peer) {
                $0.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                    Darwin.accept(fd, $0, &length)
                }
            }
            if client < 0 {
                if errno == EBADF || errno == EINVAL {
                    return
                }
                continue
            }

            DispatchQueue.global(qos: .utility).async { [weak self] in
                self?.handle(clientFd: client)
            }
        }
        #endif
    }

    private func handle(clientFd: Int32) {
        #if canImport(Darwin)
        if shouldUseGuestProxy,
           let requestBytes = Self.readClientRequest(clientFd),
           let responseBytes = guestProxy?(targetAddress, targetPort, requestBytes) {
            Self.writeAll(responseBytes, to: clientFd)
            Darwin.close(clientFd)
            return
        }

        var yes: Int32 = 1
        #if os(macOS)
        _ = setsockopt(clientFd, SOL_SOCKET, SO_NOSIGPIPE, &yes, socklen_t(MemoryLayout<Int32>.size))
        #endif

        do {
            let targetFd = try connectTarget()
            #if os(macOS)
            _ = setsockopt(targetFd, SOL_SOCKET, SO_NOSIGPIPE, &yes, socklen_t(MemoryLayout<Int32>.size))
            #endif

            track(clientFd)
            track(targetFd)
            let group = DispatchGroup()
            group.enter()
            DispatchQueue.global(qos: .utility).async {
                Self.copy(from: clientFd, to: targetFd)
                Darwin.shutdown(targetFd, SHUT_WR)
                group.leave()
            }
            group.enter()
            DispatchQueue.global(qos: .utility).async {
                Self.copy(from: targetFd, to: clientFd)
                Darwin.shutdown(clientFd, SHUT_WR)
                group.leave()
            }
            group.wait()
            untrack(clientFd)
            untrack(targetFd)
            Darwin.close(targetFd)
            Darwin.close(clientFd)
        } catch {
            Darwin.close(clientFd)
        }
        #endif
    }

    private var shouldUseGuestProxy: Bool {
        guestProxy != nil
    }

    private func connectTarget() throws -> Int32 {
        #if canImport(Darwin)
        let fd = Darwin.socket(AF_INET, SOCK_STREAM, IPPROTO_TCP)
        guard fd >= 0 else {
            throw ForwarderError.posix("socket", errno)
        }
        var address = try sockaddrIn(targetAddress, targetPort)
        let result = withUnsafePointer(to: &address) {
            $0.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                Darwin.connect(fd, $0, socklen_t(MemoryLayout<sockaddr_in>.size))
            }
        }
        guard result == 0 else {
            let code = errno
            Darwin.close(fd)
            throw ForwarderError.posix("connect", code)
        }
        return fd
        #else
        throw ForwarderError.unsupported
        #endif
    }

    private func track(_ fd: Int32) {
        lock.lock()
        activeSockets.insert(fd)
        lock.unlock()
    }

    private func untrack(_ fd: Int32) {
        lock.lock()
        activeSockets.remove(fd)
        lock.unlock()
    }

    private static func copy(from source: Int32, to destination: Int32) {
        #if canImport(Darwin)
        var buffer = [UInt8](repeating: 0, count: 32 * 1024)
        while true {
            let count = Darwin.read(source, &buffer, buffer.count)
            if count <= 0 {
                return
            }

            var written = 0
            while written < count {
                let result = buffer.withUnsafeBytes {
                    Darwin.write(destination, $0.baseAddress!.advanced(by: written), count - written)
                }
                if result <= 0 {
                    return
                }
                written += result
            }
        }
        #endif
    }

    private static func writeAll(_ bytes: [UInt8], to fd: Int32) {
        #if canImport(Darwin)
        var written = 0
        while written < bytes.count {
            let result = bytes.withUnsafeBytes {
                Darwin.write(fd, $0.baseAddress!.advanced(by: written), bytes.count - written)
            }
            if result <= 0 {
                return
            }
            written += result
        }
        #endif
    }

    private static func readClientRequest(_ fd: Int32, maxBytes: Int = 64 * 1024) -> [UInt8]? {
        #if canImport(Darwin)
        var result: [UInt8] = []
        var buffer = [UInt8](repeating: 0, count: 4096)
        let deadline = Date().addingTimeInterval(2)
        while result.count < maxBytes {
            let remaining = max(0, Int(deadline.timeIntervalSinceNow * 1000))
            if remaining <= 0 {
                break
            }

            var item = pollfd(fd: fd, events: Int16(POLLIN), revents: 0)
            let pollResult = poll(&item, 1, Int32(remaining))
            if pollResult <= 0 {
                break
            }

            let capacity = min(buffer.count, maxBytes - result.count)
            let count = buffer.withUnsafeMutableBytes {
                Darwin.read(fd, $0.baseAddress, capacity)
            }
            if count > 0 {
                result.append(contentsOf: buffer[0..<count])
                if result.containsHttpHeaderTerminator {
                    break
                }
            } else {
                break
            }
        }

        return result.isEmpty ? nil : result
        #else
        return nil
        #endif
    }
}

private extension Array where Element == UInt8 {
    var containsHttpHeaderTerminator: Bool {
        guard count >= 4 else {
            return false
        }

        for index in 3..<count {
            if self[index - 3] == 13 &&
                self[index - 2] == 10 &&
                self[index - 1] == 13 &&
                self[index] == 10 {
                return true
            }
        }

        return false
    }
}

private enum ForwarderError: Error, CustomStringConvertible {
    case portRequired
    case unsupported
    case invalidAddress(String)
    case posix(String, Int32)

    var description: String {
        switch self {
        case .portRequired:
            return "listener port is required"
        case .unsupported:
            return "endpoint forwarding is unsupported on this platform"
        case .invalidAddress(let address):
            return "invalid IPv4 address \(address)"
        case .posix(let operation, let code):
            return "\(operation) failed with errno \(code)"
        }
    }
}

#if canImport(Darwin)
private func sockaddrIn(_ address: String, _ port: UInt16) throws -> sockaddr_in {
    var result = sockaddr_in()
    result.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
    result.sin_family = sa_family_t(AF_INET)
    result.sin_port = port.bigEndian
    guard inet_pton(AF_INET, address, &result.sin_addr) == 1 else {
        throw ForwarderError.invalidAddress(address)
    }
    return result
}
#endif
