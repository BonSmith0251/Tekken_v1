# Character pipeline and conversion evidence

## Inventory and profile

Discover the character's actual moveset, model pieces, costumes and `rotsm.arc`; prefer extracted files and existing local decoders. Keep source hashes, move IDs, animation names, bank/index references, source frame counts and import settings in a per-character manifest. Cache original assets unchanged and write converted outputs under distinct character namespaces.

Where things are:

- **Fighting moves are not in the paks.** The paks hold about 1,500 UE AnimSequences (story, bowling, mob, item and stage clips). Fighter moves live in `tkdata.bin` mothead, already decrypted under `decrypted/tkdata/mothead/`: `bin/<chr>mot.bin`, `mask/<chr>rotsm.arc`, `movelist/<chr>.mvl`, and `nua/<chr>.vbn` (format unidentified).
- **Extracting moves:** `python -m t7extract moves <tkdata.bin|decrypted/tkdata> [chr...] -o OUT [--anims] [--decode-anims]` writes `moveset.json` (inputs as text, e.g. `d/f+1+2`), `movelist.json`, `attacks.csv`, raw `.anim` banks and decoded `.c8` samples.
- **Intro move:** alias 0 is the character's intro (Jin: `Jz_Direct`); its cancels are scripted transitions, which helps staging discovery.
- **Skeletal meshes:** they follow `*/<chr>_<piece>_1P/Meshes/SK_CH_<chr>_<piece>_1P.uasset`.
- **Skeleton asset:** record the fighter's own skeleton asset when one exists. Kazuya's `SKT_CH_kaz` defines `Head_Socket` with no rotation, while the shared `SKT_CH_base_Skeleton` uses yaw -90, so attachments resolved against the base skeleton came out turned 90°.

Record three identifiers per fighter, because they differ:

| Identifier | Xiaoyu | Jin | Kazuya | Alisa | Katarina |
|---|---|---|---|---|---|
| Catalog / character code | `xia` | `jin` | `kaz` | `asa` | `ltn` |
| Animation prefix | `fu_` | `jz_` | `kz_` | `aa_` | `ln_` |
| Versus catalog prefix | — | `Jz_` | `Kz_` | `Aa_` | `Ln_` |

Alisa's profile: character id 19, stance move 1456 `kmaa_fighting2(DVD)`, intro move 2374, 19 body bones, bind orientation within 0.00005° of the shared basis. Katarina's profile (`Tools/katarina-profile.json`): display name "Katarina Alves", stance 1419 `lnet_kamae(DVD)`, face piece `SK_CH_ltn_face` (not `_fac`), no `ATH_*_Btop` parts, skeleton on the shared body basis; 2,091 moves, 1,314 clips, 5 entrances and 3 victories, 4 outfits. Find a fighter's character id from a "character is" requirement unique to their moveset; Katarina has none, so her 21 is roster order only. Alisa's code was not obvious: count pak `Character/Item/Meshes/<code>` folders with `t7extract list`, then confirm `mothead/bin/<code>mot.bin` exists.

`MuseumCharacter.characterCode` is the reliable key. Display-name prefix tests break: "Ling Xiaoyu" never matched `StartsWith("Xiaoyu")`. `Tools/xiaoyu-profile.json` is the worked profile example. It records the mesh basis, the stance move (1428 `kmfu_kamae`), the staging move ranges, and the rule to reuse common-bank body clips only when their SHA256 and bind orientations match.

Current project modules to inspect before adding another fighter:

