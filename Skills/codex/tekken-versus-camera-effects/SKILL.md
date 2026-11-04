---
name: tekken-versus-camera-effects
description: Recover, extract, validate and wire Tekken 7 versus camera tracks and events into the Unity remaster, with a camera demonstration scene. Use for battle close-ups, Rage Art cameras, shake, zoom, camera cuts, lens changes and return-to-gameplay transitions. Establish behavior from original archives, native code or original-game observations; keep remaster approximations explicit.
---

# Tekken Versus Camera Effects

Recover the original game's camera behavior and reproduce the supported parts in the existing Unity project. Separate four claims: original asset samples, original event dispatch, Unity implementation and observed visual equivalence. Do not infer camera rules from existing Unity code or a plausible property name.

## Discover and recover

The current workspace is `D:/games/Tekken.7.Ultimate.Edition.v5.10`, project `UnityRemaster/TekkenVersusRemaster`. Read its `Tools/source-paths.json`, `Docs/BATTLE_CAMERA_EFFECTS.txt` and current camera implementation. Preserve unrelated work and scene edits. Skill work or original-source analysis does not require stopping Unity; source edits do require stopping the correct project's active Play session.

Read [original-camera-evidence.md](references/original-camera-evidence.md) before extracting or changing event semantics. Run the existing project tool from workspace root:

```text
python UnityRemaster/TekkenVersusRemaster/Tools/CameraEffects/export_camera_effects.py --workspace . --out <new-local-directory>
```

This reads original `tkdata.bin` entries for the imported fighters and verifies native shake dispatch against the inspected executable. Its default characters are jin, kaz, xia, asa and ltn; `--characters` narrows them. Output contains original entry bytes, raw event offsets, source hashes, converted tracks and explicit limitations. It does not modify Unity resources automatically. Inspect and validate the output before deployment.

Treat unrecognized layouts, source revisions and missing dependencies as visible gaps. Never substitute another fighter's track or guessed shake values while calling them original. Preserve raw frame flags; a masked frame value does not by itself prove timing semantics.

## Establish behavior

For every automatic camera effect, identify the original condition, event phase/frame convention, owner, side selection, anchor/bone, coordinate basis, lens, lifetime, interruption and return behavior. Cite original executable hash plus function/case bytes or a controlled original execution trace. Original footage should identify game mode, characters, action, side, settings and timestamps. A manual camera preview proves neither the automatic trigger nor the paired opponent's motion.

The existing 0x8439/0x843A track-side interpretation is current remaster behavior until individually validated. The KO is native (`Tools/CameraEffects/export_ko_camera.py`, catalog `koCamera`, 2026-10-28): 1% game speed for 90 frames (150 after a Rage Drive, 0x801B), shake preset 6, and camera mode 7 holding the shown view at FOV 35 (5 in / 90 hold / 20 out); a Rage Drive KO plays mode 8 instead (catalog `koCamera.rageDrive`: 150 frames, no blend, low telephoto off the fight line, FOV 20 -> 35, swing and roll; side/roll direction interpreted, unverified on footage). 0x84CC is not an airborne zoom: native case 0x140719875 makes it a sticky per-fighter camera request that scales the follow rate (`Tools/CameraEffects/export_follow_rates.py`, catalog `followRate`); the old guessed zoom was removed on 2026-10-28. Shake presets 3–6 have native dispatch evidence, and their amplitudes, durations and waveforms are recovered as 10 per-frame pitch tables (`Tools/CameraEffects/export_shake_presets.py`, catalog `shakePresets`; see original-camera-evidence.md). The per-hit shake in versus (`BattleEffectTuning.hitShake`, presets 3/4 on clean/counter hits, with preset and strength sliders) is a labelled remaster choice. Remaster settings in `Resources/BattleEffectTuning.asset` (F6 panel, ORIGINAL/DEFAULTS presets, F5 forces a trade): the KO slow-down hits only the winner by default (`koFreezeWinnerOnly`; ORIGINAL restores the global 1% freeze), the trade's length, easing and centring, and `centreKo` for the KO close-up. The user asked for the hit shake because ordinary knockdowns carry no shake property. Continue analysis or provide a labeled manual comparison for unresolved behavior. Only introduce a new guessed automatic rule when the user explicitly asks for an approximation.

## Integrate and demonstrate

Read [unity-camera-workflow.md](references/unity-camera-workflow.md) for the runtime and demonstration. Share camera sampling between versus and the demo. Retain original tracks' lateral motion, elevation, shot scale and changing lens; do not flatten all close-ups to one frontal camera. Keep user-requested framing adaptations distinct from source facts.

Build the requested scene without overwriting unsaved scenes. Keep all evidence, captures and reports local. The camera lab must label native tracks and comparison settings separately and expose pause, restart, scrub, facing and track selection. Missing action-body or paired-body playback must be identified rather than concealed.

Validate source bounds and frame counts, finite samples, descriptor layouts, unit/basis conversion, horizontal-to-vertical FOV handling, both sides, seek determinism, pause and restoration of the gameplay camera. Exercise interrupted actions and round restart if live dispatch changed. Compare actual rendered original/remaster events before declaring original visual parity; compilation or a hash check is not that comparison.

Report what was extracted, wired and rendered, with remaining unknowns. Do not describe all battle cameras as complete merely because the imported fighter set has valid tracks.
