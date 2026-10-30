## Read aloud and voices

Speaker icons beside original and translated text use native iPhone speech synthesis. Tap the same speaker again to stop; starting recording, discarding input or leaving the page stops playback. Playback is blocked during active recording. **Settings → Speech voices** lets users choose a language, select an available voice and hear a sample greeting; selections are saved per language. The number of voices depends on the installed/available iOS voices, so two or three choices are not guaranteed for every recognition language. Download additional voices in iPhone Accessibility settings where offered. Speech synthesis uses a system-managed audio session independent of recording. The Windows development build displays an unsupported notice. Native audio routing and voice selection require TestFlight testing.
## Grammar correction on send

Spoken and typed messages are lightly corrected for grammar, spelling and punctuation within the same translation request. The corrected input is displayed and copied as the original-language message, and its transliteration matches that corrected text. Instructions preserve meaning, tone, names, negation, numbers and facts and retain ambiguous wording rather than guessing. Image OCR remains a faithful transcript. No extra API request is added, although returning the corrected text adds output. Correction quality requires real-device testing. On failure or cancellation the unmodified editable draft is retained unless explicitly discarded.
## Current input

The red **× Discard input** button stops recording, cancels an in-progress translation and deletes the current editable draft and attachment. Existing translated chat messages and the selected speaker remain unchanged. Late recognition results cannot restore discarded text. **Cancel** remains available for operations without a draft, such as signing in.

## Conversation languages

Translation responses appear progressively in a temporary preview, then become chat messages only after the complete response has been validated. Cancelling or failing a request removes the preview and retains the original draft and attachment. The selected model is cached in memory for 30 minutes, scoped to the current credential and cleared after access/model errors; subsequent sends avoid fetching the model catalog again.

The app interface is in English. Open **••• → Settings → Conversation languages** to search and select two different languages. On iPhone, the list is built from `SFSpeechRecognizer.SupportedLocales`, normalized to .NET cultures and grouped by language; the count depends on iOS support. Regional duplicates are combined, preferring the existing recognition locales where available. Some languages require Apple's online recognition service; supported does not guarantee availability at that moment. The Windows development fallback retains English, Spanish, German, Japanese and Mandarin. The default pair is English/Japanese where supported and the selection is saved on the device. Speech recognition uses the active language; sending a message translates to the other selected language and switches speakers without starting the microphone. Image text is translated separately between the selected languages. Settings now offers Show/Hide transliteration for all languages. Original and translated non-Latin text receive independent Latin-script readings (for example Japanese Hepburn, Mandarin Pinyin with tones, or Korean romanization). Latin-script text has no extra reading. The previous Romaji preference is retained on upgrade. When disabled, new requests ask for empty transliteration fields; enabling it later cannot add readings to past messages generated without them. Native iOS discovery and individual language quality require device testing.
# Koe – .NET MAUI

Gesprächsübersetzer für Deutsch und Japanisch, jetzt in C# mit .NET MAUI 10.

## Projektstruktur

- `Koe-Maui/`: aktive iPhone-App und Windows-Version für die Entwicklung.
- `Koe-iOS/`: bisheriger SwiftUI-Prototyp als unveränderte Referenz; wird vom Root-Workflow nicht mehr gebaut.
- `codemagic.yaml`: Cloud-Builds für die neue .NET-Version.
- `tests/Koe.CoreChecks/`: ausführbare Tests für Streaming, ID-Token-Verifikation, Umschrift-Antworten und Aufnahme-/Pausenzustände, ohne Apple-Konto oder API-Aufrufe.

## Funktionen

Deutsch ↔ Japanisch als hellblauer Messenger mit Texteingabe und iPhone-Diktat. Unten liegt ein gemeinsam zentriertes Knopfpaar: links das große Mikrofon, rechts der kleinere Sendepfeil (56 × 56 Punkte). Der Mikrofonknopf startet die Aufnahme, pausiert sie beim zweiten Druck und setzt sie beim nächsten Druck fort. Er übersetzt nicht. Erst der separate Sendepfeil beendet eine laufende Aufnahme, wartet auf die letzten erkannten Wörter und übersetzt den gesamten Entwurf einschließlich eines ausgewählten Bilds. Nach erfolgreichem Senden wechselt die Sprache automatisch; die nächste Person startet ihr Mikrofon selbst. Die separaten Pause- und Gespräch-beenden-Knöpfe entfallen.

Das Plus sitzt im Textfeld auf Höhe der ersten Textzeile, auch bei mehrzeiligem Text. Zum Korrigieren ins Textfeld tippen; dabei pausiert die Aufnahme. Markieren, Löschen und Einfügen bleiben verfügbar. Während der Verarbeitung liegt die Ladeanzeige über dem Mikrofonknopf. Es gibt keine Sprachausgabe. Der Verlauf bleibt im Arbeitsspeicher und kann über das Menü gelöscht werden. Auf Windows ist Texteingabe verfügbar; das Diktat ist iOS-spezifisch.

Die ChatGPT-Anmeldung verwendet den dokumentierten Open-Source-SIWC-Flow mit PKCE, State, Nonce, Loopback-Callback und RS256-ID-Token-Verifikation. Tokens liegen in MAUI SecureStorage (auf iOS im Schlüsselbund). Es wird kein API-Schlüssel eingebaut. Übersetzungen verwenden ein vom Konto angebotenes Modell und die öffentliche Responses API mit `store=false` und `stream=true`. Es werden erkannte bzw. eingegebene Texte und ausdrücklich ausgewählte Bilder an OpenAI geschickt. Audioaufnahmen werden nicht an OpenAI geschickt. Apples Spracherkennung kann je nach Gerät und Sprache Apples Server nutzen.

