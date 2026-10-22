import Foundation

private enum Language: String {
    case german = "German"
    case japanese = "Japanese"
}

private struct ModelCatalog: Decodable {
    struct Model: Decodable { let slug: String; let visibility: String? }
    let models: [Model]
}

private struct StreamEvent: Decodable {
    let type: String
    let delta: String?
    let response: FailedResponse?
    struct FailedResponse: Decodable {
        let error: APIError?
        struct APIError: Decodable { let message: String?; let code: String? }
    }
}

@MainActor
struct OpenAITranslator {
    let auth: ChatGPTAuthStore

    func translate(_ text: String, from: Talker, to: Talker) async throws -> String {
        let token = try await auth.validAccessToken()
        let model = try await preferredModel(token: token)
        let source: Language = from == .german ? .german : .japanese
        let target: Language = to == .german ? .german : .japanese
        let instruction = "Translate the user's text from \(source.rawValue) to \(target.rawValue). Preserve meaning and tone. Return only the translation, with no explanations."
        let payload: [String: Any] = [
            "model": model,
            "instructions": instruction,
            "input": [["role": "user", "content": [["type": "input_text", "text": text]]]],
            "store": false,
            "stream": true
        ]
        var request = URLRequest(url: URL(string: "https://api.openai.com/v1/responses")!)
        request.httpMethod = "POST"
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("text/event-stream", forHTTPHeaderField: "Accept")
        request.httpBody = try JSONSerialization.data(withJSONObject: payload)
        let (bytes, response) = try await URLSession.shared.bytes(for: request)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            throw KoeTranslationError.requestFailed
        }
        var output = ""
        var eventData = ""
        var completed = false
        for try await line in bytes.lines {
            if line.isEmpty {
                if !eventData.isEmpty {
                    let event = try JSONDecoder().decode(StreamEvent.self, from: Data(eventData.utf8))
                    if event.type == "response.output_text.delta", let delta = event.delta { output += delta }
                    if event.type == "response.failed" {
                        throw KoeTranslationError.api(event.response?.error?.message ?? event.response?.error?.code ?? "Unbekannter API-Fehler")
                    }
                    if event.type == "response.completed" { completed = true }
                    eventData = ""
                }
            } else if line.hasPrefix("data:") {
                eventData = String(line.dropFirst(5)).trimmingCharacters(in: .whitespaces)
            }
        }
        guard completed else { throw KoeTranslationError.incompleteStream }
        let result = output.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !result.isEmpty else { throw KoeTranslationError.emptyTranslation }
        return result
    }

    private func preferredModel(token: String) async throws -> String {
        var request = URLRequest(url: URL(string: "https://api.openai.com/v1/models")!)
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else { throw KoeTranslationError.modelListFailed }
        let catalog = try JSONDecoder().decode(ModelCatalog.self, from: data)
        guard let selected = catalog.models.first(where: { $0.visibility == "list" })?.slug else { throw KoeTranslationError.noAvailableModel }
        return selected
    }
}

enum KoeTranslationError: LocalizedError {
    case requestFailed, modelListFailed, noAvailableModel, incompleteStream, emptyTranslation, api(String)
    var errorDescription: String? {
        switch self {
        case .requestFailed: return "Die Übersetzungsanfrage wurde abgelehnt."
        case .modelListFailed: return "Die verfügbaren ChatGPT-Modelle konnten nicht geladen werden."
        case .noAvailableModel: return "Für dieses ChatGPT-Konto wurde kein Modell zur Nutzung angezeigt."
        case .incompleteStream: return "Die Antwort wurde unterbrochen, bevor sie fertig war."
        case .emptyTranslation: return "Es kam kein übersetzter Text zurück."
        case .api(let message): return "OpenAI: \(message)"
        }
    }
}
