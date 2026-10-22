import AuthenticationServices
import CryptoKit
import Foundation
import Network
import Security
import SwiftUI
import UIKit

struct OAuthCredential: Codable {
    var clientID: String
    var accessToken: String
    var refreshToken: String
    var idToken: String
    var email: String?
    var subject: String
    var scopes: [String]
    var expiresAt: Date
}

@MainActor
final class ChatGPTAuthStore: ObservableObject {
    @Published private(set) var credential: OAuthCredential?
    private let storage = CredentialKeychain()
    var isConnected: Bool { credential?.scopes.contains("chatgpt.tokens.use.direct") == true }

    init() { credential = try? storage.load() }

    func signIn() async throws {
        let hostID = HostIdentity.value
        let listener = try await LoopbackListener.start()
        defer { listener.stop() }

        let state = RandomValue.make()
        let nonce = RandomValue.make()
        let verifier = RandomValue.make(byteCount: 48)
        let challenge = Data(SHA256.hash(data: Data(verifier.utf8))).base64URL
        let redirect = "http://127.0.0.1:\(listener.port)/auth/callback"
        let existingCredential = credential
        let requestedClientID = existingCredential?.clientID ?? "dynamic_agent_client"

        var components = URLComponents(string: "https://auth.openai.com/api/accounts/authorize")!
        var queryItems = [
            URLQueryItem(name: "client_id", value: requestedClientID),
            .init(name: "ext_agent_host_id", value: hostID),
            .init(name: "response_type", value: "code"),
            .init(name: "redirect_uri", value: redirect),
            .init(name: "scope", value: "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct"),
            .init(name: "resource", value: "https://api.openai.com/v1"),
            .init(name: "state", value: state),
            .init(name: "nonce", value: nonce),
            .init(name: "code_challenge_method", value: "S256"),
            .init(name: "code_challenge", value: challenge)
        ]
        if existingCredential == nil {
            queryItems.append(.init(name: "agent_name_hint", value: "Koe"))
        } else {
            queryItems.append(.init(name: "id_token_hint", value: existingCredential?.idToken))
            if let email = existingCredential?.email { queryItems.append(.init(name: "login_hint", value: email)) }
        }
        components.queryItems = queryItems
        guard let authURL = components.url else { throw KoeError.invalidAuthorizationURL }
        let callback = try await BrowserAuth.run(url: authURL)
        guard callback["state"] == state else { throw KoeError.oauthStateMismatch }
        if let error = callback["error"] { throw KoeError.oauth(error) }
        guard let code = callback["code"],
              let issuedClientID = callback["client_id"] ?? existingCredential?.clientID else {
            throw KoeError.missingOAuthResult
        }
        if let existingCredential, issuedClientID != existingCredential.clientID { throw KoeError.accountChanged }

        let tokens = try await TokenEndpoint.exchange(code: code, verifier: verifier, redirectURI: redirect, clientID: issuedClientID)
        guard tokens.scopes.contains("chatgpt.tokens.use.direct") else { throw KoeError.planPermissionMissing }
        let identity = try await OpenIDTokenVerifier.verify(tokens.idToken, clientID: issuedClientID, nonce: nonce)
        if let existingCredential, identity.subject != existingCredential.subject { throw KoeError.accountChanged }
        let newCredential = OAuthCredential(
            clientID: issuedClientID,
            accessToken: tokens.accessToken,
            refreshToken: tokens.refreshToken,
            idToken: tokens.idToken,
            email: identity.email,
            subject: identity.subject,
            scopes: tokens.scopes,
            expiresAt: Date().addingTimeInterval(TimeInterval(tokens.expiresIn))
        )
        try storage.save(newCredential)
        credential = newCredential
    }

    func validAccessToken() async throws -> String {
        guard var current = credential else { throw KoeError.notConnected }
        if current.expiresAt.timeIntervalSinceNow > 90 { return current.accessToken }
        let tokens = try await TokenEndpoint.refresh(refreshToken: current.refreshToken, clientID: current.clientID)
        let identity = try await OpenIDTokenVerifier.verify(tokens.idToken, clientID: current.clientID, nonce: nil)
        guard identity.subject == current.subject else { throw KoeError.accountChanged }
        current.accessToken = tokens.accessToken
        current.refreshToken = tokens.refreshToken
        current.idToken = tokens.idToken
        current.scopes = tokens.scopes.isEmpty ? current.scopes : tokens.scopes
        current.expiresAt = Date().addingTimeInterval(TimeInterval(tokens.expiresIn))
        try storage.save(current)
        credential = current
        return current.accessToken
    }

