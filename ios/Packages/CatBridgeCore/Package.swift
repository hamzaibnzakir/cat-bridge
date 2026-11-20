// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "CatBridgeCore",
    platforms: [.iOS(.v17), .macOS(.v14)],
    products: [.library(name: "CatBridgeCore", targets: ["CatBridgeCore"])],
    targets: [
        .target(name: "CatBridgeCore"),
        .testTarget(name: "CatBridgeCoreTests", dependencies: ["CatBridgeCore"]),
    ]
)