- `MuseumCharacter.cs`: model/costume references, body bone names and original move records.
- `FighterActionCatalog.cs`: currently chooses Jin/Kazuya stance IDs through prefix checks. Replace this with profile data for additional characters; do not fall through to the wrong fighter's stance.
- `FighterActionMotion.cs`: JRT1 body sampling, a 5-frame SmoothStep crossfade between moves, and presentation overrides. Combat hands load `Resources/SecondaryMotion/<code>_combat_hands` keyed on `characterCode`, otherwise they use the body clip's hands. Jin's and Kazuya's museum assets predate `characterCode`, so a display-name fallback remains for them.
- `JinRig.cs`: shared presentation class name does not prove skeleton compatibility. Its solver expects particular humanoid bone names and has a generic finger-curl fallback.
- `VersusGame.cs`: picks fighters per player (`enum Fighter { Kazuya, Jin, Xiaoyu, Alisa, Katarina }`, `playerOneFighter`/`playerTwoFighter`, code default Jin vs Xiaoyu; the saved scene has Alisa vs Katarina, but the character select screen overrides it at runtime; set the pair with `Temp/setup-localversus2`). It dresses them through `FightWardrobe.DressSaved` and builds `WardrobeDynamics`. Only `Spawn` keeps per-fighter branches. Adding a fighter to versus follows the `tekken-versus-battle` skill (command graph export, demo oracle, `Setup`/`Spawn`).
- `StagingTestPlayer.cs`: `Code()` must return `characterCode`, and it attaches character dynamics per take. `StagingValidation.cs` once sent every non-Jin entry to Kazuya; editor validators often assume two fighters.
- `CharacterMuseum.cs`, `CharacterMuseumSetup.cs`: preserve existing search, categories, scrubbing and manual transition navigation. The preset costume list has been replaced by the saved wardrobe items for characters with a wardrobe. The Staging category comes from the regex `^(jz|kz|fu|aa|ln)_[sw]\d{2}` in `CharacterMuseumSetup.cs`; add each new animation prefix there. The museum camera must frame the model's exhibit x/z, not the gallery origin. Inspect current secondary-motion support; do not assume versus support automatically applies in the museum.

## Onboarding recipe (proven on Xiaoyu, then Alisa in about 20 minutes)

1. Clone the previous fighter's top-level scripts in `UnityRemaster/` with renames. Derive them with an asserting string replace that aborts when a token is missing; that is how Katarina's scripts came from Alisa's. Drop Alisa-only parts (her detach-point table, `PrepareLocalVersus2`) when they don't apply. Also add the code to the `bake_staging_tracks.py` loop and the prefix to the Staging regex. The scripts:
   - `export_<name>.py`, with a hand-written `SETS` list of costume pieces;
   - `extract_<name>_motion.py`;
   - `prepare_<name>_materials.py`, `prepare_<name>_moveset.py` and `prepare_<name>_staging.py`.
2. Write outputs to `Assets/_Project/Characters/<Name>/{Source,Costumes,Wardrobe,Motion}`, never under an `Editor/` folder; Unity strips those from player builds.
3. Add the editor scripts under `Assets/_Project/Scripts/Editor`:
   - `<Name>Importer.cs`: a `Temp/import-<name>` flag runs Import, then `<Name>MuseumSetup.Finish`, then `PrepareLocalVersus2`.
   - `<Name>MuseumSetup.cs`: `BuildCharacter`, setting `characterCode` and `stanceMoveId`. It binds the staging.json takes and throws if body frames ≠ take frames.
   - `<Name>Capture.cs`: a `capture-<name>` flag writes preview renders to `Validation/<Name>/`.
4. Have each step write a report JSON (`Validation/<name>-model.json`, `-museum.json`, `-localversus2.json`).
5. **Edit scenes without touching the user's work.** `AlisaMuseumSetup.EditScene` opens each scene additively, edits it, and saves only if the user had no unsaved edits there; otherwise it only logs a warning. It added Alisa to the museum and `staging_test` this way, so prefer it to hand-editing `.unity` files.
6. Report coverage gaps plainly:
   - `<code>_combat_hands` exists only for jin and kaz;
   - Alisa has no Rage Art camera (Katarina has one);
   - a face capture can miss the face when the stance turns the head away (Katarina): pick another frame;
   - in the museum and `staging_test` only `xia` gets character dynamics (`CharacterMuseum.cs`, `StagingTestPlayer.cs`), so Alisa's hair is rigid there.
7. **Audits miss defects.** Alisa's first post-import audit reported no problems and the contact sheets looked right, yet the user found an opaque lip and a split neck. Stance renders don't reveal these.
8. **Wait loops.** A 30-minute DLL wait loop can expire while the user is in Play. When they return, re-check the DLL timestamp instead of trusting the failed loop.

## Body animation

The bundled `t7extract.motion` decodes 0x64 and 0xC8 samples. The 0x64 rotational triples are spherical axis/angle, not Euler angles. Use `motion.euler_samples()` or the decoder's C8 conversion before any Euler-to-quaternion conversion. Honor the archive's byte order.