    func disconnect() {
        try? storage.delete()
        credential = nil
    }
}

private enum TokenEndpoint {
    struct Tokens { let accessToken: String; let refreshToken: String; let idToken: String; let expiresIn: Int; let scopes: [String] }
    private struct Response: Decodable {
        let access_token: String
        let refresh_token: String?
        let id_token: String
        let expires_in: Int
        let scope: String?
    }

    static func exchange(code: String, verifier: String, redirectURI: String, clientID: String) async throws -> Tokens {
        try await post([
            "grant_type": "authorization_code", "client_id": clientID,
            "code": code, "code_verifier": verifier, "redirect_uri": redirectURI,
            "resource": "https://api.openai.com/v1"
        ], previousRefresh: nil)
    }

    static func refresh(refreshToken: String, clientID: String) async throws -> Tokens {
        try await post([
            "grant_type": "refresh_token", "client_id": clientID,
            "refresh_token": refreshToken, "resource": "https://api.openai.com/v1"
        ], previousRefresh: refreshToken)
    }

    private static func post(_ values: [String: String], previousRefresh: String?) async throws -> Tokens {
        var request = URLRequest(url: URL(string: "https://auth.openai.com/api/accounts/oauth/token")!)
        request.httpMethod = "POST"
        request.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        var form = URLComponents()
        form.queryItems = values.map { URLQueryItem(name: $0.key, value: $0.value) }
        request.httpBody = form.percentEncodedQuery?.data(using: .utf8)
        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            throw KoeError.tokenExchangeFailed
        }
        let decoded = try JSONDecoder().decode(Response.self, from: data)
        return Tokens(
            accessToken: decoded.access_token,
            refreshToken: decoded.refresh_token ?? previousRefresh ?? "",
            idToken: decoded.id_token,
            expiresIn: decoded.expires_in,
            scopes: (decoded.scope ?? "").split(separator: " ").map(String.init)
        )
    }
}

private struct Identity { let subject: String; let email: String? }

private enum OpenIDTokenVerifier {
    private struct Header: Decodable { let kid: String; let alg: String }
    private struct Claims: Decodable {
        let iss: String
        let aud: Audience
        let sub: String
        let exp: TimeInterval
        let nonce: String?
        let email: String?
        enum Audience: Decodable {
            case one(String), many([String])
            init(from decoder: Decoder) throws {
                let c = try decoder.singleValueContainer()
                if let s = try? c.decode(String.self) { self = .one(s) }
                else { self = .many(try c.decode([String].self)) }
            }
            func includes(_ item: String) -> Bool {
                switch self {
                case .one(let value): return value == item
                case .many(let values): return values.contains(item)
                }
            }
        }
    }
    private struct KeySet: Decodable { let keys: [JWK] }
    private struct JWK: Decodable { let kid: String; let kty: String; let n: String?; let e: String?; let alg: String? }

    static func verify(_ token: String, clientID: String, nonce: String?) async throws -> Identity {
        let parts = token.split(separator: ".")
        guard parts.count == 3,
              let headerData = Data(base64URL: String(parts[0])),
              let claimsData = Data(base64URL: String(parts[1])),
              let signature = Data(base64URL: String(parts[2])) else { throw KoeError.invalidIDToken }
        let header = try JSONDecoder().decode(Header.self, from: headerData)
        let claims = try JSONDecoder().decode(Claims.self, from: claimsData)
        guard header.alg == "RS256",
              claims.iss == "https://auth.openai.com",
              claims.aud.includes(clientID),
              claims.exp > Date().timeIntervalSince1970,
              nonce == nil || claims.nonce == nonce else { throw KoeError.invalidIDToken }

        let (data, _) = try await URLSession.shared.data(from: URL(string: "https://auth.openai.com/.well-known/jwks.json")!)
        let keySet = try JSONDecoder().decode(KeySet.self, from: data)
        guard let jwk = keySet.keys.first(where: { $0.kid == header.kid && $0.kty == "RSA" }),
              let modulus = jwk.n.flatMap(Data.init(base64URL:)),
              let exponent = jwk.e.flatMap(Data.init(base64URL:)) else { throw KoeError.invalidIDToken }
        let der = RSAKeyDER.subjectPublicKeyInfo(modulus: modulus, exponent: exponent)
        let attributes: [String: Any] = [
            kSecAttrKeyType as String: kSecAttrKeyTypeRSA,
            kSecAttrKeyClass as String: kSecAttrKeyClassPublic,
            kSecAttrKeySizeInBits as String: modulus.count * 8
        ]
        var unmanagedError: Unmanaged<CFError>?
        guard let key = SecKeyCreateWithData(der as CFData, attributes as CFDictionary, &unmanagedError) else {
            throw KoeError.invalidIDToken
        }
        let signed = Data("\(parts[0]).\(parts[1])".utf8)
        guard SecKeyVerifySignature(key, .rsaSignatureMessagePKCS1v15SHA256, signed as CFData, signature as CFData, &unmanagedError) else {
            throw KoeError.invalidIDToken
        }
        return Identity(subject: claims.sub, email: claims.email)
    }
}

