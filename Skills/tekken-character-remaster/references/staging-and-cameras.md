# Staging, cameras and gameplay flow

## Discover and bind the actions

Use original move records and properties, not guessed numeric ranges. Native `*_sNN` and `*_wNN` stems are useful entrance/victory candidates in this dataset; verify their meaning for the requested fighter. Some story/paired records alias a clip. Preserve the records but avoid double-weighting identical entrance aliases when choosing randomly. Compare body, secondary and camera references before deduplicating by animation name.

The inspector lists aliases and all secondary/camera events. Resolve missing tracks explicitly. Paired victory variants (the `_y` takes such as Xiaoyu's `fu_w01_y`, move 2435, and earlier Jin/Kazuya pairs) usually have hand tracks but no standalone camera or face property. They get a labelled frontal full-body fallback, and the on-screen caption says so; do not claim those are original camera tracks.

Camera extraction rules that held for Jin, Kazuya and Xiaoyu:

- Read property 33845 (0x8435) from bank 4.
- Assert C8 format, four descriptors, and a sample count equal to the move's frames.
- Secondary properties are 33838 (left hand), 33839 (right hand) and 33986 (face).

`StagingCatalog.json` and `StagingCatalog.cs` connect character codes, move IDs, animation names, kinds, native frame lengths, camera resources, secondary resources and the specific landing edit flag. The catalog holds jin 9, kaz 10 and xia 9 takes. `bake_staging_tracks.py` bakes only Jin and Kazuya itself, assumes synchronized single events, and then appends the entries from `Assets/_Project/Characters/Xiaoyu/Source/Base/staging.json`, so a re-bake keeps Xiaoyu.

## Adding a fighter to staging_test

1. Bake the fighter's tracks with a per-character script, for example `UnityRemaster/prepare_xiaoyu_staging.py`. It writes `Characters/<Name>/Source/Base/staging.json`, `Resources/EntranceCameras/<code>_staging_<move>.bytes` (T7C1) and the SMT1 secondary resources.
2. Add a merge hook for that `staging.json` to `bake_staging_tracks.py`.
3. Add the `MuseumCharacter` asset's GUID to the `characters:` list in `staging_test.unity`. Edit the file on disk only when that scene is not open in the editor; check the Unity window title.
4. Make sure `StagingTestPlayer.Code()` resolves the fighter through `characterCode`, and that its bone dictionary ignores case.
5. Attach character dynamics (for example `XiaoyuSecondaryDynamics`) per take, after dressing. Call `Reset()` after the first `Present()`, tick with unscaled delta time, freeze while paused, and null them on take change.
6. Filter editor validators to the characters they support. `VictoryStaging.cs` filters by key.

## Sequence behavior

- Choose the character's entrance when starting the play session, before the cinematic clock starts. Avoid repeating the last selection if there is more than one choice. Use separate history keys for entrance and victory.
- Show one player entrance at a time, then the round/ready/fight sequence. Preserve the selected player sides/order for the user's reference rather than treating Kazuya-left/Jin-right as universal.
- Use the winning character's victory pool at match end. Wait for completion before automatic rematch; clear secondary transforms and presentation overrides on manual rematch or skip.
- Load/cache bind-relative tracks before rendering animated poses. Loading a track against an already posed skeleton contaminates its bind reference.
- Do not let expensive loading advance the first cinematic frame. Base presentation durations on the selected take, not the formerly hard-coded reference take.

## Preserve the action's camera identity

T7C1 camera resource: little-endian magic, frames, rate, source move ID, source track index, then 12 float values per frame (48 bytes). The observed original bank-4 source tracks are C8 with four channels, descriptors 11/7/15/21, millimeter positions, angular orientation, and horizontal FOV in radians. Confirm this layout before converting another asset family.

`EntranceCameraTrack.cs` converts these tracks into Unity. A previous bug loaded distinct tracks but replaced every azimuth with the same frontal 12-degree angle, retaining only distance. Another forced all new variants into full-body bounds. Both erase shot identity even when each resource hash is unique.

Preserve the actual lateral path, changing elevation, lens and shot scale. Derive close-up versus wide framing from the source position/FOV and subject, with safety bounds including the head. Source orientation/roll and target/framing must be adapted together; copying positions while globally overriding orientation is not a faithful camera port.

The current user prefers frontal views. The project reflects rearward depth into the front hemisphere, retains signed lateral travel, limits the profile angle, and bounds roll. These are adaptation choices: disclose them if asked for exact original-game matching. Different actions can legitimately have similar original paths; do not invent movement to make every take artificially unique.

Keep smoothing scoped to within-action discontinuities. Current example blends detected source edits over six frames before/nine after and eases composed angle/distance/FOV. Preserve deliberate between-player cuts unless the user requests otherwise. Tune against actual motion; these constants are not universal source-game settings.

Follow subject translation promptly. Smoothing the whole camera world position caused fast falling/turning characters to leave the view. Smooth relative viewing direction, zoom and FOV while checking framing and stage occlusion. A camera inside a pillar can pass the viewport head test and still render an unusable shot.

## Character-specific exception

Kazuya `kz_s03`, move 2326 in this installation, repeats source descent segments. Its verified presentation removes source frames 47..57 and 88..96, mapping presentation frame f to f+11 at f>=47 and a further +9 at f>=77. Body, secondary and camera all use the same map, plus the pose join blend. Do not attach this map to another fighter, another take or a newly discovered source revision based on a matching filename alone.

## Verification

- Check move/body/camera IDs, frame counts, finite samples and unique resource bindings.
- Before Play, run an offline consistency check per catalog entry:
  - JRT1 body frames = SMT1 frames = T7C1 frames;
  - JRT1 bone count = the asset's `boneNames` count (Xiaoyu: 19);
  - the camera magic is `T7C1`.

  Editor.log lines such as "Staging test playing Ling Xiaoyu entrance fu_s00" prove that takes cycle, not that they look right.
- Scan each take through the whole timeline and edited joins. Record visibility and camera position/angular/FOV changes; a passing JSON boolean without reviewing those values is insufficient.
- Capture actual rendered frames after at least several skinning updates, not a stale mesh immediately after setting bones. Inspect opening/middle/end and joins where the risk is localized.
- Exercise randomized startup, skip, fight, both winner paths and cleanup for changed flows. Use deterministic validation selection without leaking it into normal random play.
- Distinguish resource validity, visual framing and fidelity to a reference recording. None alone proves the other two.
