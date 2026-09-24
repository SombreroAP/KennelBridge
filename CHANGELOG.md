# Changelog

All notable changes to KennelBridge. Release notes on GitHub are taken from here.

## 1.7.0
- **MyInstants on the soundboard.** Browse MyInstants opens myinstants.com in a browser window inside KennelBridge (the Edge WebView2 engine that ships with Windows), and every sound on its pages gets a **+ Board** button. The file is fetched in that browser session and put straight on your board, saved on the PC. MyInstants only serves its sounds to a real browser, which is why it works this way rather than as a search in the app.
- **The other PC gets its own copy.** It cannot fetch MyInstants files itself, so KennelBridge sends the file over your LAN when you add it and before the first play on both PCs. This uses a new TCP port, 47854 (the file bridge's port + 1): press **Firewall…** on the Connection page again on both PCs so it is let in.
- MyInstants sounds are uploads by its users; the window says so, and the streamer is responsible for having the right to play them.

## 1.6.0
- **The board comes first.** On the Soundboard page your board is now large and on top, next to Find sounds, with the Output settings underneath. Sound buttons are bigger.
- **Hover a sound and press Delete or Backspace** to take it off the board (ignored while you are typing in the search box). Right-click → Remove still works.
- **Longer sounds and music.** Find sounds has a length choice: Short (effects under a minute, as before), Long (anything up to 10 minutes) and Music (Creative Commons tracks from Jamendo, up to 10 minutes). All still limited to licences that are fine on a monetised stream.
- **Long sounds start at once.** A sound over 20 seconds that is not saved yet now streams straight from the web when pressed, and is saved in the background for next time, instead of waiting for the whole file to download.

## 1.5.2
- **Stop all sounds stops everything.** It missed three kinds of sound: one still downloading the first time it was pressed (it started when the download finished), one waiting the 150 ms for push-to-talk to open, and, with Play on both PCs on, everything on the other PC. Now a stop cancels presses that have not started yet and tells the other PC to stop too.

## 1.5.1
- **On the streaming PC the soundboard plays into your microphone.** A new Play into choice, **My microphone**, now the default on the streaming PC, mixes the sound into the microphone the audio bridge sends to the gaming PC, so it arrives in CABLE together with your voice and the game and Discord hear both. It needs the audio bridge running. With Play on both PCs on, the gaming PC no longer also plays a sound into CABLE when the streaming PC already put it in the mic, so nothing is heard twice. Stop all stops mic sounds too, and Mute my mic still silences your voice while leaving the sound in.

## 1.5.0
- **The soundboard's hold key always goes to the gaming PC.** Soundboard → While playing now lists every key from both PCs' Hotkeys lists once, and whichever you pick is held on the gaming PC for the length of the sound, whichever PC's list it came from and whichever PC you pressed the sound on (on the gaming PC itself it is simply pressed there). A new switch, **Also press it on this PC**, holds it on the streaming PC as well.
- **Two outputs.** Soundboard → Output has a second, optional **Also play into** besides the required Play into, so one press can go to the gaming PC's mic path and, on the streaming PC, into your own microphone chain as well. A real microphone cannot be played into, so pick a virtual cable your mic also feeds (VB-CABLE, VoiceMeeter's input) and use that as the microphone in Discord or OBS.
- **Mute my mic while a sound plays** (off by default). On the streaming PC the microphone stops being sent to the gaming PC and the mic device itself is muted in Windows, so Discord and OBS there go quiet too; its mute state is put back exactly as it was afterwards. On the gaming PC the incoming mic is silenced before it reaches CABLE. Overlapping sounds share one mute, and the app never leaves the mic muted when it closes.

## 1.4.1
- **Hold list shows both PCs' hotkeys.** The two PCs now share their Hotkeys lists over the link (whenever a row changes, and every 15 seconds so a PC started later catches up), and Soundboard → While playing lists every row from both, each labelled with where its key is pressed: this PC's rows press their key on the other PC, the other PC's rows press theirs here. So a sound played on either PC can hold a key on either PC. The per-sound right-click choice lists the same rows.

