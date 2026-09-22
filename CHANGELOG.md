# Changelog

All notable changes to KennelBridge. Release notes on GitHub are taken from here.

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