## Nachrichten und Darstellung

Neue Nachrichten werden automatisch ins Bild gescrollt. Unter Originalen und Übersetzungen mit nichtlateinischer Schrift steht bei aktivierter Einstellung die passende lateinische Umschrift. Die Kopiersymbole neben Original und Übersetzung kopieren jeweils nur diesen Text, ohne Umschrift oder Sprachbezeichnung. Unter **••• → Einstellungen** ist die Textgröße **Klein / Mittel / Groß** wählbar; die Einstellung wirkt sofort und bleibt auf dem Gerät gespeichert.

## Bilder mit einer Nachricht übersetzen

Über **+** am Textfeld ein Foto, die Kamera oder eine Bilddatei auswählen. Pro Nachricht ist ein Bild möglich. Die Vorschau lässt sich mit **×** entfernen. Danach sprechen, schreiben oder das Bild direkt mit **↑** senden. Bildtext und Nachricht werden zusammen verarbeitet, aber als getrennte Sprechblasen angezeigt: Ein japanischer Bildtext wird ins Deutsche übersetzt, ein deutscher ins Japanische. Deine gesprochene Nachricht behält die gewählte Sprechrichtung. Umschrift und Kopieren funktionieren für beide Ergebnisse. Bei Fehlern bleibt der Entwurf samt Bild erhalten.

Auf iOS bereitet UIKit Fotos einschließlich HEIC als JPEG mit maximal 2560 Pixeln längster Kante auf. Die Ausgangsdatei darf höchstens 20 MB und das vorbereitete Bild höchstens 8 MB groß sein. Unter Windows sind JPEG, PNG und WebP unterstützt. Bilder werden im Arbeitsspeicher gehalten und erst beim Senden an OpenAI übertragen. Es gibt keinen separaten Files-API-Upload. Kamerazugriff, Fotoauswahl, HEIC/Orientierung und echte Bildübersetzungen müssen noch auf dem iPhone getestet werden.

## Unter Windows entwickeln

1. .NET SDK **10.0.402** und Visual Studio mit .NET MAUI oder VS Code mit C#/.NET-MAUI-Erweiterungen installieren.
2. Im Repo-Root `dotnet workload install maui-windows --version 10.0.402.1` ausführen.
3. `dotnet build Koe-Maui/Koe.csproj -f net10.0-windows10.0.19041.0`.
4. `dotnet build Koe-Maui/Koe.csproj -t:Run -f net10.0-windows10.0.19041.0`.

Tests: `dotnet run --project tests/Koe.CoreChecks/Koe.CoreChecks.csproj -c Release`.

Die iOS-App wird auf einem Mac kompiliert. Ein Windows-PC oder eine Windows-VM genügt zum Bearbeiten und zum Windows-Test; er ersetzt keinen Mac für den iOS-Simulator oder iOS-Debugging.

## Codemagic

Die Konfiguration erwartet SDK 10.0.402, Workload-Set 10.0.402.1 und Xcode 27.1. Diese Versionen gehören zusammen; nicht unabhängig auf `latest` umstellen.

1. Änderungen ins verbundene GitHub-Repo übernehmen.
2. Zuerst **Koe iOS compile check (no signing)** starten. Dieser Workflow kompiliert für den iOS-Simulator ohne Apple-Signierungsdateien. Er veröffentlicht keine App und ist kein iPhone-Laufzeittest.
3. Für **Koe iOS TestFlight** die Integration exakt **webflow** nennen. Apple-Distribution-Zertifikat mit privatem Schlüssel und ein gültiges App-Store-Profil für **ch.paulo.koe** in Codemagic hinterlegen. Die API-Verbindung allein erzeugt diese Dateien nicht.
4. App-ID und App-Store-Connect-App-Datensatz für `ch.paulo.koe` müssen vorhanden sein.
5. **Koe iOS TestFlight** baut die IPA und lädt sie nach App Store Connect. Die Buildnummer wird aus Codemagics BUILD_NUMBER + 1 gebildet; sie muss höher sein als bereits hochgeladene Builds. Bei bestehender App gegebenenfalls die Buildnummerierung in Codemagic erhöhen.

## Noch auf einem echten iPhone zu prüfen

Ein erfolgreicher Compile-Check genügt nicht für die Freigabe: Starten, Mikrofon-/Sprachberechtigungen, Diktat in beiden Sprachen, Login-Abbruch, Loopback-/Browser-Rückkehr, Token-Erneuerung, Abmelden, Übersetzung, Sprecherwechsel und Gespräch beenden müssen auf einem iPhone getestet werden. SIWC ist eine Vorschau; insbesondere der Loopback-Browser-Flow auf iOS ist ein zu verifizierender Integrationspunkt. Kontoberechtigung und Plan-Verfügbarkeit hängen von OpenAI ab.

Offizielle Quellen:
- https://developers.openai.com/siwc/token-sharing-open-source/sign-in
- https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference
- https://docs.codemagic.io/yaml-quick-start/building-a-dotnet-maui-app/
- https://github.com/dotnet/macios/releases/tag/dotnet-10.0.1xx-xcode27.1-10018

MIT-Lizenz: siehe `Koe-iOS/LICENSE`.
