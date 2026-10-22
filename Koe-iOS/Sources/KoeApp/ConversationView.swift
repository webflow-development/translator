import SwiftUI
import Speech
import AVFoundation

enum Talker: String, CaseIterable, Identifiable {
    case german = "Deutsch"
    case japanese = "日本語"
    var id: String { rawValue }
    var locale: Locale { Locale(identifier: self == .german ? "de-DE" : "ja-JP") }
    var other: Talker { self == .german ? .japanese : .german }
}

private struct Turn: Identifiable {
    let id = UUID()
    let speaker: Talker
    let original: String
    let translated: String
}

struct ConversationView: View {
    @EnvironmentObject private var auth: ChatGPTAuthStore
    @State private var turns: [Turn] = []
    @State private var draft = ""
    @State private var speaker: Talker = .german
    @State private var isListening = false
    @State private var isTranslating = false
    @State private var status: String?
    @StateObject private var speech = SpeechCapture()
    @State private var synthesizer = AVSpeechSynthesizer()

    var body: some View {
        VStack(spacing: 0) {
            header
            if !auth.isConnected { signInCard }
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(spacing: 14) {
                        if turns.isEmpty { emptyState }
                        ForEach(turns) { turn in
                            TurnBubble(turn: turn).id(turn.id)
                        }
                        if isTranslating { ProgressView("Übersetze …").padding(.vertical, 8) }
                    }
                    .padding(.horizontal, 16)
                    .padding(.vertical, 20)
                }
                .onChange(of: turns.count) { _, _ in
                    if let id = turns.last?.id { withAnimation { proxy.scrollTo(id, anchor: .bottom) } }
                }
            }
            if let status { Text(status).font(.footnote).foregroundStyle(.secondary).padding(.horizontal) }
            composer
        }
        .background(Color(uiColor: .systemGroupedBackground))
        .onChange(of: speech.transcript) { _, value in draft = value }
        .onChange(of: speech.isRecording) { _, value in isListening = value }
    }

    private var header: some View {
        HStack(spacing: 12) {
            Image(systemName: "bubble.left.and.bubble.right.fill").font(.title2).foregroundStyle(.blue)
            VStack(alignment: .leading, spacing: 2) {
                Text("Koe").font(.headline)
                Text("Deutsch  ↔  日本語").font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            if auth.isConnected {
                Menu {
                    Button("Erneut mit ChatGPT anmelden", systemImage: "arrow.clockwise") {
                        Task {
                            do { try await auth.signIn(); status = "ChatGPT-Verbindung erneuert." }
                            catch { status = error.localizedDescription }
                        }
                    }
                    Link("Nutzung verwalten", destination: URL(string: "https://chatgpt.com/settings/usage")!)
                    Button("Abmelden", systemImage: "rectangle.portrait.and.arrow.right", role: .destructive) {
                        auth.disconnect()
                    }
                } label: {
                    Label("ChatGPT", systemImage: "checkmark.circle.fill").font(.caption2).foregroundStyle(.green)
                }
            }
        }
        .padding(.horizontal, 18).padding(.vertical, 14)
        .background(.regularMaterial)
    }

    private var signInCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Mit ChatGPT verbinden").font(.headline)
            Text("Du erlaubst Koe, geeignete Übersetzungsanfragen über deinen ChatGPT-Plan zu senden. Dafür wird kein API-Schlüssel benötigt. Die Anfrage nutzt deine ChatGPT-Plan-Nutzung oder verfügbare Credits.")
                .font(.footnote).foregroundStyle(.secondary)
            Button {
                Task {
                    do { try await auth.signIn(); status = "ChatGPT-Plan ist verbunden." }
                    catch { status = error.localizedDescription }
                }
            } label: {
                Label("Continue with ChatGPT", systemImage: "person.crop.circle.badge.checkmark")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
        }
        .padding(16).background(.background, in: RoundedRectangle(cornerRadius: 18))
        .padding(.horizontal, 16).padding(.top, 14)
    }

    private var emptyState: some View {
        VStack(spacing: 10) {
            Image(systemName: "waveform.circle").font(.system(size: 48)).foregroundStyle(.blue.opacity(0.7))
            Text("Dein Gespräch beginnt hier").font(.headline)
            Text("Wähle unten, wer spricht. Koe zeigt Original und Übersetzung als Chatverlauf.")
                .font(.subheadline).foregroundStyle(.secondary).multilineTextAlignment(.center)
        }
        .frame(maxWidth: 300).padding(.top, 60).frame(maxWidth: .infinity)
    }

    private var composer: some View {
        VStack(spacing: 10) {
            HStack {
                Text("Spricht gerade").font(.caption).foregroundStyle(.secondary)
                Picker("Sprache", selection: $speaker) {
                    ForEach(Talker.allCases) { Text($0.rawValue).tag($0) }
                }
                .pickerStyle(.segmented)
            }
            Text("Diktat nutzt die iOS-Spracherkennung; je nach Gerät lokal oder über Apples Dienst.")
                .font(.caption2).foregroundStyle(.tertiary).frame(maxWidth: .infinity, alignment: .leading)
            HStack(alignment: .bottom, spacing: 10) {
                TextField("Satz eingeben …", text: $draft, axis: .vertical)
                    .lineLimit(1...4).padding(12)
                    .background(Color(uiColor: .secondarySystemGroupedBackground), in: RoundedRectangle(cornerRadius: 20))
                Button {
                    if isListening { speech.stop() }
                    else { Task { await speech.start(locale: speaker.locale) } }
                } label: {
                    Image(systemName: isListening ? "stop.fill" : "mic.fill")
                        .font(.title3).frame(width: 46, height: 46)
                        .background(isListening ? Color.red : Color.blue, in: Circle()).foregroundStyle(.white)
                }
                .accessibilityLabel(isListening ? "Diktat stoppen" : "Sprechen")
                Button {
                    Task { await send() }
                } label: {
                    Image(systemName: "arrow.up").font(.headline).frame(width: 46, height: 46)
                        .background(Color.primary, in: Circle()).foregroundStyle(Color(uiColor: .systemBackground))
                }
                .disabled(draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || isTranslating || !auth.isConnected)
            }
        }
        .padding(.horizontal, 14).padding(.top, 12).padding(.bottom, 10)
        .background(.regularMaterial)
    }

    @MainActor private func send() async {
        let text = draft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { return }
        guard auth.isConnected else { status = "Verbinde zuerst deinen ChatGPT-Plan."; return }
        speech.stop()
        draft = ""; speech.reset(); isTranslating = true; status = nil
        defer { isTranslating = false }
        do {
            let translated = try await OpenAITranslator(auth: auth).translate(text, from: speaker, to: speaker.other)
            turns.append(Turn(speaker: speaker, original: text, translated: translated))
            speak(translated, locale: speaker.other.locale)
            speaker = speaker.other
        } catch {
            draft = text
            status = error.localizedDescription
        }
    }

    private func speak(_ text: String, locale: Locale) {
        let utterance = AVSpeechUtterance(string: text)
        utterance.voice = AVSpeechSynthesisVoice(language: locale.identifier)
        synthesizer.speak(utterance)
    }
}

