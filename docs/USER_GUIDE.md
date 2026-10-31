# MimeWalk: user guide

MimeWalk speeds you up and slows you down on every step while you mime
walking in place, so your movement looks like real walking
instead of a constant glide.

There are two parts. You need both.

- **The avatar part** (`MimeWalk.unitypackage`) adds a MimeWalk menu
  to your avatar, plus an optional light and click on each step. Only you see
  and hear them. The light shows only in your own view: not to other players,
  and not in mirrors, VRChat's camera (including the Stream Camera) or photos.
- **The app** (`MimeWalk.exe`) runs in a console window next to VRChat. It
  reads your menu and sends the pulsing movement.

## 1. Add it to your avatar (once per avatar)

You need Unity with the VRChat Avatars SDK (through the VRChat Creator
Companion), plus:
- **Modular Avatar** or **VRCFury**. Modular Avatar 1.18+ and VRCFury
  1.1431+ are recommended; those are the versions it was tested with.
- **Poiyomi Toon 10 or newer**, free or Pro, for the light. This makes the
  avatar part PC-only. If the lights show pink in Unity, select the
  `MimeWalk_LightL` and `MimeWalk_LightR` materials and pick
  `.poiyomi/Poiyomi Toon` as their shader.

1. Open your avatar's project and double-click `MimeWalk.unitypackage`,
   then click **Import**.
2. From the `MimeWalk` folder, drag `MimeWalk (Modular Avatar)` or
   `MimeWalk (VRCFury)` onto your avatar in the Hierarchy. Use the one
   for the tool you have.
3. Choose your feedback. Expand `MimeWalk ▸ Head`:
   - for **sound only**, untick (or delete) `Visual`
   - for the **light only**, untick (or delete) `Audio`
   - leave both for both
   - to move the light, select `Visual` and change its position
4. Save the scene (Ctrl+S) and upload as usual.

**Test it in Unity first:** enter Play mode with Gesture Manager, turn on
**Walk** and **Feedback** in its radial menu, then tick and untick `MimeWalk/Beat`
in its parameter list. Each change should flash the light (left blue, right
red) and play a click. If that works, the avatar part is fine.

## 2. Turn on OSC in VRChat (once)

Open **Action Menu ▸ Options ▸ OSC** and switch it on. After re-uploading an
avatar with changed parameters, use **OSC ▸ Reset Config** while wearing it.

## 3. Run the app and walk

Extract the zip first, then run `MimeWalk.exe`. It opens a console (command
prompt) window showing a status line; leave it open while you play, and close
it (or press Ctrl+C) to quit, which always stops your movement. Windows may
warn about an unknown publisher; choose **More info ▸ Run anyway**. In
VRChat, open **Action Menu ▸ Expressions ▸ MimeWalk**:

| Control | What it does |
|---|---|
| Walk | Start or stop walking |
| Feedback | Light and/or click on each step |
| Tempo | Steps per minute (40–200) |
| Surge | How much your speed rises and falls each step (0–50%) |
| Speed | Average speed |

The app keeps a beat at your tempo. Plant a foot on each beat: that's when
the light flashes and the click plays. Your speed is highest on the beat and
lowest halfway between beats.

## Settings

The first time the app runs, it creates `config.toml` next to
`MimeWalk.exe`. Open it in Notepad. Every setting has a comment. Save, then
restart the app. If a setting has the wrong kind of value, the console window
says which one. When the app starts, it moves the avatar's Tempo, Surge and
Speed radials to the starting values from the config. The settings include:

- starting tempo, surge and speed
- the surge shape (`sharpness`)
- speed-up and slow-down time
- what 0% and 100% on the radials mean
- the OSC ports

See [CHOREOGRAPHY.md](CHOREOGRAPHY.md) for tips on matching your mime to the
steps.

## Troubleshooting

- **Antivirus blocks MimeWalk.exe.** Some antivirus programs wrongly flag
  small unsigned apps packaged like this one. Allow it, or run `mimewalk.py`
  with Python instead.
- **I keep moving after the app closed.** VRChat keeps the last movement it
  received. Closing the app normally always stops you; only a crash or a
  force-kill can leave you moving. Start the app again to stop.
- **Nothing moves.** Check that OSC is on, and that the console window doesn't
  say it can't listen on port 9001. If it does, another OSC app is using that
  port: close it, or change `listen_port` in `config.toml` and point that
  other app elsewhere.
- **I can't find the menu.** A radial ring holds 8 items. With many add-ons,
  MimeWalk may be on the "Next" or "More" page. Drag the `MimeWalk`
  object higher in your avatar's Hierarchy to move it to the first page.
- **No light or click.** Do the Unity test above first. If that works but
  VRChat doesn't, reset the OSC config (step 2), and make sure Feedback is on
  while you're walking.
- **Other people can't see the surge well.** Raise Surge to 20–30%.
- **Streaming:** the light stays out of VRChat's Stream Camera, but VRChat's
  normal desktop window shows what your headset sees, so the light appears
  there. The click is game audio, so it's in the stream unless you turn
  Feedback's sound off by unticking `Audio`.