`t7extract/motion.py` follows TKMovesets `Animations_T_x64.cpp` for 0x64: a base pose, independent 16-frame blocks, and run-length-coded acceleration residuals with `ANGLE_UNIT = 0.000095873802`. The older memory note that 0x64 is undecoded is obsolete; about 1,710 Jin clips decode.

Basis conventions differ by exporter, so never stack an unverified Unity permutation on top:

- **Wardrobe and museum JSON decoders:** Unity-space directly, with positions `(-x, z, y)/100` and rotations `(-qx, qz, qy, qw)`.
- **t7extract glTF exporter:** converts UE (left-handed, Z-up, cm) to glTF (right-handed, Y-up, m). Positions go `(x,y,z)→(x,z,y)`, quaternions go `(-x,-z,-y,w)`, tangent handedness flips, and index order is kept.

Use the existing reference-rig/body converter as a starting point, then establish the new skeleton's rest transforms, scale, heading, root axes and parent relationships. A permutation that fixes one fighter is not proof for another skeleton or for a camera. Generic humanoid retargeting must not silently discard source body motion, extra limbs or attachment channels.

JRT1 body resource layout in this project: magic, frame count, bone count, rate (four little-endian 32-bit fields), then each frame's root float3 and local quaternion float4 per mapped bone. Expected size is `16 + frames * (3 + bones*4) * 4`. Existing output is 60 Hz; validate source assumptions before resampling other data.

The JRT1 root is −x forward, +y left, z up, and the body heading is the hip (bone 2) yaw. Under `BODY_SCALE__group` the root travel lands 90° off the body. Any presentation that plays source root travel (versus entrance and victory) must remap `(dx, dy) → (−dy, −dx)`; without it, fighters looked like they were "walking slippery/in the air". The museum's "Source travel" toggle (`CharacterMuseum.cs`) and the `staging_test` player do not apply this remap and are suspected of the same 90° slide (unverified). Versus battle travel instead uses the exported clip `forward`/`lateral` along the body heading.

Combat body motion stays in place while staging may use source root travel. Apply any edited source-frame map consistently to root translation, rotations, secondary tracks, camera and completion time. Blend pose joins without replaying removed overlap frames.

## Finger and facial tracks

Observed move property IDs in the installed source:

| Event | Property | Character MOTA bank |
|---|---|---|
| Left hand | 0x842E | 1 |
| Right hand | 0x842F | 1 |
| Face | 0x84C2 | 2 |
| Camera | 0x8435 | 4 |

For the observed character-reference encoding, `(value & 0xC000) == 0x4000` and the track index is `value & 0x3FFF`. Other reference kinds need their own resolution. Property frame/start information matters; preserve all events, not just the last value per property. Existing exporters assume one synchronized track per kind for the verified staging takes. Audit that assumption for each new action. Only property frame values 0, 1 and 32769 (0x8001, seen on Xiaoyu) have been accepted as undelayed, and 32769 is not yet interpreted. Assert on any other value rather than ignoring a possible timeline offset.

Jin/Kazuya have both 15-channel compact hand clips and 19-channel hand clips. The compact layout has three joints for each of Thumb, Index, Middle, Ring and Pinky. The larger layout includes additional palm/base bones. Their facial layout has 90 triples including translation/rotation and tongue scale channels. Treat these as verified examples; match names/order from skeleton and source helpers before using them for another character.

Existing examples:

- `Tools/SecondaryMotion/bake_combat_hands.py`: converts native stance hands, with different left/right palm bases and a palm-direction regression check.
- `bake_entrance_tracks.py`, `bake_staging_tracks.py`: 19-channel staging hands and facial samples. `hand_rotation()` mirrors the palm basis per side, left `quat(-x, z, y, w)` and right `quat(x, -z, y, w)`, because the two extracted bind skeletons have opposite palm normals. Using the right-hand reflection for both sent the left middle joints through the back of the hand. `bake_staging_tracks.py` asserts on backward curl and on thumb offsets over 80°. `prepare_xiaoyu_staging.py` imports the same function; reuse it for new fighters.
- `../AnimationResearch/Direct_TekkenAnimHelper.py`: source helper channel order and per-side conventions.
- `SecondaryMotionTrack.cs`: SMT1 parser and interpolation.

