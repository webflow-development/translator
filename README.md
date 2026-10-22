# Koe – Gesprächsübersetzer

Koe ist ein iOS-Prototyp für persönliche Gespräche auf Deutsch und Japanisch. Die Oberfläche zeigt beide Seiten als Messenger-Verlauf. Spracheingabe und Vorlesen laufen über iOS; erkannter Text wird mit der OpenAI Responses API übersetzt.

## iOS-Prototyp

Der SwiftUI-Prototyp und Build-Hinweise liegen in [`Koe-iOS/`](./Koe-iOS/). Er verwendet den offenen Sign in with ChatGPT (SIWC)-Flow mit PKCE und speichert Tokens im iOS-Schlüsselbund. Ein eigener Backend-Server und ein vom App-Anbieter verwalteter API-Schlüssel sind nicht vorgesehen.

## Status

Dies ist ein nicht verifizierter Prototyp. Insbesondere müssen Xcode-Build, OAuth-Callback auf einem echten iPhone und App-Store-/TestFlight-Konfiguration noch getestet werden. Die Anmeldung mit ChatGPT ist keine allgemeine Freigabe des ChatGPT-Abonnements für beliebige API-Aufrufe; Verfügbarkeit und Nutzungsbedingungen hängen von OpenAIs SIWC-Unterstützung ab. Prüfe vor Verteilung die aktuellen OpenAI-Vorgaben.

Die App nutzt Apples Spracherkennung für Spracheingabe. Übersetzt wird der erkannte Text über die API; ChatGPT-Abozugang umfasst nicht automatisch Audio- oder Transkriptions-API-Aufrufe.

## Build

1. Auf einem Mac Xcode und XcodeGen installieren.
2. Im Ordner `Koe-iOS/` `xcodegen generate --spec project.yml` ausführen.
3. Projekt in Xcode öffnen, Team/Signing konfigurieren und auf einem iPhone testen.

Für Cloud-Builds ist eine Codemagic-Konfiguration unter [`codemagic.yaml`](./codemagic.yaml) enthalten. Apple-Zugangsdaten, Tokens und Zertifikate gehören nicht ins Repository.

## Lizenz

Der iOS-Prototyp steht unter MIT-Lizenz; Details siehe [`Koe-iOS/LICENSE`](./Koe-iOS/LICENSE).
