import Foundation
import HPDVZCore

if CommandLine.arguments.contains("--generate-realization-identity") {
    do {
        let identity = try VirtualMachineIdentityFactory.generate()
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        var bytes = try encoder.encode(identity)
        bytes.append(0x0A)
        try FileHandle.standardOutput.write(contentsOf: bytes)
        exit(EXIT_SUCCESS)
    } catch {
        try? FileHandle.standardError.write(
            contentsOf: Data(
                "hpd-vz identity generation failed: \(error)\n".utf8))
        exit(EXIT_FAILURE)
    }
}

let adapter: VirtualizationAdapter
if CommandLine.arguments.contains("--fake") || CommandLine.arguments.contains("--local") {
    adapter = FakeVirtualizationAdapter()
} else {
    adapter = LocalVirtualizationAdapter()
}

let server = StdioServer(service: HelperService(adapter: adapter))

DispatchQueue.global(qos: .userInitiated).async {
    do {
        try server.run()
        exit(EXIT_SUCCESS)
    } catch {
        try? FileHandle.standardError.write(contentsOf: Data("hpd-vz failed: \(error)\n".utf8))
        exit(EXIT_FAILURE)
    }
}

RunLoop.main.run()
