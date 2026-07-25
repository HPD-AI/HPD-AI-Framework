// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "hpd-vz",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(name: "hpd-vz", targets: ["hpd-vz"]),
        .executable(name: "hpd-vz-routing-tests", targets: ["hpd-vz-routing-tests"]),
        .library(name: "HPDVZCore", targets: ["HPDVZCore"])
    ],
    targets: [
        .target(name: "HPDVZCore"),
        .executableTarget(
            name: "hpd-vz",
            dependencies: ["HPDVZCore"]
        ),
        .executableTarget(
            name: "hpd-vz-routing-tests",
            dependencies: ["HPDVZCore"],
            path: "Tests/HPDVZCoreRoutingTests"
        )
    ]
)