private struct TurnBubble: View {
    let turn: Turn
    private var isGerman: Bool { turn.speaker == .german }

    var body: some View {
        VStack(alignment: .leading, spacing: 9) {
            HStack {
                Text(turn.speaker.rawValue).font(.caption.weight(.semibold)).foregroundStyle(.secondary)
                Spacer()
                Image(systemName: isGerman ? "arrow.right" : "arrow.left").font(.caption).foregroundStyle(.secondary)
                Text(turn.speaker.other.rawValue).font(.caption.weight(.semibold)).foregroundStyle(.secondary)
            }
            Text(turn.original).font(.body)
            Divider()
            Text(turn.translated).font(.body.weight(.medium))
        }
        .padding(14).frame(maxWidth: 340, alignment: .leading)
        .background(isGerman ? Color.blue.opacity(0.12) : Color.green.opacity(0.13), in: RoundedRectangle(cornerRadius: 18))
        .frame(maxWidth: .infinity, alignment: isGerman ? .trailing : .leading)
    }
}

@MainActor
private final class SpeechCapture: ObservableObject {
    @Published var transcript = ""
    @Published var isRecording = false
    private let engine = AVAudioEngine()
    private var request: SFSpeechAudioBufferRecognitionRequest?
    private var task: SFSpeechRecognitionTask?

    func start(locale: Locale) async {
        stop()
        let speechAuth = await SFSpeechRecognizer.requestAuthorizationAsync()
        guard speechAuth == .authorized else { return }
        let mic = await requestMicrophonePermission()
        guard mic, let recognizer = SFSpeechRecognizer(locale: locale), recognizer.isAvailable else { return }
        transcript = ""
        do {
            let audioSession = AVAudioSession.sharedInstance()
            try audioSession.setCategory(.record, mode: .measurement, options: .duckOthers)
            try audioSession.setActive(true, options: .notifyOthersOnDeactivation)
            let req = SFSpeechAudioBufferRecognitionRequest()
            req.shouldReportPartialResults = true
            if recognizer.supportsOnDeviceRecognition { req.requiresOnDeviceRecognition = true }
            request = req
            let input = engine.inputNode
            input.installTap(onBus: 0, bufferSize: 1024, format: input.outputFormat(forBus: 0)) { buffer, _ in req.append(buffer) }
            engine.prepare(); try engine.start(); isRecording = true
            task = recognizer.recognitionTask(with: req) { [weak self] result, error in
                Task { @MainActor in
                    if let result { self?.transcript = result.bestTranscription.formattedString }
                    if error != nil || result?.isFinal == true { self?.stop() }
                }
            }
        } catch { stop() }
    }

    func stop() {
        if engine.isRunning { engine.stop() }
        engine.inputNode.removeTap(onBus: 0)
        request?.endAudio(); task?.cancel(); task = nil; request = nil
        isRecording = false
    }

    func reset() { transcript = "" }

    private func requestMicrophonePermission() async -> Bool {
        await withCheckedContinuation { continuation in
            AVAudioSession.sharedInstance().requestRecordPermission { continuation.resume(returning: $0) }
        }
    }
}

private extension SFSpeechRecognizer {
    static func requestAuthorizationAsync() async -> SFSpeechRecognizerAuthorizationStatus {
        await withCheckedContinuation { continuation in
            SFSpeechRecognizer.requestAuthorization { continuation.resume(returning: $0) }
        }
    }
}
