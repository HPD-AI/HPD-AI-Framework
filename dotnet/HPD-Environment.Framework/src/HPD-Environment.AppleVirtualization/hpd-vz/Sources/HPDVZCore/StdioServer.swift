import Foundation
import Darwin

public final class StdioServer: @unchecked Sendable {
    private let service: HelperService
    private let input: FileHandle
    private let output: FileHandle
    private let errorOutput: FileHandle

    public init(
        service: HelperService,
        input: FileHandle = .standardInput,
        output: FileHandle = .standardOutput,
        errorOutput: FileHandle = .standardError
    ) {
        self.service = service
        self.input = input
        self.output = output
        self.errorOutput = errorOutput
    }

    public func run() throws {
        let reader = BoundedLineReader(input: input)
        while let line = try reader.readLineData() {
            try autoreleasepool {
                do {
                    let request = try ProtocolCodec.decodeLine(line)
                    let response = service.handle(request)
                    try output.writeAll(ProtocolCodec.encodeLine(response))
                } catch {
                    let response = service.malformedFrameResponse(message: "Malformed helper protocol frame: \(error)")
                    try output.writeAll(ProtocolCodec.encodeLine(response))
                    try errorOutput.writeAll(Data("hpd-vz protocol error: \(error)\n".utf8))
                }
            }
        }
    }
}

@_spi(Testing) public final class BoundedLineReader {
    private static let maximumLineBytes = 128 * 1024
    private static let readChunkBytes = 16 * 1024
    private let input: FileHandle
    private var buffered = Data()
    private var readBuffer = [UInt8](
        repeating: 0,
        count: BoundedLineReader.readChunkBytes)

    @_spi(Testing) public init(input: FileHandle) {
        self.input = input
    }

    @_spi(Testing) public func readLineData() throws -> Data? {
        while true {
            if let newline = buffered.firstIndex(of: 0x0A) {
                guard newline <= Self.maximumLineBytes else {
                    throw ProtocolError.invalidFrame("Helper protocol frame exceeds 131072 bytes.")
                }
                let line = Data(buffered[..<newline])
                buffered.removeSubrange(...newline)
                return line
            }
            guard buffered.count <= Self.maximumLineBytes else {
                throw ProtocolError.invalidFrame("Helper protocol frame exceeds 131072 bytes.")
            }
            let count = readBuffer.withUnsafeMutableBytes { bytes in
                Darwin.read(input.fileDescriptor, bytes.baseAddress, bytes.count)
            }
            if count < 0 {
                if errno == EINTR { continue }
                throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
            }
            guard count != 0 else {
                guard !buffered.isEmpty else { return nil }
                let line = buffered
                buffered.removeAll(keepingCapacity: false)
                return line
            }
            buffered.append(contentsOf: readBuffer[..<count])
        }
    }
}

extension FileHandle {
    func writeAll(_ data: Data) throws {
        try write(contentsOf: data)
    }
}
