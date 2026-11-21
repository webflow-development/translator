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

## Access arrangement to resolve before resubmitting

Apple requires working access for the full feature set. The previous instruction
to use the reviewer's own ChatGPT account has not satisfied this review.

OpenAI may require an expiring email code on a new review device. Coordinate a
workable review path with App Review. The account-switch correction in Koe lets a
different account register separately, while OpenAI still controls login verification.

Apple describes a fully featured demonstration mode using demonstration data.
Agree on how this would expose Koe's features, including translation, photos,
bilingual AI, saved chats and speech playback. A mode that produces live model
responses also needs an authorized processing service. Koe currently uses the
connected user's ChatGPT plan for that processing.

OpenAI's account-sharing policy reserves personal accounts for their individual
owners. A working review arrangement should account for that policy and any email
verification requirement. The current app still needs this arrangement before
resubmission. See [Apple's review-access guidance](https://developer.apple.com/help/app-review/before-submitting-for-review/complete-review/)
and [OpenAI's account-sharing policy](https://help.openai.com/en/articles/10471989-openai-account-sharing-policy).

## Device verification before selecting the replacement build

Record the actual device, OS and replacement build number for each completed test.
The Windows build and iOS reference-API compilation do not replace these checks.

1. Fresh install, disconnected: the sign-in card and composer explain the requirement.
   Both microphone and send taps open the sign-in dialog. "Not now" leaves text/photo
   drafts intact. Rapid repeated taps do not open multiple dialogs.
2. After signing out, choose **Use another ChatGPT account** and verify that a
   different eligible account can connect. Also test returning to the previous
   registration, cancelling the account choice, and upgrading an older installation.
   Complete sign-in through the supported route, or use the fully configured test
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
