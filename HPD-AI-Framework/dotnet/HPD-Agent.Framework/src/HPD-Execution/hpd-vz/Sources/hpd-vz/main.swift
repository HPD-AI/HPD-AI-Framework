import Foundation
import HPDVZCore

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
