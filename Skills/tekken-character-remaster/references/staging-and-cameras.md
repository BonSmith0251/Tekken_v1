# Staging, cameras and gameplay flow

## Discover and bind the actions

Use original move records and properties, not guessed numeric ranges. Native `*_sNN` and `*_wNN` stems are useful entrance/victory candidates in this dataset; verify their meaning for the requested fighter. Some story/paired records alias a clip. Preserve the records but avoid double-weighting identical entrance aliases when choosing randomly. Compare body, secondary and camera references before deduplicating by animation name.

The inspector lists aliases and all secondary/camera events. Resolve missing tracks explicitly. In this session two paired victory variants had no standalone camera/face property and received a documented full-body fallback; do not claim those were original camera tracks.

`StagingCatalog.json` and `StagingCatalog.cs` currently connect character codes, move IDs, animation names, kinds, native frame lengths, camera resources, secondary resources and the specific landing edit flag. Generalize the manifest/exporter inputs before applying it to the full roster: `bake_staging_tracks.py` currently loops only Jin and Kazuya and assumes synchronized single events.

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
- Scan each take through the whole timeline and edited joins. Record visibility and camera position/angular/FOV changes; a passing JSON boolean without reviewing those values is insufficient.
- Capture actual rendered frames after at least several skinning updates, not a stale mesh immediately after setting bones. Inspect opening/middle/end and joins where the risk is localized.
- Exercise randomized startup, skip, fight, both winner paths and cleanup for changed flows. Use deterministic validation selection without leaking it into normal random play.
- Distinguish resource validity, visual framing and fidelity to a reference recording. None alone proves the other two.
