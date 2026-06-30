import Foundation

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
        while let line = try input.readLineData() {
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

extension FileHandle {
    func readLineData() throws -> Data? {
        var data = Data()
        while true {
            let chunk = try read(upToCount: 1)
            guard let byte = chunk?.first else {
                return data.isEmpty ? nil : data
            }

            if byte == 0x0A {
                return data
            }

            data.append(byte)
        }
    }

    func writeAll(_ data: Data) throws {
        try write(contentsOf: data)
    }
}
