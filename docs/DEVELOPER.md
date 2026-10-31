# Developer notes

## Layout

```
app/mimewalk.py        the whole app (config, surge curve, step clock, OSC)
app/test_mimewalk.py   tests (pytest), including a run of the real app against the fake VRChat
app/fake_vrchat.py        pretend to be VRChat, for trying the app without the game
app/build_exe.ps1         runs the tests, then builds dist/MimeWalk.exe (console, one file)
unity/MimeWalkDev/     dev Unity project; Assets/MimeWalk is what ships, Assets/MimeWalkBuild builds it
tools/                    build_unitypackage.ps1, make_sounds.py
```

## Parameter contract

All parameters use the fixed prefix `MimeWalk/` (`PARAMS` in the app, `P` in the builder) and are local-only: 0 synced bits.
On start, and 2 seconds after an avatar change, the app sends `On` (false) and its current `BPM`, `Surge` and `Speed`
to the avatar, so the menu always shows what the app is using.

| Param | Type | Direction | Meaning |
|---|---|---|---|
| `On` | bool | menu > app | Walk toggle |
| `Feedback` | bool | menu > avatar | Light and/or click on each step |
| `BPM`, `Surge`, `Speed` | float 0–1 | menu > app | Radials, mapped by `bpm_min`/`bpm_max`, `surge_max` and 0–1 |
| `Beat` | bool | app > avatar | Flips on every step: true = left foot, false = right. Reset to false when stopped. |

## App

```bash
cd app && .venv/Scripts/python -m pip install python-osc pytest pyinstaller
```

```bash
cd app && .venv/Scripts/python -m pytest -q
```

```bash
powershell -ExecutionPolicy Bypass -File app/build_exe.ps1
```

To try it without VRChat:
1. Write a config with `send_port = 19000` and `listen_port = 19001`.
2. Run `python mimewalk.py thatconfig.toml`.
3. In a second window, run `python fake_vrchat.py 19000 19001`. It walks for
   6 seconds and reports the average, swing, beats and the stop.

Don't use the default ports while VRChat is running: the app will really move
you.

**The surge curve:** `c(φ) = 2·(m − |sin πφ|^p)`, where p is `sharpness` and
m is the mean of `|sin πφ|^p`. It averages 0 and spans exactly 2. So the input
`speed · (1 + surge · c)` averages `speed` and spans `2·surge·speed` from slowest to fastest. Speed is
highest at the footfall (φ = 0) and lowest at midstance (φ = 0.5).

## Unity package

The dev project is `unity/MimeWalkDev`: Unity 2022.3.22f1, VRChat SDK
3.10.5, Modular Avatar 1.18.7 with NDMF, VRCFury 1.1431, and Poiyomi Toon
10.0.25 with ThryEditor 2.74.3. Open it once in the VRChat Creator Companion to
restore its packages.

**What ships.** `Assets/MimeWalk` holds assets only, with no scripts, so
recipients have nothing to compile:

- **Two prefabs** with the same hierarchy:
  - `MimeWalk` carries either MA Merge Animator + Parameters + Menu
    Installer, or a VRCFury Full Controller with `globalParams "*"`.
  - `Head` is attached to the head bone (MA Bone Proxy or VRCFury Armature
    Link). It has a VRC Head Chop at scale 1, so it stays visible in first
    person.
  - `Head/Visual/LightL` and `LightR` are blue and red quads at scale 0.
  - `Head/Audio/ClickL` and `ClickR` play the tick and tock: 3D AudioSources,
    inactive until a step.
- **`MimeWalk_FX.controller`**, with one layer called "MimeWalk Feedback" (write
  defaults off):
  - Off goes to Ready when `On`, `Feedback` and `IsLocal` are all true.
  - Each flip of `Beat` then plays Left (true) or Right (false).
  - Any State goes back to Off when On or Feedback turns off.
- **Light material:** the lights use `.poiyomi/Poiyomi Toon` (Poiyomi 10+,
  free or Pro; PC only). The builder sets the same values Poiyomi's own
  controls would:
  - the Cutout preset, so only the dot and its black ring show
  - emission, so the dot looks the same in any world
  - Mirror/Camera Visibility, so only the normal view draws it, not mirrors,
    VRChat's camera or screenshots

  Poiyomi locks the shader automatically when the avatar is uploaded.
- **The rest:** the menus, `MimeWalk_Parameters.asset`, the flash clips, the light
  texture and materials, and `Audio/MimeWalk_Tick.wav` and `MimeWalk_Tock.wav` (made by
  `tools/make_sounds.py`).

**Rebuild and export.** `Assets/MimeWalkBuild/Editor/BuildPackage.cs`
(dev only, never exported) does this, from **Tools > MimeWalk (dev) >
Build Package** or headlessly:

```bash
powershell -ExecutionPolicy Bypass -File tools/build_unitypackage.ps1 -ReimportTest
```

1. It regenerates everything, keeping each file's GUID.
2. It runs the FX layer in edit mode on each prefab. Flipping Beat must give
   left, right, left, and nothing must happen with Feedback off or for a
   remote player.
3. It refuses to export if any script ended up in the package, then writes
   `dist/MimeWalk.unitypackage`.
4. `-ReimportTest` also imports the package into a scratch project and checks
   it there.

Close the dev project in Unity first. Logs go to `unity/Logs`. If Unity 2022.3.22f1
isn't in Unity Hub's default folder, add `-Unity "path\to\Unity.exe"`.

## Release

Build the exe and the package, then attach `dist/MimeWalk.exe`,
`dist/MimeWalk.unitypackage` and `app/mimewalk.py` to a GitHub release. The
version number is in `app/pyproject.toml`.

## Only in-game testing can confirm

- the light is visible in first person (it relies on VRC Head Chop)
- the click is audible
- other players see and hear neither
- partial `/input/Vertical` values scale speed smoothly in the worlds you use

(Built with Claude Code - Opus 5.5 Ultracode)
