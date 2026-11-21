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

The replacement source includes a disclosed local demonstration mode. On a fresh
installation, tap **Try demo** below **Continue with ChatGPT**. It is also available
in Settings when an account is connected. Reviewers can use these steps without
ChatGPT credentials.

1. Tap **Try demo**. The English–Japanese hotel phrase is loaded. Tap send to view
   its prepared translation, or use the microphone to dictate the displayed phrase.
2. Open **Example → Hotel conversation** for more phrases. Tap the other speaker
   and choose a phrase to test the reverse direction.
3. Open **Example → Menu photo** and send the bundled image. Copy the menu transcript
   or its prepared English translation.
4. Open **Example → Ask AI** and send. The prepared answer appears in both languages.
   **AI follow-up** loads another prepared question. The active speaker determines
   which language is loaded, and the answer follows that question's language.
5. Test playback, text size, transliteration, back to chats, a new conversation,
   reopening a saved demo chat and swipe deletion. **Exit** returns to normal chats.

The persistent banner identifies sample responses. The demo handles the listed
examples, uses the bundled menu, and explains unsupported input. Native microphone
recognition, editing, chat storage and iPhone speech playback remain interactive.
The normal account provides live translations, arbitrary AI requests and web search.
Optional OpenAI voice uses a personal API key. Explain these boundaries to App Review
and confirm an acceptable way to assess the full feature set.

OpenAI's account-sharing policy reserves personal accounts for their individual
owners. A working review arrangement should account for that policy and any email
verification requirement. See [Apple's review-access guidance](https://developer.apple.com/help/app-review/before-submitting-for-review/complete-review/)
and [OpenAI's account-sharing policy](https://help.openai.com/en/articles/10471989-openai-account-sharing-policy).

## Device verification before selecting the replacement build

Record the actual device, OS and replacement build number for each completed test.
The Windows build and iOS reference-API compilation do not replace these checks.

1. Fresh install, disconnected: the sign-in card and composer explain the requirement.
   Both microphone and send taps open the sign-in dialog. "Not now" leaves text/photo
   drafts intact. Rapid repeated taps do not open multiple dialogs.
2. After signing out, tap **Continue with ChatGPT** and verify that a
   different eligible account can connect without an account picker or a preselected
   previous account. Also test signing in again with the same account, cancelling
   sign-in, and upgrading an older installation with a saved session.
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
8. Follow all demo steps above. Enter from an existing normal chat with a draft and
   photo, then exit and verify both are restored. Change demo text size and
   transliteration and verify the previous normal settings return. Repeat with
   optional OpenAI voice enabled and confirm that demo playback uses iPhone voices.
   Reopen the app, reenter the demo and check that its saved chats stay separate.

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
