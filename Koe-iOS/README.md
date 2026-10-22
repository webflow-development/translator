# Koe – iOS translator prototype

Local-first iPhone conversation translator for German and Japanese. It keeps the conversation on the device and sends recognized text to the OpenAI Responses API. Sign-in uses the open-source Sign in with ChatGPT flow. There is no API key and no application backend.

## Included

- SwiftUI messenger conversation for German and Japanese
- iOS Speech dictation and text-to-speech playback
- ChatGPT OAuth authorization-code + PKCE flow with a loopback callback
- Keychain token storage and token refresh
- Responses API streaming request with store=false and stream=true
- XcodeGen project definition and Codemagic TestFlight workflow

## Prototype and release gates

OpenAI's current open-source preview documentation requires a listener on 127.0.0.1 and does not provide a native iOS walkthrough. The callback bridge and ID-token signature verification against OpenAI's JWKS are implemented, but must be compiled and tested on a real iPhone before treating sign-in as reliable.

ChatGPT plan usage currently does not accept audio or transcription API input. Koe therefore uses Apple's Speech framework to convert microphone input to text and sends only that text for translation. On-device recognition is requested when available; otherwise iOS may use Apple's speech-recognition service. The text field remains available if speech recognition cannot be used.

## Build

1. Install XcodeGen on a Mac or make it available in the cloud image.
2. Run xcodegen generate --spec project.yml from this folder.
3. Open Koe.xcodeproj, choose a signing team, and run it on a physical iPhone.

For cloud signing, connect your Apple Developer account to Codemagic yourself and configure the App Store Connect integration there. Create the app record in App Store Connect before the first automated TestFlight upload. Never put Apple credentials, OAuth tokens, or signing certificates in source control.

## Open source

License: MIT. Review OpenAI's current SIWC terms and preview limitations before publishing or distributing builds.
