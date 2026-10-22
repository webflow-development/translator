import SwiftUI

@main
struct KoeApp: App {
    @StateObject private var auth = ChatGPTAuthStore()

    var body: some Scene {
        WindowGroup {
            ConversationView()
                .environmentObject(auth)
                .tint(Color(red: 0.20, green: 0.43, blue: 0.88))
        }
    }
}
