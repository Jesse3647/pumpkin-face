# Pumpkin Face

Pumpkin Face is a GPU-accelerated jack-o-lantern face for projection onto a physical pumpkin. A private operator window controls a separate, clean output window while both display the same rendered framebuffer. The face is built from deformable geometry rather than prerecorded video, leaving the mouth ready for future speech animation.

The procedural carving uses independently authored eye sockets, ember-rimmed shadow pupils, an irregular cut nose, and a broad stepped three-tooth grin. Each opening is layered as restrained spill light, a soot-dark lip, uneven directional pumpkin flesh, a recessed cavity, and a shared low candle plane with cream-hot cores and amber falloff. The background stays completely black so the physical pumpkin remains the visible surface around the projected cuts.

Version 1 includes four silent scenes:

- **Watchful** anticipates, lets each pupil investigate independently, double-blinks, and gives a curious little reaction.
- **Frightened** performs a compact cartoon startle, two diminishing shivers, and an embarrassed smile recovery.
- **Drowsy** loses the fight one eye at a time, yawns, pops awake, and settles sheepishly.
- **Mischievous** peeks and winks before locking forward and revealing a crooked, glowing grin.

Autoplay inserts randomized 3–8 second neutral pauses, shuffles the scenes without immediate repeats, keeps each performance inside a tight tempo window, and blends between expressions over 250 ms. Gaze, lid, and shake tracks use crisp linear timing for readable darts and blinks; mouth, brow, pupil, and candle tracks retain smoother curves.

## Requirements