Do not apply the compact hand conversion indiscriminately to the 19-channel bank. Derive each joint's expected active rotation in its actual bind basis. Verify thumb opposition separately; simply inverting all left-hand axes can fix curl while sending the thumb through the glove.

SMT1: little-endian magic, frames, bone count, rate; then length-prefixed UTF-8 bone names; then frame-major per-bone float3 position delta, quaternion float4 rotation delta, float3 scale. Current reader expects `frames * bones * 40` bytes after the name table. Cache bind positions/rotations/scales before any pose is applied. Runtime pose is bind position plus delta, bind rotation times delta, and componentwise bind scale times scale.

Restore bind transforms on completion, skip and rematch. Apply secondary tracks after the body/procedural fallback each frame. Bind native combat hands by profile, retaining action-specific hand events when present. The current stance-hand fallback is not complete per-attack finger animation.

## Skinning and visual checks

Restored animation is ineffective if mesh influences have been collapsed onto parent body bones. Check both hand and face vertices against the full bone table, normalized weights and bind poses for each costume. Kazuya required weight restoration; that is a prior defect, not a transformation to apply to every import.

The decoders keep the four strongest normalized influences, and so does `t7extract export` by default (`--influences 4`). Some source garments store eight. Jin's jacket `SK_CH_jin_bdu_1P` has 170 vertices using more than four bones, while faces store four. If shoulders or the waist deform badly, measure influence counts before blaming the rig.

Match bone names case-insensitively, as Unreal does. Pieces mix `Base`/`BASE` and `Head`/`HEAD`, and the museum, validators and capture tools all need the same rule. `transform\d+` helpers are piece-local (hair and feathers both have `transform3`), so give each piece its own copy.

**Re-import trap.** When a mesh asset already exists, updating it with `EditorUtility.CopySerialized(newMesh, existing)` refreshes the bounds but keeps the old vertex buffer, so corrected geometry silently never reaches Unity. Kazuya's glasses stayed rotated after the export was fixed. `JinCostumeImporter.cs` now rebuilds in place (`Clear()`, then assign and `SetDirty`). `JinImporter.cs`, `KazuyaImporter.cs` and `XiaoyuImporter.cs` still use `CopySerialized`; `AlisaImporter.cs` rebuilds in place (and also welds triangle soup). Fix the one you touch, or audit by decoding `_typelessdata` from the `.asset` and comparing it with the source JSON.

Verify left/right hands at neutral, curled, open and moving frames, including thumb position and glove intersections. Mouth checks should distinguish lips, jaw, tongue and any blendshape/attachment mechanism. Missing channel layouts or costume rig variants should be reported specifically, not replaced with a permanently folded hand or arbitrary mouth motion.

## Mesh topology and seams

Importers call `RecalculateNormals`, which averages only across shared vertex indices. Some exported pieces are triangle soup: Xiaoyu's `xia_bdu_1p` skin sections have exactly 3 vertices per triangle (arm skin 3,510 vertices, 622 positions). That shading is flat and faceted, and the arms look pale once the material has any sheen. Measure vertices per triangle for every submesh; welded pieces sit around 0.55–0.8. `XiaoyuImporter.WeldTriangleSoup` shares corners with identical position, UV and skin weights, only in submeshes where no index repeats. Hair and feather cards repeat positions on purpose for double-sided geometry; never weld those.

Seams between mesh pieces (face/upper at the neck, upper/lower at the waist or thigh) should have identical weights on coincident vertices. Measure this before suspecting tearing. Xiaoyu's seams match in every costume.

**Detach-point bones (`ATH_*_Btop`).** Some rigs (Alisa) have them: points where body parts detach. In the source skeleton they can hang from the wrong parent for the skin they drive.

