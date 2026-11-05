# Code-drawn UI previews

These illustrations explain the layout implemented in commit `e22a192`.
They are **not screenshots captured from the running application or an iPhone**.
The native app window could not be opened in the current desktop session
(`GetCursorPos failed: Access is denied`).

Colors, text sizes, grouping, controls and the shared header/composer follow
`MainPage.cs`, `ConversationListView.cs` and `SettingsPage.cs`. Example messages,
dates, connection status and the displayed model are illustrative sample data.
Font rendering, native control appearance, exact spacing, safe areas, keyboard
behavior and scrolling may differ on a real iPhone. The Settings previews show
the upper scroll position; account settings remain accessible farther down.

- `Koe-UI-Code-Preview.png`: overview of the embedded chat list, conversation and dark Settings.
- `Koe-Preview-Chats.png`: embedded chat list.
- `Koe-Preview-Conversation.png`: opened conversation.
- `Koe-Preview-Settings-Dark.png`: dark Settings.
- `Koe-Preview-Settings-Light.png`: light Settings.

Each image explicitly carries a preview label. These files are for discussing
the design, **not for upload to App Store Connect as app screenshots**.
Use real device or simulator captures for the App Store listing.
