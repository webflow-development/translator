# App Review access: version 1.0.0, rejected build 41

Apple's November 5, 2026 message reports inactive microphone/send controls and
missing access to account-based features on an iPad Air 11-inch (M3), iPadOS 27.2.
The submitted review information asked reviewers to use their own ChatGPT account;
no working test credentials were supplied.

## Findings and limits

- In the existing source, both controls were disabled while ChatGPT was disconnected.
  This explains the same symptom on a fresh installation and is a likely connection
  between the two reported issues. It is not proof of what happened on Apple's device.
- A build with the guest service enabled lets reviewers record, translate, and use AI
  chat before signing in. A build without the service still explains how to sign in.
  Both retain the draft and photo when offering sign-in.
- The app currently declares iPhone device family 1. iPhone compatibility on iPad
  still needs testing; the declaration is not a reason to dismiss Apple's iPad finding.

## Access arrangement to resolve before resubmitting

Apple requires working access for the full feature set. The previous instruction
to use the reviewer's own ChatGPT account has not satisfied this review.

Deploy and configure the guest service as described in the [repository README](../README.md#enable-guest-mode-for-a-build).
The guest path handles real translations, photos, bilingual AI and web search. On-device
speech playback and saved chats remain accessible. The custom model picker requires
a connected ChatGPT plan. Optional OpenAI voice uses the reviewer's own API key.
Describe these differences and make sure App Review has a way to evaluate any feature
it asks to verify. Guest access has a shared service allowance. Check its availability
for the review before submitting.

## Device verification before selecting the replacement build

Record the actual device, OS and replacement build number for each completed test.
The Windows build and iOS reference-API compilation do not replace these checks.

1. Install the configured build fresh. As a guest, verify that the microphone starts
   and that the first send asks for permission to share content with OpenAI. Declining
   keeps the draft and photo. Confirm an accepted send returns a real translation.
2. Check the remaining allowance in Settings. With an exhausted test allowance, verify
   the prompt to sign in and that the draft stays editable. Test choosing a model after
   signing in with an eligible account and signing back out to guest mode.
3. As a guest, request microphone/speech permissions, record, pause, resume and send.
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