- **Alisa's elbow bone.** `ATH_L/R_Arm_Btm_Btop` sits at the elbow but is parented to the forearm, and on every costume's forearm piece (`exp_*_parmL/R`) it skins the lower 40% of the upper arm. The upper arm therefore bowed 4–7.5 cm with each elbow bend, which the user reported as "the upper arm of alisa is not straight".
- **The fix.** `AlisaImporter.DetachPoints` re-hangs each detach bone under the limb its skin belongs to: Arm_Top, Arm_Mid and Arm_Btm under `L/R_Arm`, plus the neck, head and waist ones. After it, the upper arm stayed rigid within 0.5 cm from the shoulder to 80% of the way to the elbow.
- **Check all of them.** For any rig with `ATH_*` bones, measure which vertices every one of them drives in every costume piece and compare that with its parent. Don't stop at the bones on seams: the seam fix had already covered neck, head and waist and still missed the elbow.
- **Robot parts (Alisa-specific).** Her `exp_*` parts (chainsaws, wings, boosters, beamchainsaw) appear only in certain moves and are not imported. Because the re-hang keeps parts attached, moves that truly detach her head or arms would need those bones driven separately.
- **One table for all costumes is a compromise.** Dropping `ATH_Hip_Btop`→Spine1 reopened the Battle costume's waist (2.07 cm), so it stayed, at the cost of 0.87 cm under Default 1P's skirt. Re-parent only under uniformly scaled targets (Head 1.15, Spine1 0.92) so the bind pose survives.
- **Bend-only defects.** This kind of defect appears only when a joint bends. The bind pose looks fine, uniform helper scales, four influences and in-place mesh rebuilds all passed, so measure deviation from a rigid limb under a bent pose.

## Runtime cloth and helper bones

Tekken rigs shape the body with non-uniformly scaled helper bones. Xiaoyu has `Neck__offset` (1, 1.344, 1.459), collarbone, pectoral and `HLP_*_UpLeg_*`, butt and groin helpers at (1, 0.881, 0.881). Unity's `Cloth` re-skins every vertex of its renderer on the CPU and does not support that scale, so any skin sharing a renderer with simulated fabric balloons (her neck in Default/Classic/School and thighs in Formal/School). `XiaoyuSecondaryDynamics.SeparateGarment` moves only the simulated sections onto a separate renderer before adding `Cloth`. Keep that split when adding cloth to any fighter. Check renderer contents per costume for skin materials (`*_skin_*`) next to fabric sections.

Validation that disables cloth for its "skinned" captures (`XiaoyuDynamicsValidation`) cannot catch this. Wide museum frames also hide it; inspect close-ups of the neck and thighs in a crouching stance with dynamics running.

These were not the cause of the bulging neck and thighs, and the user kept reporting "bulging" after each attempt:

- procedural `JointCorrection` driving of `Neck__offset` and the `HLP_*_Knee`, `UpLeg_Top`, `Butt` and `Inguinal` helpers;
- smoothing skin normals across seams;
- blaming helpers that were never animated.

Check the renderer split first. Codex's `UnityRemaster/CategoryBuild` copy still carries the `JointCorrection` code.

**Other cloth rules:**

- **Rest poses differ per mesh.** A helper bone with the same name can rest differently in two costume pieces. Mixed pieces get piece-local helper copies with animated bones shared (`MuseumWardrobe`). Dress the model before posing it.
- **Dress before building dynamics.** The museum dresses inside `LoadModel` before `XiaoyuSecondaryDynamics` runs; otherwise `SeparateGarment` and the springs act on the wrong renderers. Cleanup outside Play mode must use `DestroyImmediate`.
- **Cloth particles are created lazily.** `cloth.coefficients` is empty at `AddComponent` and in edit-mode previews, so apply max distances once it is populated.
- **Unity welds particles.** Map them to vertices by quantized position.
- **Flared garments need room to drop.** A garment modelled flared at bind (Xiaoyu's Formal skirt) needs a max distance of at least its outward offset from the body. A static capture shows it as a flat disc, which is expected.
- **Skirts distort even with cloth off.** Hem vertices weighted to thigh and hip helpers flare sideways. On a runtime mesh copy, blend those weights toward a "Skirt suspension" bone that follows the pelvis position and the `Base` rotation.
- **Pin waistband panels by geometry.** Panels that share the skirt material are pinned when their minimum y is above about 1 m.
- **Check at 60 fps.** Test after settling, during attacks and across timeline seeks. Reset on seek, not on loop wrap.
- **Feathers need cutout** like hair and eyelashes; opaque feather cards showed jagged dark patches.
