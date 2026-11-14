# App Review access: version 1.0.0, rejected build 41

Apple's November 5, 2026 message reports inactive microphone/send controls and
missing access to account-based features on an iPad Air 11-inch (M3), iPadOS 27.2.
The submitted review information asked reviewers to use their own ChatGPT account;
no working test credentials were supplied.

## Findings and limits

- In the existing source, both controls were disabled while ChatGPT was disconnected.
  This explains the same symptom on a fresh installation and is a likely connection
  between the two reported issues. It is not proof of what happened on Apple's device.
- The updated source makes these buttons open a sign-in explanation when disconnected.
  It keeps the editable draft/photo, does not automatically record or send after sign-in,
  and shows authentication failures in the sign-in card.
- Translation and AI still require real ChatGPT authorization. This change alone does
  not meet Apple's request for review access and is not a completed review fix.
- The app currently declares iPhone device family 1. iPhone compatibility on iPad
  still needs testing; the declaration is not a reason to dismiss Apple's iPad finding.
- No new test credentials, shared API key, demo service or paid backend has been created.
  No message has been sent to Apple.

## Access arrangement to resolve before resubmitting

Apple requires working access for the full feature set. The previous instruction
to use the reviewer's own ChatGPT account has not satisfied this review.

A possible next implementation is a disclosed, fully functional demonstration
mode with dedicated test access to a secured API-backed service. The service would
keep its provider credentials on the server; no developer API key would be bundled
in the app. It must support actual test input, translation, photos, bilingual AI,
web search where supported, chat persistence and the relevant speech-output paths.
Fixed canned replies or a demonstration video would not establish those behaviors.

That is a separate integration requiring the owner's decision on API costs,
hosting, credentials and access limits. It is a proposal, not a feature of the
current build. Any review-access differences must be explained to Apple. If a
different account arrangement is chosen, confirm with App Review that it provides
the required access; do not assume a shared personal ChatGPT account is suitable.

## Device verification before selecting the replacement build

Record the actual device, OS and replacement build number for each completed test.
The Windows build and iOS reference-API compilation do not replace these checks.

1. Fresh install, disconnected: the sign-in card and composer explain the requirement.
   Both microphone and send taps open the sign-in dialog. "Not now" leaves text/photo
   drafts intact. Rapid repeated taps do not open multiple dialogs.
2. Complete sign-in through the supported route, or use the fully configured test
   access once it exists. Record a real successful connection; do not mark this
   complete based only on a displayed connected indicator.
3. With access: request microphone/speech permissions, record, pause, resume and send.
   Confirm a real translation and the speaker switch. Deny permissions separately
   and verify that the app shows an explanation and remains usable for typed input.
4. Test typed text, a photo/camera image, an AI question in both languages, a web-search
   request, copy, speech playback, saved chats, a new chat and reopening/deleting a chat.
5. Test sign-in cancellation, failed sign-in, network loss, retry and sign-out. A kept
   draft must not be sent or erased merely by opening or completing sign-in.
6. Repeat on iPhone and on iPad in the app's actual compatibility layout, including
   rotation and opening the keyboard. Confirm taps reach the visible controls.
7. Install as an update and verify saved chats and the existing sign-in state.

## Submission notes

- Select the replacement build explicitly in App Store Connect. Replying to a review
  message or uploading a newer TestFlight build does not change build 41's binary.
- Supply the finalized, working test-access details and steps in App Review Information.
  Include only device/OS/build combinations that were genuinely tested.
- Explain the disconnected-state UI correction and the actual review-access path.
  Do not claim the iPad issue is fixed until a device test establishes that.

Official references:

- https://developer.apple.com/app-store/review/guidelines/#app-completeness
- https://developer.apple.com/help/app-review/before-submitting-for-review/complete-review
