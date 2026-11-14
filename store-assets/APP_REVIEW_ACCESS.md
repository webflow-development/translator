# App Review access: version 1.0.0, rejected build 41

Apple's November 5, 2026 message reports inactive microphone/send controls and
missing access to account-based features on an iPad Air 11-inch (M3), iPadOS 27.2.
The submitted review information asked reviewers to use their own ChatGPT account;
no working test credentials were supplied.

## Findings and limits

- In the existing source, both controls were disabled while ChatGPT was disconnected.
  This explains the same symptom on a fresh installation and is a likely connection
  between the two reported issues. It is not proof of what happened on Apple's device.
- Reviewers can record and edit a draft before signing in. A signed-out send explains
  how to connect an eligible ChatGPT plan and keeps the draft and photo.
- The app currently declares iPhone device family 1. iPhone compatibility on iPad
  still needs testing; the declaration is not a reason to dismiss Apple's iPad finding.

## Access arrangement to resolve before resubmitting

Apple requires working access for the full feature set. The previous instruction
to use the reviewer's own ChatGPT account has not satisfied this review.

The web button opens ChatGPT's public website. It gives reviewers the same web
experience available in their location. In-app translations, photo processing,
bilingual AI and model selection require an eligible connected ChatGPT plan. Optional
OpenAI voice uses a personal API key. Arrange working access for the in-app features
with App Review before resubmitting.

## Device verification before selecting the replacement build

Record the actual device, OS and replacement build number for each completed test.
The Windows build and iOS reference-API compilation do not replace these checks.

1. On a fresh install, verify that **Try ChatGPT on the web** opens the official
   website and returning to Koe keeps the current conversation and its draft.
2. While signed out, grant microphone and speech permissions and record, pause and
   resume. Send and dismiss the sign-in prompt. Verify the text and photo are still
   editable. Deny permissions separately and verify that typed input remains usable.
3. Sign in with an eligible plan, choose a model and send the draft. Verify the
   first-send permission prompt, a real translation and the speaker switch.
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