private enum RSAKeyDER {
    static func subjectPublicKeyInfo(modulus: Data, exponent: Data) -> Data {
        let rsa = sequence(integer(modulus) + integer(exponent))
        let algorithm = Data([0x30, 0x0d, 0x06, 0x09, 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x01, 0x05, 0x00])
        let bitString = element(0x03, Data([0x00]) + rsa)
        return sequence(algorithm + bitString)
    }
    private static func integer(_ data: Data) -> Data {
        let stripped = Data(data.drop(while: { $0 == 0 }))
        let positive = (stripped.first ?? 0) & 0x80 == 0 ? stripped : Data([0]) + stripped
        return element(0x02, positive)
    }
    private static func sequence(_ data: Data) -> Data { element(0x30, data) }
    private static func element(_ tag: UInt8, _ data: Data) -> Data { Data([tag]) + length(data.count) + data }
    private static func length(_ value: Int) -> Data {
        if value < 128 { return Data([UInt8(value)]) }
        var number = value
        var bytes: [UInt8] = []
        while number > 0 { bytes.insert(UInt8(number & 0xff), at: 0); number >>= 8 }
        return Data([0x80 | UInt8(bytes.count)] + bytes)
    }
}

private final class CredentialKeychain {
    private let service = "ch.paulo.koe.openai"
    private let account = "oauth-credential"
    func load() throws -> OAuthCredential? {
        var query = baseQuery
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess, let data = result as? Data else { throw KoeError.keychain(status) }
        return try JSONDecoder().decode(OAuthCredential.self, from: data)
    }
    func save(_ credential: OAuthCredential) throws {
        let data = try JSONEncoder().encode(credential)
        let status = SecItemUpdate(baseQuery as CFDictionary, [kSecValueData as String: data] as CFDictionary)
        if status == errSecItemNotFound {
            var query = baseQuery
            query[kSecValueData as String] = data
            query[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
            let addStatus = SecItemAdd(query as CFDictionary, nil)
            guard addStatus == errSecSuccess else { throw KoeError.keychain(addStatus) }
        } else if status != errSecSuccess { throw KoeError.keychain(status) }
    }
    func delete() throws {
        let status = SecItemDelete(baseQuery as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else { throw KoeError.keychain(status) }
    }
    private var baseQuery: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
    }
}

private enum HostIdentity {
    static var value: String {
        let key = "ch.paulo.koe.host-id"
        if let saved = UserDefaults.standard.string(forKey: key) { return saved }
        let newValue = "urn:uuid:\(UUID().uuidString.lowercased())"
        UserDefaults.standard.set(newValue, forKey: key)
        return newValue
    }
}

private enum RandomValue {
    static func make(byteCount: Int = 32) -> String {
        var bytes = [UInt8](repeating: 0, count: byteCount)
        _ = SecRandomCopyBytes(kSecRandomDefault, byteCount, &bytes)
        return Data(bytes).base64URL
    }
}

private final class LoopbackListener {
    let port: UInt16 = 1455
    private let listener: NWListener
    private var callback: (([String: String]) -> Void)?
    private init(listener: NWListener) { self.listener = listener }

    static func start() async throws -> LoopbackListener {
        let parameters = NWParameters.tcp
        parameters.requiredLocalEndpoint = .hostPort(host: "127.0.0.1", port: 1455)
        let listener = try NWListener(using: parameters)
        let wrapper = LoopbackListener(listener: listener)
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            var resumed = false
            listener.stateUpdateHandler = { state in
                switch state {
                case .ready where !resumed: resumed = true; continuation.resume()
                case .failed(let error) where !resumed: resumed = true; continuation.resume(throwing: error)
                default: break
                }
            }
            listener.newConnectionHandler = { [weak wrapper] connection in wrapper?.handle(connection) }
            listener.start(queue: .main)
        }
        return wrapper
    }

    func waitForCallback() async throws -> [String: String] {
        try await withCheckedThrowingContinuation { continuation in
            callback = { continuation.resume(returning: $0); self.callback = nil }
        }
    }
    func stop() { listener.cancel() }