- macOS, Windows, or Linux for development; the supplied export preset currently targets macOS.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). [`global.json`](global.json) pins SDK 8.0.100 and permits compatible patch updates in that feature band.
- [Godot 4.7.1 .NET](https://godotengine.org/download/archive/4.7.1-stable/), not the standard non-.NET editor.
- Matching Godot 4.7.1 export templates to produce the macOS application bundle.
- A GPU and graphics driver capable of Godot's Compatibility renderer.

The examples use `godot-mono` as the Godot executable. If it is not on your `PATH`, replace it with the editor executable, such as `/Applications/Godot_mono.app/Contents/MacOS/Godot` on macOS.

Verify the installed tools:

```sh
dotnet --version
godot-mono --version
```

The Godot version should report `4.7.1.stable.mono`.

## Build and run

From the repository root:

```sh
dotnet restore PumpkinFace.sln
dotnet build PumpkinFace.sln
godot-mono --editor --path src/PumpkinFace.Display
```

Press **F5** or the editor's Run Project button after Godot opens. To launch the application directly without opening the editor:

```sh
godot-mono --path src/PumpkinFace.Display
```

For a real projection, configure the projector as an **extended display**, not a mirrored display, before launching. Keep the operator window on the primary display and select the projector from **Projection output**.

## Operator controls

The operator window contains the exact projector preview, output controls, scene triggers, named calibration profiles, and fine adjustment controls. The projector window contains only the face on black.

| Control | Action |
| --- | --- |
| `Space` | Play the next shuffled scene now |
| `1` | Play Watchful |
| `2` | Play Frightened |
| `3` | Play Drowsy |
| `4` | Play Mischievous |
| `A` | Toggle autoplay |
| `F` | Toggle projector fullscreen |
| `Esc` | Leave projector fullscreen without quitting |

The keyboard shortcuts apply while the operator application has keyboard focus. The same scene, autoplay, output, and fullscreen actions are available as buttons.

### Safe output behavior

- At startup, a remembered display is reused when it still exists. If it is missing—or no display has been saved yet—the output opens safely windowed on the primary display.
- Automatic fullscreen is used only on a non-primary display when more than one display is available.
- With one display or the primary display selected, output opens as a centered 960×540 borderless window and the operator shows a warning.
- The output can be hidden and restored without closing the operator window.
- The projector background remains black, and the cursor is hidden while it is over fullscreen output.
- Alignment guides are off by default. Turning them on intentionally shows them in both preview and projector output, so turn them off before the decoration is presented.

### Align the face

Enable **Show alignment guides** to expose direct controls in the operator preview:

- Drag inside the face outline to move it.
- Drag a corner handle to scale it uniformly.
- Drag the top handle to rotate it.

Fine controls independently adjust horizontal and vertical position and scale, rotation, eye spacing, mouth position and scale, brightness, and gamma.

## Calibration profiles

Profiles let one computer remember different pumpkins, projector placements, or throw distances. The profile panel can create, rename, duplicate, delete, reset, and select profiles. **New** starts from the current calibration; **Duplicate** makes a named copy; **Reset selected profile** restores neutral alignment. The final remaining profile cannot be deleted.

Profile edits, autoplay state, and the selected display are saved automatically after a short debounce and flushed during normal shutdown. The versioned JSON state and recovery backup live under Godot's platform-specific user data directory at:

```text
user://pumpkin-face/application-state.json
user://pumpkin-face/application-state.backup.json
```

Writes use a temporary file and atomic replacement where the platform supports it. On startup, an invalid primary file falls back to the last-known-good backup and then safe defaults. Avoid editing these files while the application is running.

## Tests and project validation

Run all deterministic core and persistence tests:

```sh
dotnet test PumpkinFace.sln
```

The suites cover command ordering and capacity, shuffle timing, interruption, same-scene retrigger and autoplay rules, pose interpolation and clamping, calibration validation, profile round trips/migration/recovery, and future speech timing contracts.

Use Godot headlessly for a resource/import/C# smoke check:

```sh
godot-mono --headless --path src/PumpkinFace.Display --editor --build-solutions --quit
```

This smoke check does not validate native multi-display behavior or captured pixels. Those require an active desktop session.

## Deterministic visual captures

Capture mode renders 13 fixed neutral and scene poses at 1280×720, fixes the procedural candle clock, disables alignment guides, and exits automatically.

Image capture requires a real GPU-backed, windowed Godot session. **Do not add `--headless` or use a dummy display driver**: a headless smoke check can load resources, but it cannot reliably read back the application's GPU viewport.

Create or refresh a reference set:

```sh
godot-mono --path src/PumpkinFace.Display -- \
  --capture-dir="$PWD/captures/reference"
```

Compare a new capture with that reference set:

```sh
godot-mono --path src/PumpkinFace.Display -- \
  --capture-dir="$PWD/captures/actual" \
  --compare-dir="$PWD/captures/reference"
```

Comparison uses luma root-mean-square error with a tolerance of `0.035`. A successful run exits with code `0`; missing references, size changes, save failures, or visual differences above the threshold exit with code `2`; an unavailable GPU framebuffer exits with code `3` and an actionable message. Keep reference images tied to a known Godot version, renderer, and GPU because driver changes can produce small pixel differences.

## Export an unnotarized macOS app

Install the Godot 4.7.1 export templates, then run:

```sh
mkdir -p dist
godot-mono --headless --path src/PumpkinFace.Display \
  --export-release "macOS" "$PWD/dist/PumpkinFace.app"
```

The preset creates a universal macOS bundle using Apple's system `codesign` with the certificate-free `-` ad-hoc identity. It has no Apple Developer ID identity and is not notarized; the ad-hoc signature is required for a reliable launch on Apple Silicon but does not establish a trusted publisher. On another Mac, Finder may require **Control-click → Open** on first launch. Developer ID signing and notarization are intentionally outside V1.

Only the macOS export preset is currently supplied. The application architecture is portable, but Windows and Linux packages require corresponding Godot export presets and platform testing.

## V1 boundaries

V1 is silent and does not yet include speech playback, lip-sync inference, remote control, a local AI model, camera-based alignment, keystone/corner-pin correction, sound effects, Developer ID signing, or notarization. Projection calibration is an affine move/scale/rotate adjustment rather than surface mapping.

The project is intentionally scaffolded for those additions. See [Architecture and extension guide](docs/ARCHITECTURE.md) for the component boundaries and concrete next steps.
