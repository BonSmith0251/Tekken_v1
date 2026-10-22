# Character pipeline and conversion evidence

## Inventory and profile

Discover the character's actual moveset, model pieces, costumes and `rotsm.arc`; prefer extracted files and existing local decoders. Keep source hashes, move IDs, animation names, bank/index references, source frame counts and import settings in a per-character manifest. Cache original assets unchanged and write converted outputs under distinct character namespaces.

Current project modules to inspect before adding another fighter:

- `MuseumCharacter.cs`: model/costume references, body bone names and original move records.
- `FighterActionCatalog.cs`: currently chooses Jin/Kazuya stance IDs through prefix checks. Replace this with profile data for additional characters; do not fall through to the wrong fighter's stance.
- `FighterActionMotion.cs`: JRT1 body sampling and presentation overrides. Its combat-hand resource selection currently uses a Jin-versus-Kazuya name test; generalize that binding before adding anyone else.
- `JinRig.cs`: shared presentation class name does not prove skeleton compatibility. Its solver expects particular humanoid bone names and has a generic finger-curl fallback.
- `CharacterMuseum.cs`, `CharacterMuseumSetup.cs`: preserve existing search, categories, costumes, scrubbing and manual transition navigation. Inspect current secondary-motion support; do not assume versus support automatically applies in the museum.

## Body animation

The bundled `t7extract.motion` decodes 0x64 and 0xC8 samples. The 0x64 rotational triples are spherical axis/angle, not Euler angles. Use `motion.euler_samples()` or the decoder's C8 conversion before any Euler-to-quaternion conversion. Honor the archive's byte order.

Use the existing reference-rig/body converter as a starting point, then establish the new skeleton's rest transforms, scale, heading, root axes and parent relationships. A permutation that fixes one fighter is not proof for another skeleton or for a camera. Generic humanoid retargeting must not silently discard source body motion, extra limbs or attachment channels.

JRT1 body resource layout in this project: magic, frame count, bone count, rate (four little-endian 32-bit fields), then each frame's root float3 and local quaternion float4 per mapped bone. Expected size is `16 + frames * (3 + bones*4) * 4`. Existing output is 60 Hz; validate source assumptions before resampling other data.

Combat body motion stays in place while staging may use source root travel. Apply any edited source-frame map consistently to root translation, rotations, secondary tracks, camera and completion time. Blend pose joins without replaying removed overlap frames.

## Finger and facial tracks

Observed move property IDs in the installed source:

| Event | Property | Character MOTA bank |
|---|---|---|
| Left hand | 0x842E | 1 |
| Right hand | 0x842F | 1 |
| Face | 0x84C2 | 2 |
| Camera | 0x8435 | 4 |

For the observed character-reference encoding, `(value & 0xC000) == 0x4000` and the track index is `value & 0x3FFF`. Other reference kinds need their own resolution. Property frame/start information matters; preserve all events, not just the last value per property. Existing exporters assume one synchronized track per kind for the verified staging takes. Audit that assumption for each new action.

Jin/Kazuya have both 15-channel compact hand clips and 19-channel hand clips. The compact layout has three joints for each of Thumb, Index, Middle, Ring and Pinky. The larger layout includes additional palm/base bones. Their facial layout has 90 triples including translation/rotation and tongue scale channels. Treat these as verified examples; match names/order from skeleton and source helpers before using them for another character.

Existing examples:

- `Tools/SecondaryMotion/bake_combat_hands.py`: converts native stance hands, with different left/right palm bases and a palm-direction regression check.
- `bake_entrance_tracks.py`, `bake_staging_tracks.py`: 19-channel staging hands and facial samples, with their own tested conversion convention.
- `../AnimationResearch/Direct_TekkenAnimHelper.py`: source helper channel order and per-side conventions.
- `SecondaryMotionTrack.cs`: SMT1 parser and interpolation.

Do not apply the compact hand conversion indiscriminately to the 19-channel bank. Derive each joint's expected active rotation in its actual bind basis. Verify thumb opposition separately; simply inverting all left-hand axes can fix curl while sending the thumb through the glove.

SMT1: little-endian magic, frames, bone count, rate; then length-prefixed UTF-8 bone names; then frame-major per-bone float3 position delta, quaternion float4 rotation delta, float3 scale. Current reader expects `frames * bones * 40` bytes after the name table. Cache bind positions/rotations/scales before any pose is applied. Runtime pose is bind position plus delta, bind rotation times delta, and componentwise bind scale times scale.

Restore bind transforms on completion, skip and rematch. Apply secondary tracks after the body/procedural fallback each frame. Bind native combat hands by profile, retaining action-specific hand events when present. The current stance-hand fallback is not complete per-attack finger animation.

## Skinning and visual checks

Restored animation is ineffective if mesh influences have been collapsed onto parent body bones. Check both hand and face vertices against the full bone table, normalized weights and bind poses for each costume. Kazuya required weight restoration; that is a prior defect, not a transformation to apply to every import.

Verify left/right hands at neutral, curled, open and moving frames, including thumb position and glove intersections. Mouth checks should distinguish lips, jaw, tongue and any blendshape/attachment mechanism. Missing channel layouts or costume rig variants should be reported specifically, not replaced with a permanently folded hand or arbitrary mouth motion.