    private func handle(_ connection: NWConnection) {
        connection.start(queue: .main)
        connection.receive(minimumIncompleteLength: 1, maximumLength: 16_384) { [weak self] data, _, _, _ in
            guard let self, let data, let line = String(data: data, encoding: .utf8)?.components(separatedBy: "\r\n").first,
                  let target = line.split(separator: " ").dropFirst().first,
                  let url = URL(string: "http://127.0.0.1:\(self.port)\(target)"),
                  url.path == "/auth/callback" else {
                connection.cancel(); return
            }
            let query = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems ?? []
            let values = Dictionary(query.map { ($0.name, $0.value ?? "") }, uniquingKeysWith: { first, _ in first })
            var callbackURL = URLComponents()
            callbackURL.scheme = "koeapp"
            callbackURL.host = "oauth"
            callbackURL.queryItems = query
            let location = callbackURL.url?.absoluteString ?? "koeapp://oauth"
            let body = Data("Du kannst zu Koe zurückkehren.".utf8)
            let response = "HTTP/1.1 302 Found\r\nLocation: \(location)\r\nContent-Length: \(body.count)\r\nConnection: close\r\n\r\n"
            connection.send(content: Data(response.utf8), completion: .contentProcessed { _ in
                connection.cancel()
                self.callback?(values)
            })
        }
    }
}

private final class BrowserAuth: NSObject, ASWebAuthenticationPresentationContextProviding {
    private var session: ASWebAuthenticationSession?
    static func run(url: URL) async throws -> [String: String] {
        let runner = BrowserAuth()
        return try await runner.start(url: url)
    }
    private func start(url: URL) async throws -> [String: String] {
        try await withCheckedThrowingContinuation { continuation in
            session = ASWebAuthenticationSession(url: url, callbackURLScheme: "koeapp") { callbackURL, error in
                if let error { continuation.resume(throwing: error); return }
                guard let callbackURL else { continuation.resume(throwing: KoeError.missingOAuthResult); return }
                let items = URLComponents(url: callbackURL, resolvingAgainstBaseURL: false)?.queryItems ?? []
                continuation.resume(returning: Dictionary(items.map { ($0.name, $0.value ?? "") }, uniquingKeysWith: { first, _ in first }))
            }
            session?.presentationContextProvider = self
            session?.prefersEphemeralWebBrowserSession = false
            if session?.start() != true { continuation.resume(throwing: KoeError.browserUnavailable) }
        }
    }
    func presentationAnchor(for session: ASWebAuthenticationSession) -> ASPresentationAnchor {
        UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }.flatMap(\.windows).first(where: \.isKeyWindow) ?? ASPresentationAnchor()
    }
}

private extension Data {
    var base64URL: String { base64EncodedString().replacingOccurrences(of: "+", with: "-").replacingOccurrences(of: "/", with: "_").replacingOccurrences(of: "=", with: "") }
    init?(base64URL value: String) {
        var base64 = value.replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        base64 += String(repeating: "=", count: (4 - base64.count % 4) % 4)
        self.init(base64Encoded: base64)
    }
}

enum KoeError: LocalizedError {
    case invalidAuthorizationURL, oauthStateMismatch, oauth(String), missingOAuthResult
    case tokenExchangeFailed, invalidIDToken, planPermissionMissing, notConnected, accountChanged
    case keychain(OSStatus), browserUnavailable
    var errorDescription: String? {
        switch self {
        case .invalidAuthorizationURL: return "Die Anmelde-URL konnte nicht erstellt werden."
        case .oauthStateMismatch: return "Die OAuth-Antwort konnte nicht sicher zugeordnet werden."
        case .oauth(let value): return "Anmeldung abgebrochen oder fehlgeschlagen: \(value)"
        case .missingOAuthResult: return "OpenAI hat keinen vollständigen Anmelde-Rückruf geliefert."
        case .tokenExchangeFailed: return "Der OAuth-Code konnte nicht gegen Tokens getauscht werden."
        case .invalidIDToken: return "Das OpenAI-ID-Token konnte nicht verifiziert werden."
        case .planPermissionMissing: return "Die Freigabe für ChatGPT-Plan-Nutzung fehlt."
        case .notConnected: return "Kein ChatGPT-Konto verbunden."
        case .accountChanged: return "Das erneuerte Token gehört zu einem anderen Konto."
        case .keychain(let status): return "Sichere lokale Speicherung fehlgeschlagen (\(status))."
        case .browserUnavailable: return "Der sichere Browser-Login konnte nicht gestartet werden."
        }
    }
}
