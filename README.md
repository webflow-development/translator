# Koe – .NET MAUI

Gesprächsübersetzer für Deutsch und Japanisch, jetzt in C# mit .NET MAUI 10.

## Projektstruktur

- `Koe-Maui/`: aktive iPhone-App und Windows-Version für die Entwicklung.
- `Koe-iOS/`: bisheriger SwiftUI-Prototyp als unveränderte Referenz; wird vom Root-Workflow nicht mehr gebaut.
- `codemagic.yaml`: Cloud-Builds für die neue .NET-Version.
- `tests/Koe.CoreChecks/`: ausführbare Tests für Streaming und ID-Token-Verifikation, ohne Apple-Konto oder API-Aufrufe.

## Funktionen

Deutsch ↔ Japanisch als hellblauer Messenger mit Texteingabe und iPhone-Diktat. Der große runde Gesprächsknopf unten links startet die Aufnahme. Jeder weitere Druck beendet den Satz, übersetzt ihn, wechselt die Sprache und startet das Mikrofon für die andere Person. Pause übersetzt den aktuellen Satz, ohne die nächste Aufnahme zu starten. Es gibt keine Sprachausgabe. Der Verlauf bleibt nur im Arbeitsspeicher und kann gelöscht werden. Auf Windows ist Texteingabe verfügbar; das Diktat ist iOS-spezifisch.

Die ChatGPT-Anmeldung verwendet den dokumentierten Open-Source-SIWC-Flow mit PKCE, State, Nonce, Loopback-Callback und RS256-ID-Token-Verifikation. Tokens liegen in MAUI SecureStorage (auf iOS im Schlüsselbund). Es wird kein API-Schlüssel eingebaut. Übersetzungen verwenden ein vom Konto angebotenes Modell und die öffentliche Responses API mit `store=false` und `stream=true`. Es werden nur erkannte Texte an OpenAI geschickt. Apples Spracherkennung kann je nach Gerät und Sprache Apples Server nutzen.

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

Ein erfolgreicher Compile-Check genügt nicht für die Freigabe: Starten, Mikrofon-/Sprachberechtigungen, Diktat in beiden Sprachen, Login-Abbruch, Loopback-/Browser-Rückkehr, Token-Erneuerung, Abmelden, Übersetzung, Sprecherwechsel und Pause müssen auf einem iPhone getestet werden. SIWC ist eine Vorschau; insbesondere der Loopback-Browser-Flow auf iOS ist ein zu verifizierender Integrationspunkt. Kontoberechtigung und Plan-Verfügbarkeit hängen von OpenAI ab.

Offizielle Quellen:
- https://developers.openai.com/siwc/token-sharing-open-source/sign-in
- https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference
- https://docs.codemagic.io/yaml-quick-start/building-a-dotnet-maui-app/
- https://github.com/dotnet/macios/releases/tag/dotnet-10.0.1xx-xcode27.1-10018

MIT-Lizenz: siehe `Koe-iOS/LICENSE`.
