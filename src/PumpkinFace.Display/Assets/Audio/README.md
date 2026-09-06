# Performance audio

Voice reactions use real recordings; the saber uses synthesized sound effects. The rejected “hmm” has been removed. No synthesized reactions, voice cloning, AtmosFX material, or Ghostbusters recording is included. Typed speech remains separate.

## Sources and licenses

Source pages and explicit license labels were checked September 6, 2026. Source MP3s are the publicly available high-quality previews, retained under `tools/audio-sources/` for reproducible assembly.

- **BOO AND LAUGH.wav**, metrostock99 — [source](https://freesound.org/people/metrostock99/sounds/540686/), [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/), [public MP3](https://cdn.freesound.org/previews/540/540686_1256155-hq.mp3). The creator describes their own Boo followed by a laugh with reverb.
- **Dramatic Organ, A.wav**, InspectorJ ([www.jshaw.co.uk](https://www.jshaw.co.uk/)) — [source](https://freesound.org/people/InspectorJ/sounds/402095/), [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), [public MP3](https://cdn.freesound.org/previews/402/402095_5121236-hq.mp3). InspectorJ’s church-organ performance of an excerpt from Bach’s Toccata and Fugue in D Minor. Credit the recording and retain the license link when distributing it. The switchboard also displays the performer and license.

## Editing and timing

All output is mono, 24 kHz, 16-bit PCM, imported without lossy compression. Runtime playback adds -5 dB gain. Edits comprise resampling, normalization, trimming, placement and fades; no pitch changes or time stretching.

| Asset | Duration | Source editing |
| --- | --- | --- |
| jack-playful-scare.wav | 13.5 s | Boo/laugh source 0.48–3.50 s, peak 0.72, placed at 5.82 s; the original vocal spacing is intact |
| boo.wav | 2.7 s | First 1.15 s of that reaction, placed at 0.82 s, with a 40 ms tail fade |
| chuckle.wav | 3.5 s | Reaction from 1.18 s onward, placed at 0.30 s |
| haunted-organ.wav | 20 s | Organ source 0–19.9 s, peak 0.72, 350 ms tail fade, silence to 20 s |

The full scene is silent before 5.82 s. `JackVocalTrack.cs` contains 10 ms RMS envelopes from the full reaction PCM, with explicit Boo (5.82–6.97) and Laugh (7.00–8.84) regions and authored vowel shapes. Short routines reuse this time base. Quiet reverb tails are excluded from jaw energy. Instrumental music uses expressive swaying and blinking, without mouth chatter. User-selected songs use an adjustable dance tempo rather than automatic beat detection or lip-sync.

## Rebuilding and validation

```sh
python3 tools/build_jack_reactions.py --ffmpeg /path/to/ffmpeg
```

Reimport changed audio in Godot before launching/exporting. Engine checks cover every built-in clip, runtime-loaded song playback, invalid selections, mute, stop, replay and completion. Pose checks cover bounds, return to rest, removed hum cues and music without speech. These checks do not assess subjective audio quality.

## Vader’s entrance

`vader-entrance.wav` uses **Darth Vader Breathing** by **ihitokage**, who describes recording their own breathing into a microphone. [Source](https://freesound.org/people/ihitokage/sounds/553154/), [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/), [public high-quality MP3](https://cdn.freesound.org/previews/553/553154_1675920-hq.mp3). The source page and license were checked September 6, 2026. This is a creator-recorded character-inspired effect, not extracted film dialogue or a cloned actor’s voice.

Source seconds 0–11.9 are normalized to peak 0.72, faded in over 120 ms and out over 400 ms, placed at 0.65 s in a 13.5 s scene. The output is mono 24 kHz, 16-bit PCM; playback uses the library’s -5 dB gain. No generated speech or pitch shifting is added. `VaderBreathTrack.cs` contains a 50 Hz energy envelope measured from the isolated breathing PCM with a 120 ms window, so the saber effects do not drive breath movement. It drives restrained vertical motion and light variation while the mouth stays closed. Authored eyes turn toward the audience and finish with a slow nod, then restore the selected expression and lighting.

Rebuild with `python3 tools/build_vader_performance.py --ffmpeg /path/to/ffmpeg`, then reimport audio in Godot. This builder does not change the existing vocal or music assets.

### Lightsaber layer

**Lightsaber Ignition** by **pip_** — [source](https://freesound.org/people/pip_/sounds/557194/), [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/), [public high-quality MP3](https://cdn.freesound.org/previews/557/557194_6737463-hq.mp3). The creator made the effect by layering synthesizers in Logic Pro X. Source and license checked September 6, 2026.

The ignition is normalized to peak 0.52 and starts at **3.25 s**. A new low electrical drone (60/120/180 Hz partials) sustains until shutdown at **10.40 s**, fading by **10.95 s**. The first 0.65 s of the ignition is reversed, normalized to peak 0.36 and edge-faded to make the shutdown effect, ending at 11.05 s. The whole mix is peak-limited by a single gain adjustment only if needed to stay below 0.92. Breath timing is preserved. No new speech is generated.

The blade itself stays outside the projected face. A red line-shaped light source illuminates curved skin, carved walls and the cavity, with red eye reflections. The light ignites in 320 ms, drifts closer across the held performance, and fades during shutdown. Its strength and position are pose channels; Stop releases them over the same 240 ms as the face, and other performances leave them at zero. Candle brightness is reduced during the saber cue to keep the red spill readable; saved lighting calibration is unchanged.
