# KennelBridge

One tray app for a **two-PC streaming setup**. It replaces the three separate apps
(InputOverlayBridge, AudioBridge, HotkeyBridge) with one exe, one connection and one page per bridge:

| Page | What it does | Gaming PC | Streaming PC |
|---|---|---|---|
| **Connection** | Role, the other PC, passphrase, port, firewall. Set once, used by every bridge. | | |
| **Input Overlay** | Live keyboard / mouse / controller overlays for OBS. | captures and sends inputs | serves the overlay pages (`http://localhost:47790/live`) |
| **Audio** | Game audio one way, your microphone the other. | sends game audio, receives the mic into VB-CABLE | plays game audio in the headphones, sends the mic |
| **Hotkeys** | Press a key here, the other PC presses too (push-to-mute, scene switches). | both ways | both ways |
| **Files** | Finished OBS recordings copied from one PC's folder to the other's. | receives into a folder | watches the recording folder and sends |
| **Activity** | Live console of everything the bridges do, plus the log file. | | |

The **same exe runs on both PCs**. Pick *Gaming PC* on one and *Streaming PC* on the other; every
bridge works out its direction from that. Each bridge has its own on/off switch, so you can use only
the ones you want.

## Setup (a few minutes)

1. Copy `KennelBridge.exe` to both PCs and run it. No install, no runtime needed. The first run opens
   a wizard: role → passphrase + firewall → find the other PC (auto-discovered on the LAN, with a link
   test) → tick the bridges you want → (Discord buttons, on the gaming PC) → done. **Setup wizard** in
   the header reopens it.
2. Click **Firewall…** once on each PC (UAC prompt). One rule covers every bridge. Without it the PCs
   do not find each other and nothing arrives.
3. Then per bridge:
   - **Input Overlay** (streaming PC): pick an overlay, **Copy live URL**, and in OBS add one Browser
     source with it (any size, untick "Shutdown source when not visible"). Whatever you select in the
     app shows in OBS at once, no refresh needed. Each overlay also has a fixed URL for a source that should not follow the
     selection. `&demo=1` shows fake input while positioning.
   - **Audio**: tick *Audio bridge on* on both PCs, pick the playback device (gaming PC: the one your
     games play through; streaming PC: your headphones) and, on the streaming PC, the microphone. The
     gaming PC needs **VB-CABLE** (free, from vb-cable.com) so the mic can appear as a real
     microphone: pick *CABLE Output* as the mic in your game or Discord.
   - **Hotkeys**: **Add press…**, press the key or mouse button you will use here, then what the other
     PC should do. *Hold* mirrors how long you hold it. The **Discord…** button sets up push-to-mute,
     toggle mute and toggle deafen with spare F13–F24 keys.
   - **Files**: on the streaming PC tick *Send new recordings*, point it at OBS's recording folder; on
     the gaming PC tick *Receive recordings* and pick a folder. A recording is sent once OBS has
     finished writing it (size stable for 10 s and no longer open). Delivered files are remembered so
     nothing goes twice; **Sync now** scans the folder right away; *Delete here after the other PC has
     it* frees the streaming PC's disk.

**Streamer mode** (on by default) hides every IP address on screen. Close the window and the app keeps
running in the tray; the amber icon flashes green on activity. **Pause** in the header stops every
bridge at once.

## Soundboard

The **Soundboard** page searches Openverse, an open catalogue of Creative Commons audio (Freesound,
Wikimedia and more). Preview, then **Add to board**: the sound downloads once to
`%APPDATA%\KennelBridge\sounds` and plays instantly after that. With *Play on both PCs* on, a press
on either PC plays on both, and the other PC adds the sound to its own board. On the gaming PC sounds
go into **CABLE Input** by default, mixed with your streamed microphone, so the game and Discord hear
them; on the streaming PC they go to the normal output for OBS. Only CC0, CC BY and CC BY-SA sounds
are listed; right-click a sound to copy the credit line CC BY asks for (a stream description is fine).

**Hold a key while a sound plays:** under Output → *While playing*, choose a row from your Hotkeys
list (for example one whose action on the gaming PC is the game's proximity-chat key). The PC you
press the sound on holds that key on the other PC for the length of the sound. Right-click a sound to
give it its own key. The gaming PC must have the hotkey bridge on and set to receive (Both is fine).

## Problems

**Collect diagnostics** on the Activity page (or in the tray menu) saves a zip to the Desktop with
the log, your settings with the passphrase removed, and a short system and audio-device report.
Nothing is uploaded; send the zip to whoever is helping you.

## Updates

KennelBridge checks GitHub for a newer release a few seconds after it starts and every six hours.
When one is out, an amber **Update available** button appears in the header and the tray icon shows
a balloon once; the button opens what changed with a Download button. **Check for updates…** in the
tray menu runs the check by hand. Nothing is installed automatically: download the new exe, close
KennelBridge and replace the old one on both PCs. Every version is listed in
[CHANGELOG.md](CHANGELOG.md) and at https://kennel.gg/bridge/changelog/.

## Ports

| Port | Used for |
|---|---|
| UDP 47850 | hotkeys, input snapshots, link test |
| UDP 47851 | finding the other PC (broadcast) |
| UDP 47852 | audio (raw 48 kHz stereo 16-bit PCM, both directions) |
| TCP 47853 | file transfers |
| TCP 47790 | overlay pages for OBS, localhost only (no firewall rule needed) |

All traffic is plain LAN traffic protected by the shared passphrase. Do not expose the ports to the
internet.

## Notes

- Controllers: Xbox pads through XInput; DualSense, DualShock 4, Switch Pro and other HID pads through
  Raw Input. Three real 3D models are bundled (Xbox, DualSense, DualShock 4, CC-BY, see
  [CREDITS.md](CREDITS.md)); your own glTF goes in `%APPDATA%\KennelBridge\models`.
- Hotkey injection uses `SendInput`. Games with kernel anti-cheat may ignore synthetic input, and an
  app running as administrator only accepts input from KennelBridge running as administrator too.
- Holds are kept alive every 100 ms; if the receiving PC hears nothing for half a second it releases
  the key itself, so nothing stays stuck.
- Audio is uncompressed, about 1.5 Mbit/s per direction. Use a wired LAN. Both PCs should use the same
  latency setting.
- Settings: `%APPDATA%\KennelBridge\settings.json`. Log: `%APPDATA%\KennelBridge\kennelbridge.log`.

## Build

Needs the .NET 8+ SDK (https://dot.net). On Windows double-click `build.cmd`; anywhere:

```
dotnet publish KennelBridge.csproj -c Release -o dist
```

produces a single self-contained `dist\KennelBridge.exe`.

## Licence

MIT, see [LICENSE](LICENSE). The bundled controller models are CC-BY 4.0, three.js is MIT and NAudio
is MIT, see [CREDITS.md](CREDITS.md). VB-CABLE is not bundled and stays under VB-Audio's own terms.