## 1.4.0
- **Hold a key on the other PC while a sound plays.** Pick any row from your Hotkeys list under Soundboard → Output → While playing, and the PC you press the sound on holds that row's key on the other PC for exactly as long as the sound lasts. Use it to route a sound into the right in-game channel: play on the streaming PC, the sound reaches the gaming PC through CABLE, and the game's proximity-chat or push-to-talk key is held there at the same time. Right-click a sound → Hold while playing to give one sound its own key or none.
- The key goes down 150 ms before the sound starts, so push-to-talk is open for the first syllable, and is released 200 ms after it ends. It is kept alive the same way hold rows are, so if the link drops the other PC lets go within half a second. Overlapping sounds that hold the same key share one hold, and with Play on both PCs only the PC you pressed on sends it.

## 1.3.1
- **Popular sounds first.** The Soundboard's Find sounds list now opens on 30 classic streamer sounds before you search: air horn, sad trombone, ba dum tss, drum roll, applause, crowd laugh, crickets, wrong buzzer, victory fanfare, cha-ching, dun dun dun, ta-da, game over, level up and more. One short, clean version of each, all CC0 or CC BY, built into the app so the list appears instantly. The Popular chip brings it back after a search.

## 1.3.0
- **Soundboard.** A new page. Search a catalogue of Creative Commons sounds (Openverse, which covers Freesound, Wikimedia and more), or tap a category such as air horn, applause, laugh or drum roll, preview a sound, and add it to your board. It downloads once and is saved on the PC, so it plays instantly after that. No files to find or name yourself.
- **Plays on both PCs.** Pressing a sound on either PC plays it there and tells the other PC to play it too; the other PC fetches it the first time and adds it to its own board, so the two boards stay the same.
- **Into the mic.** On the gaming PC sounds play into CABLE Input by default, where Windows mixes them with the microphone the audio bridge delivers, so the game and Discord hear them. On the streaming PC they play on the normal output so OBS picks them up. Either can be changed under Output, with a volume setting and Stop all.
- Only licences that are fine on a monetised stream are offered (CC0, CC BY, CC BY-SA). Right-click a sound to copy its credit line, which CC BY sounds ask for.

## 1.2.0
- **Audio crackling fixed at the source.** Three things in the playback loop caused it, whichever latency setting was picked: the loop slept on Windows' default 15.6 ms timer, so the sound card's 25–40 ms buffer could run dry between wake-ups; nothing kept a cushion in front of the card after start-up, so ordinary network jitter emptied it; and clock drift between the two PCs was corrected by throwing away a whole 5 ms block at once, an audible click. Now the loop runs on a 1 ms timer, keeps a cushion of 20 / 35 / 70 ms (Lowest / Balanced / Most stable) and rebuilds it after any gap, follows drift one sample-frame at a time, and fills a lost packet with a quieter copy of the previous one instead of a hole of silence. Latency goes up by about the cushion.
- **Collect diagnostics.** A button on the Activity page (and in the tray menu) saves one zip to the Desktop: the log, settings with the passphrase removed, a system report (Windows and app version, display scaling, network adapters with Wi-Fi or cable and link speed, every audio device with the format Windows runs it at) and what the app is showing right now. Nothing is sent anywhere; you pass the zip on.
- While audio runs, the log gets one line every 10 seconds with buffer level, packets received and lost, late packets, underruns, trims and drift, so a crackle can be matched to what caused it. The Audio page's status line now shows loss and underruns too.

## 1.1.2
- **Windows display scaling (125 %, 150 %…).** Text grew with the scaling but the rows, cards and page heights it sits in did not, so at 125 % everything was crowded. Every fixed row and column, the minimum page height, and the switches, cards, rail and status dots now scale with the monitor, including when you drag the window to a monitor with different scaling. Windows are also kept inside the screen, so a 1080p laptop at 125 % gets a window that fits rather than one taller than the display.

## 1.1.1
- The Startup card on the Connection page cut off its last switch (Streamer mode). It now has room for all three.

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
