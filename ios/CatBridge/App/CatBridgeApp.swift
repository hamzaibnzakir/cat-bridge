import SwiftUI
import CatBridgeCore

@main
struct CatBridgeApp: App {
    @State private var model = AppModel.shared

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(model)
                .preferredColorScheme(.dark)
                .tint(Theme.accent)
                .onOpenURL { url in model.handle(url: url) }
        }
    }
}
