# Original stages and the env_scene selector

Added 2026-10-26. **The full, primary guide is the Claude skill `tekken-stage-environments`** (`~/.claude/skills/tekken-stage-environments`: SKILL.md, `references/pipeline.md` with the STG2/stages.json formats and the exact material-choice order, and read-only diagnosis scripts such as `stage_near.py`, `stage_materials.py` and `color_slot_check.py`). Read it for stage work; this file is a summary kept for Codex, and both must be updated together. `Tools/MUSEUM_STAGES.txt` in the Unity project is the canonical user doc. Re-check the "Known broken" list against the current `Validation/Stages` contact sheets (`contact-env-1..3.png`) before relying on it.

## Pipeline

1. **Export.** `Tools/import_museum_stages.py` writes STG2 meshes (normals, tangents, UVs, material sections) plus base, normal and emissive PNGs into `Assets/_Project/Stages/Textures`, and `Stages/Source/{stages.json,report.json}`.
   - **Sublevels.** It reads each stage's `_reload` and `_demo` sublevels as well. They hold stg01's breakable floors (DestructibleMesh), stg08a's ring, stg04's deck, the helicopter in stg12/12a, stg19's blocks and stg28's crowd. Destructible and skeletal meshes are drawn in rest pose.
   - **Size.** A full run takes about 20 minutes and skips existing PNGs. On 2026-10-26: 34 stages (stg00–32 plus 08a, 12a and 90; there is no stg17 or stg25), 4,044 unique meshes, 2,924 materials of which 2,909 are textured, and 3,949 textures.
   - **Report.** `report.json` lists `untexturedMaterials` (11) with a reason for each. 4 textures failed (the stg11 movie texture and 3 `violet_*_fix` textures that aren't 2D), and 2 meshes (engine Cube in stg10, Cylinder in stg12).
2. **Import.** The `Temp/import-stages` flag runs `MuseumStageImporter`. It makes one HDRP/Lit material per material instance in `Stages/Materials`, plus `Resources/MuseumStages/<stg>.prefab` and the `StageNames` list.
3. **Scene and captures.** `Temp/create-env-scene` (`EnvironmentSceneSetup`, editing additively) builds `Scenes/env_scene.unity`, which is in Build Settings. `Temp/capture-stages` writes contact sheets to `Validation/Stages/`; pass stage names in the flag to capture only those.
   - **Before/after folders.** `before-winding-fix`, `before-layer-fix`, `before-stg15-fix` and `before-sublevels` keep earlier runs.
   - **Memory.** A full 34-stage capture once crashed Unity with d3d11 `E_OUTOFMEMORY` at stg30. `EnvironmentSceneSetup` now calls `UnloadUnusedAssetsImmediate` and `GC.Collect` after each shot. After a crash, recapture only the missing stages.

Commit the stage textures as their own commit (about 3 GB); see the git notes in project-and-rendering.md.

## Material rules

- **Helper geometry is never drawn.** Collision, NoPixelDraw, lighting and bounce materials, and `bHiddenInGame` / `bCastHiddenShadow` geometry all map to `Hidden.mat` and are skipped.
- **Blueprint effect planes are skipped too.** FogSheet planes (8192× scale, about 80 km, no material) and `LightBeam/Meshes/SM_GodRay*` planes are skipped in `MuseumStageImporter`. FogSheets caused stg15's grey slab and five planes in stg30.
- **Always take the primary layer.** `layer_rank` picks it in layered materials: BaseColor_A over _B, Diffuse over Moss, Mat1 over Mat2. This corrected 102 layered materials across all stages, not only stg30/31.
- **Opacity.** Fog, mist and cloud cards need `_op` opacity maps, built from brightness × mask, or they render as grey boxes. They are drawn at half alpha. Sky domes are self-lit; stg31's sky had been lit by the sun.
- **SpeedTree naming.** Maps are `X_Tex`, `X_Normal_Tex` and `X_Spec_Tex`, and `suffix_role` strips `_Tex`. Before that, normal maps were used as colour on 15 materials (cherry trees in stg09/15/23, one stg18 wire). This was confirmed in the material data, not on screen.
- **Packed masks in a colour slot.** stg01's "blue panels" were ivy whose colour slot held a packed mask; it now uses its albedo and normal maps. Check odd flat colours for this.
- **Unrecoverable sources.** `WorldGridMaterial` (on 9 stages), engine Cube/Cylinder primitives, GLOWMASK moons (stg00/13/29), stg11's live screen and stg07's heat haze aren't in the paks or can't be converted. stg90 shows 489 of 503 submeshes textured.
- **Normal maps.** PNGs imported before `StageTexturePostprocessor` compiled came in as colour textures; the postprocessor fixes their type. Adding any `AssetPostprocessor` reimports every texture in the project, which takes a long time.

## Geometry and placement

- **Winding.** Unreal index order is reversed relative to Unity: 99.7% of triangles faced away from their normals, which the user saw as "transparent" stages. The importer now reverses every triangle.
- **Placement.** Each stage is placed on its first-floor `TekkenPlayerStart*_GAME_START` marker (`StageInfo`). stg07 has no such marker, so it sits at the map origin: the fighters stand on uneven lava rock and Jin's feet sink in.
- **Turning.** `StageEnvironment.Spawn` turns the stage by -(yaw+90), because yaw -90 already looks along +Z, the versus camera's direction. The user approved using the GAME_START direction.
- **Floor.** A raycast finds the floor under the marker. Glass and water count as floor, because stg32 fights on a glass pool.

## Selection and versus

- **`env_scene`.** It spawns Alisa and Jin, dressed with `FightWardrobe.DressSaved` and in their stance loop. The user ticks stages and saves them to `Resources/EnvironmentSelection.asset`; a player build saves to `persistentDataPath/environment-selection.json`. The museum's old STAGE control was removed; selection lives only here, as the user asked.
- **The selection is user data.** Never reset it. On 2026-10-26 the user re-saved it with 31 stages, adding back stg15/30/31 after their fixes (implied approval). stg01, stg08a and stg22 stay unticked, even though stg01 and stg08a were fixed after that save.
- **Versus.** `VersusGame.BuildArena` calls `EnvironmentSelection.Load().PickForMatch(random)`. With nothing usable saved, or only "arena", it builds the generated arena. The far clip is 2000 with a stage.
- **Gameplay bounds.** Movement bounds are still the generated arena's rectangle (`arenaHalfWidth/Depth`, the victory fit). Stage walls have no collision. Unverified in Play.

## Lighting gap

Since 2026-10-26 the stage scenes convert each stage's original lights, sky, fog and post-processing, and can bake lightmaps; see the Claude stage skill. The user chose to keep the 4× arena density and bake stages selectively: never batch-bake or lower the density unasked. Open-stage flags: `Temp/retune-open-stage`, `capture-open-stage`, `add-stage-navigator`; `save-first`/`save-open` only after the user confirms. Versus and env_scene still spawn the unbaked prefabs lit by the scene's "Render Settings" object (env_scene has its own copy, yawed by `VersusYaw`). Not converted: shader graphs, water, panners and fog animation. Water and fog stages (stg20, stg22, stg29) look bare, and the 12 m shadow distance hasn't been tested against stages much larger than the arena.

## Gotchas

- **Preview renders.** When one HDRP `PreviewRenderUtility` was reused for several renders, every render after the first came out white (exposure carried over). Use one preview per shot.
- **Edit-mode cleanup.** `FighterView` must use `DestroyImmediate` outside Play mode; `Destroy` throws in edit-mode captures.

## Status (2026-10-26)

- **Fixed (seen in preview captures only):**
  - stg30/31 mismatched textures, and their grey billboards (stg31's pagoda now shows through the waterfall mist; stg30's mist is faint);
  - stg15's grey backdrop (it was the FogSheet planes);
  - stg01's blue ivy and missing floors;
  - stg08a's missing ring.
- **Open:**
  - stg28's fog cards show their cut-out mask shapes against the sky; fixing that needs the unconverted fog animation;
  - stg07 has no start marker (see Placement);
  - the unrecoverable materials above.
- **Not yet checked by the user in Play:** versus loading stages, stage lighting and shadows, bounds against walls, and stg32's close framing. The user has not commented on any capture.

## Stage effects and light shafts (2026-10-26, stg01 "missing parts" request)

- **Attachment parents.** Every `*Component` is now a transform node, so meshes attached to
  capsules and billboards are placed (97 meshes: stg02/26 waterfall-group rocks, waterfalls and
  vine bridge, god rays). The exporter also reads the `_effect` sublevel.
- **Particles.** `Tools/StageEffects/export_stage_effects.py` decodes the 101 systems the stages
  place (539 placements) with the HitEffects Cascade decoder, straight from the paks, into
  `Resources/StageEffects`. `StageEffectMarker`s in the prefabs; `StageEnvironment.StartEffects`
  plays the looping ones (one-shot break bursts stay off). Fire instances inherit their parent's
  flame noise × shape mask (baked, mask turned tip-up: assumed); soft particles at 30% opacity;
  stacked ColorScaleOverLife modules now multiply in the shared decoder (battle effects keep their
  old export until re-run).
- **Light shafts.** `BP_LightBeam` god rays (stg01 12, stg30 25, stg31 1) are crossed quads with
  `Resources/StageEffects/LightShaft.shader` (additive, camera fade over `fadedistance`), at 40%
  brightness set by eye.
- **Not missing:** stg01's 134 `BP_WallBreak` meshes are `M_Collision` hit volumes, and
  `stg01_light` holds only bake helpers.
- **stg02/26 mist:** `PS_EF_STG02_Mist` is HDR red-orange in the data; at the user's request it is tinted pale green-white (`TINT_OVERRIDES` in the effects exporter), same opacity and fade.

## Original stage lighting and foliage wind (2026-10-26)

- Each stage now brings its own lights, sky light, height fog and post process (exported from
  `_light`/`_lightstatic`, blueprint lights resolved through their template), built by
  `StageLightingBuilder` into real HDRP lights and `Stages/Lighting/<stg>_Lighting.asset`.
  `StageLighting` swaps out the Render Settings rig while a stage is shown; the arena keeps the rig.
- By-eye limits: baked-only lights ×0.25 ≤ 2000 lm (nearest 96), together ≤ 6 lux at the fighters
  (fixed stg18's white-out; also dims stg13/90/19/10/00), suns > 20 lux replaced by 8 (stg18 has 200),
  sky intensity ≤ 1.5, sky reflection 35%. Lightmaps/reflection captures/light functions/flicker
  not converted.
- `StageWind` sways foliage on 16 stages (stg01 ivy 3–4 cm; windy stages ≤ ~20 cm, strength capped
  at 0.5); `Temp/verify-stage-wind` measures it.

## Stage scenes (2026-10-26, for editing and lightmap baking)

`Scenes/Stages/<stg>.unity` (34, built by `StageSceneSetup`, flag `Temp/create-stage-scenes`): a linked
prefab instance on the match start, the stage volume and static lighting sky, all originally baked lights
as Unity Baked lights (`StageBakedLight` keeps them in the prefab, switched off at runtime beyond the 96
stand-ins), suns Mixed, light/reflection probes, a camera and LightingSettings (2 texels/m). Bake with only
that scene open (`Temp/bake-stage-scene` refuses otherwise). Versus/env_scene still use the prefabs. stg01 was test-baked
2026-10-26 (49 s, one 2048 map, 14 MB): sky occlusion and lamp light baked, sun bounce weak. Retuned and rebaked the same day
(indirect ×2.5, albedo 1.5, 4 bounces, sun indirect ×2, 4× texels within 30 m): 9 min, four 2048 maps, 280 MB;
bounce now lifts shadows and walls. At that density all 34 stages won't fit on D: (11 GB free).

Stage scenes have a fly navigator (`StageNavigator`, "Fly Navigator": free-fly camera in Play, WASD/Q/E, Shift/Ctrl, right-drag look, scroll speed, R start view, no collision). The user had the third-person fighter removed (2026-10-26).
