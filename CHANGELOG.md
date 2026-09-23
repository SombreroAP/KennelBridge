# Changelog

All notable changes to KennelBridge. Release notes on GitHub are taken from here.

## 1.1.0
- **A new look.** Redesigned in Claude Design and rebuilt to match: a left rail with an icon and a live on/off dot for each bridge, the connection summary and Pause at the bottom of the rail, and the page's own title and one-line description at the top instead of the big banner. Settings sit on flat rounded surfaces with hairline dividers, tick boxes are now on/off switches, buttons are rounded and quieter, lists have roomier rows with a clear selection, and the title bar matches the window on Windows 11.
- Less text: the explainer cards on the Connection and Audio pages are gone, card headings no longer repeat the page name, and the remaining help lines are one sentence.

## 1.0.3
- **Switch overlays from the app with no OBS refresh.** The live URL page now rebuilds itself in place when you pick a different overlay, look, theme, style, scale, plate or history option in the app: the WebSocket, the browser source and OBS all stay as they were. Before, the page navigated to a new address on every change, which OBS did not always follow (and the custom-keys overlay could reload endlessly because of the `|` in its address). The last known input state is shown on the new overlay immediately, and the 3D controller frees its renderer when swapped out.

## 1.0.2
- **Audio: "only one usage of each socket address" fixed.** When the audio link failed to start part-way (the other PC not up yet, a device missing), the half-started session was dropped instead of disposed, so its receiver kept the audio port and every retry failed with that error. The start, stop and restart paths now match AudioBridge's: a failed session is disposed, and start and stop wait for each other so a new socket is never bound before the old one is closed.

## 1.0.1
- **Update notifications.** KennelBridge checks GitHub for a newer release a few seconds after it starts and every six hours after that. When one is out, an amber **Update available** button appears in the header, the tray icon shows a balloon once per version, and the button opens a window with what changed, a Download button and a link to the changelog. **Check for updates…** in the tray menu runs the check by hand.
- **Changelog.** This file, published at kennel.gg/bridge/changelog and shown in the app's update window.
- Checkboxes are drawn by the app: an amber box with a black tick when on, so the state is obvious on the dark cards.
- The overlay options no longer run off the right edge of the window: theme, style, scale and the plate/history switches sit on one row, the 3D model and its folder on the next.

## 1.0.0
- First release. One app that replaces InputOverlayBridge, HotkeyBridge and AudioBridge, with a shared Connection page (Gaming PC / Streaming PC, passphrase, port, one firewall rule) and one page per bridge.
- **Input Overlay:** everything from InputOverlayBridge, including the live OBS URL, the ready-made looks and the bundled 3D Xbox, DualSense and DualShock 4 models.
- **Audio:** the AudioBridge engine. Game audio to the streaming PC, the microphone back through VB-CABLE, three latency presets, auto-start and reconnect.
- **Hotkeys:** everything from HotkeyBridge, including hold rows, pass-through, mouse buttons and the Discord push-to-mute setup.
- **FileBridge (new):** finished OBS recordings copied from the streaming PC's recording folder to a folder on the gaming PC. Waits until OBS has finished writing, remembers what was sent, optional delete after send, Sync now.
- Kennel dog-head icon, larger text and a scrolling layout for small windows.
