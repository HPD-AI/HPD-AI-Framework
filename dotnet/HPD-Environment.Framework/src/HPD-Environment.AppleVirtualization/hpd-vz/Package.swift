// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "hpd-vz",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(name: "hpd-vz", targets: ["hpd-vz"]),
        .library(name: "HPDVZCore", targets: ["HPDVZCore"])
    ],
    targets: [
        .target(name: "HPDVZCore"),
        .executableTarget(
            name: "hpd-vz",
            dependencies: ["HPDVZCore"]
        )
    ]
)
