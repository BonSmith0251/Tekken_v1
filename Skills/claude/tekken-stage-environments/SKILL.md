---
name: tekken-stage-environments
description: Work on the UnityRemaster's original Tekken 7 stages (stg00–stg32, stg08a/12a/90) and the env_scene stage selector - exporting stage geometry, textures and materials from the game, importing them as HDRP/Lit stage prefabs, placing and turning them on the original match start, choosing and saving which stages versus uses, and rendering stage captures. Use this whenever the user says a stage looks wrong ("stg15 has a grey wall", "textures don't match", "they float in the air", "the stage looks transparent", "fix stg08a"), asks to add, re-export, re-texture or fix a stage, a floor, a material, fog, sky or water, asks about env_scene or the saved environment selection, or wants stages in versus, even when they only name a stage number or paste a screenshot.
---

# Tekken stage environments

The 34 original stages in the Unity remaster at `UnityRemaster/TekkenVersusRemaster`
(workspace `D:\games\Tekken.7.Ultimate.Edition.v5.10`; Unity 2023.2.22f1, HDRP 16). Confirm paths;
they are hints. The user-facing doc is `Tools/MUSEUM_STAGES.txt` in the project. The Codex
character skill keeps a status copy at `~/.codex/skills/tekken-character-remaster/references/stages-and-environments.md`;
when you change behaviour, update both.

The user keeps Unity open and reports issues **one stage at a time**, usually from a capture
("check stage30 and 31, some textures don't match"). Find the real cause in the data, fix the
general rule (not the one stage), measure how many materials or meshes the rule changes across
all stages, re-export, reimport, recapture, and show before/after.

## Map

| Piece | Path (under the project) |
|---|---|
| Exporter (game paks → STG2 meshes, PNG textures, catalog, report) | `Tools/import_museum_stages.py` (~20 min full run; `python import_museum_stages.py stg01 stg08a` limits it, but **rewrites stages.json with only those stages**) |
| Exporter output | `Assets/_Project/Stages/Source/{<hash>.bytes, stages.json, report.json}`, `Stages/Textures/*.png` (~3,950 files, ~3 GB) |
| Importer (flag `Temp/import-stages`) | `Scripts/Editor/MuseumStageImporter.cs` → `Stages/Materials/*.mat`, `Resources/MuseumStages/<stg>.prefab`, `Meshes/`, `StageNames.txt`, `Validation/Stages/import.json` |
| Normal-map import rule | `StageTexturePostprocessor` (same file); `_nrm.png` = NormalMap |
| Spawning + placement | `Scripts/Runtime/StageEnvironment.cs` (`Spawn`, `LastAlignment`, `StartEffects`), `StageInfo.cs` (match start, yaw) |
| Stage particle systems | `Tools/StageEffects/export_stage_effects.py` (reuses `Tools/HitEffects/export_hit_effects.py`'s Cascade decoder, reads the paks) → `Resources/StageEffects/{effects.json,report.json,Textures,Meshes}`; `StageEffectMarker` on effect nodes; played by `CascadeEffects.Create(parent, "StageEffects")` |
| Original lighting | Exporter `light_info`/`sky_info`/`fog_info`/`post_volume`/`archetype` (reads `_light`, `_lightstatic`; blueprint defaults from the template package) → node `light`/`sky`/`fog`/`wind`, stage `post`, `Stages/Lighting/<cube>.t7sky`; `Scripts/Editor/StageLightingBuilder.cs` (lights, `<stg>_Lighting.asset` profile) → `Scripts/Runtime/StageLighting.cs` (Activate/Deactivate, swaps out the Render Settings rig) |
| Foliage wind | `Scripts/Runtime/StageWind.cs` (CPU vertex sway, hanging vs rooted), added by `StageLightingBuilder.Finish`; flag `Temp/verify-stage-wind` → `Validation/Stages/wind.json` |
| Light shafts | `BP_LightBeam` god rays: exporter `god_ray()` reads the MaterialInstanceDynamic; importer `AddBeam` builds crossed quads with `Resources/StageEffects/LightShaft.shader` (additive, camera fade over `fadedistance`, edge-on fade; `BeamGain` 0.4) |
| Saved selection | `Scripts/Runtime/EnvironmentSelection.cs` → `Resources/EnvironmentSelection.asset` (player build: `persistentDataPath/environment-selection.json`) |
| Selector scene | `Scenes/env_scene.unity`, `Scripts/Runtime/EnvironmentScene.cs` (Alisa + Jin in stance, tick + Save) |
| Scene build, captures, selection test | `Scripts/Editor/EnvironmentSceneSetup.cs`: flags `Temp/create-env-scene`, `Temp/capture-stages` (stage names one per line, `arena` optional), `Temp/verify-env-selection` |
| Versus use | `VersusGame.BuildArena` → `EnvironmentSelection.Load().PickForMatch(random)`; `StageName` property; generated arena when nothing usable is saved |
| Stage scenes (editing, lightmap baking) | `Scripts/Editor/StageSceneSetup.cs`: flags `Temp/create-stage-scenes` (names; `force` rebuilds), `Temp/bake-stage-scene` (only with that one scene open) → `Scenes/Stages/<stg>.unity` + `<stg>/{Volume.asset,Lighting.lighting}`; `StageScene` root starts effects in Play; `Validation/Stages/scenes.json` |
| Captures | `Validation/Stages/env_scene-<stg>.png` (fighters), `<stg>.png` (wide, no fighters), `view-<stg>-<label>.png` (flag line `stg01 view <yaw> <pitch> <dist> <label>`; effects are simulated 3 s first), `capture.json` (renderers, textured submeshes, `alignment`), `before-*` folders, `contact-*.png` |

Formats (STG2, stages.json fields, material entry fields) and the exact material rules are
in `references/pipeline.md`. Read it before changing the exporter or importer.

## Tools bundled with this skill

All read-only; run with `PYTHONIOENCODING=utf-8` (stdout is cp1252 otherwise).

- `scripts/stage_materials.py <stg> [--suspect]` – every material a stage draws with its colour
  map and how it was chosen. `<<` marks guessed sources; `--suspect` keeps guessed, untextured,
  transparent and overlay-picked ones and prints their parameters.
- `scripts/stage_near.py <stg> <x> <z> [--maxheight 3]` – meshes whose world bounds cover a point
  (Unity metres, map space; the stage's `start` is printed first). Finds "that grey slab" or
  "the floor under the fighters". Level L0 = `_geom`, L1 = `_reload`, L2 = `_demo`.
- `scripts/stage_sublevels.py <stg> [--map stgNN_x --near X Y Z]` – component classes in every
  `stgNN_*.umap` and meshes near a UE point (cm, Z up). Use when something in the game is missing.
- `scripts/color_slot_check.py [stg]` – colour slots holding a packed mask, a normal map, or a
  guessed flat image.
- `scripts/contact_sheet.py [--wide | --compare <before-folder> stg..]` – contact sheets and
  before/after strips.

The versus skill's `harness/compile_check.sh` compiles both assemblies headlessly; run it after
every C# edit.

## Workflow for a reported stage problem

1. **Look first.** Capture the stage (write its name to `Temp/capture-stages`) and read both
   images. Copy the current captures to `Validation/Stages/before-<fix>/` before fixing.
2. **Identify the object.** `stage_near.py` at the spot, `stage_materials.py --suspect`,
   `color_slot_check.py`. For a missing object, `stage_sublevels.py`. Typical causes, in the
   order they have turned up:
   - see-through faces → winding (fixed in the importer; check it was not undone);
   - floating fighters / bare frame → the floor is in a sublevel or is a DestructibleMesh;
   - grey slab / grey cards → blueprint effect planes with no material (FogSheet, GodRay), or
     a fog card without opacity;
   - wrong texture → overlay layer picked (moss, `_B`, Mat2), SpeedTree `_Normal_Tex`, or a
     packed mask in a `_c` slot (ppeel ivy uses `_a` for albedo);
   - stage facing the wrong way / camera blocked → GAME_START yaw or the floor raycast;
   - something at the map origin or "floating" → its parent is a non-mesh component (capsule,
     billboard); every `*Component` is now a transform node, check it was not narrowed;
   - "missing" fire, sand, dust, light shafts → particle systems and god rays, not meshes; check
     `Resources/StageEffects/report.json` and the decoder's limitations;
   - a part that exists in no map you read → `stage_sublevels.py` (stg01 floors in `_reload`,
     sand streams in `_effect`). Collision volumes (`BP_WallBreak`, `M_Collision`) are correctly skipped.
3. **Fix the rule** in `import_museum_stages.py` (data choice) or `MuseumStageImporter.cs`
   (Unity build, skips), never a per-stage hack unless the data has no general signal.
4. **Measure the blast radius** before the full run: export the affected stages into a scratch
   folder (`m.OUT=Path(scratch)` from Python) and diff material choices against the current
   `stages.json`. Report the count to the user ("15 materials changed: cherry trees in stg09/15/23").
   A one-material data fix can be patched into `stages.json` in place by re-running
   `Materials.describe` for just those entries, instead of a 20-minute export.
5. **Run it.** Full export in the background → `Temp/refresh-assets` → wait until
   `Library/ScriptAssemblies/Assembly-CSharp-Editor.dll` is newer than the edited script →
   `Temp/import-stages` → wait for `Validation/Stages/import.json` → `Temp/capture-stages`.
6. **Show before/after** (`contact_sheet.py --compare`) and say what is still wrong.

## Decisions already made (keep them unless the user says otherwise)

- Environment selection lives only in `env_scene`; the museum STAGE control was removed at the
  user's request. `env_scene` spawns Alisa and Jin by default.
- **The saved selection is user data. Never reset or overwrite it.** `Temp/verify-env-selection`
  saves a test choice and restores the original; the user re-saved 31 stages on 2026-10-26,
  adding back stg15/30/31 after their fixes (implied approval) and leaving out stg01, stg08a and
  stg22. stg01 and stg08a were fixed after that save; don't tick them for the user.
- Stages turn by −(GAME_START yaw + 90): yaw −90 already looks along +Z, the versus camera's view
  (user approved). Placement uses the first-floor `TekkenPlayerStart*_GAME_START`.
- Glass and water count as floor (stg32 fights on a glass pool floor).
- Stage effects: only systems with a looping emitter start with the stage (`CascadeEffects.Loops`);
  one-shot ones (12 of 101: `smokshot` break bursts, `Sandfall04_shot`) are event effects. Soft (depth-fade) particles at 30% opacity (`SOFT_FADE`), the flame shape mask
  turned tip-up when baked with its noise (`bake_composites`), light shafts at 40% brightness;
  all set by eye and documented as such. Stacked Cascade ColorScaleOverLife modules multiply
  (fixed in the shared hit-effects decoder; the battle effects keep their old export until re-run).
- Fog, mist and cloud cards are drawn at half alpha; helper materials (`M_Collision`,
  `M_NoPixelDraw`, `M_lighting`, `BOUNCEMAT*`) map to `Hidden.mat`; FogSheet and GodRay planes
  are skipped.
- Lighting is each stage's own (user asked to sync it, 2026-10-26): `StageLighting` replaces the
  scene's Render Settings rig while a stage is shown; the arena keeps the rig. Calibration is the
  stg29 one (native → lux 1:1 at fixed EV 0). Set by eye: baked-only lights ×0.25 capped 2000 lm
  (nearest 96) and summed to ≤ 6 lux at the fighters' chest (`BakedLuxBudget`; stg18/13/90 pack hundreds
  of bake lights), suns > 20 lux replaced by 8 (stg18's 200), sky intensity ≤ 1.5, sky reflections 35%. Lightmaps,
  reflection captures, light functions and flicker/pulse blueprints are not converted.
- Foliage wind: gentle by request — strength capped at 0.5, ~0.35 m per unit of strength.

## Stage scenes and baking

- Scenes hold a **linked prefab instance**; never copy geometry into them. Per-stage changes go in
  the scene as overrides or extra objects, and a re-import updates every scene. Regenerate with
  `force` only when the scene layout itself changes (it discards hand edits).
- Bake data: `StageBakedLight` on every light the original only baked (`standIn` = one of the
  nearest 96 that run at runtime; `bakeIntensity` = per-light value with all of them held to the
  6-lux budget at the match start). The scene switches all of them on as Baked; suns are Mixed.
- `MuseumStageImporter.MarkForBaking`: ContributeGI for opaque/cut-out, not transparent, helper or
  >500 m (sky); ReceiveGI lightmaps only for opaque, >1 m, with a valid channel-1 UV, else probes.
  `Unwrap` switches to 32-bit indices first; `Temp/fix-lightmap-uvs` repairs meshes in place.
  Four destructible props (stg06 watermelon and floor tiles, stg00 statue) can't be unwrapped and use probes.
- `Temp/capture-open-stage` renders the open stage scene (lightmaps included) through a temporary copy of
  its Stage Camera: lines `label yaw pitch distance [baked-only]` → `Validation/Stages/scene-<stg>-<label>.png`.
  Before a bake, HDRP draws Baked-mode lights as realtime and spams "Max shadow requests"; it stops after baking.
- First bake (2026-10-26, stg01, user-opened scene): 49 s, one 2048 lightmap + reflection probe, 14 MB. It adds
  sky occlusion under the roof and baked lamp light; sun bounce into shadows is weak (shadows stay near-black).
  Tuned bake (same day, user-approved direction): `StageSceneSetup.Tune` — indirect ×2.5, albedo boost 1.5,
  4 bounces, suns' indirect ×2, lightmapped surfaces within 30 m of the match start at 4× texel density.
  stg01: 9 min, four 2048 maps, 280 MB of source EXR (the first bake was 14 MB); bounce now lifts the shadows
  and walls. Disk: D: had 11 GB left after it, so all 34 at this density won't fit. The user chose (2026-10-26) to keep this
  density and bake stages selectively; don't lower ArenaScale or batch-bake all stages unasked. `Temp/retune-open-stage` applies Tune to the open scene (refuses unsaved changes unless
  the flag says `save-first`, only after the user confirms). Captures with `baked-only` dirty the scene: the user had to confirm twice on stg01 that the change was ours. Make `capture-open-stage` restore the scene without marking it dirty (or save it itself) before relying on it; until then, expect to ask every time.
- Break effects (`smokshot` bursts and the like) stay off because nothing in the remaster breaks floors yet.
- Lightmaps exist only in the stage scenes: versus and env_scene still spawn the unbaked prefabs.
- Stage particles share `CascadeEffects` and `CascadeParticle.shader` with the battle effects. The 2026-10-26/27 changes there (the `Linear()` gamma encode, peak compression above 4, the shoulder instead of whitening, SubUV atlas playback, `simulationLocal`) also change stage effects, and re-exporting `StageEffects` with the new fields turns atlas mode on. Re-capture stages after such changes; this hasn't been checked on any stage yet.
- Never bake with the user's scenes open (a bake writes lighting data into every open scene);
  `BakeOpenStageScene` refuses unless one stage scene is the only scene. Ask the user to open it.
- `StageLighting.PlacedProfile` deep-copies the profile's components (a plain `Instantiate` shared
  them, so runtime fog/sky overrides wrote into the stage profile asset).

- Navigation: `StageNavigator` ("Fly Navigator" under the StageScene root) is a free-fly camera in Play
  (WASD, Q/E, Shift/Ctrl, right-drag look, scroll speed, R start view), no collision. The user asked for the
  third-person fighter to be removed (2026-10-26): don't add a character back unasked. `Temp/add-stage-navigator`
  adds or renames it in existing scenes without rebuilding them (skips open scenes with unsaved changes;
  `save-open` in the flag only after the user confirms).

## Gotchas

- **Unity editor state.** Never replace or discard the user's open scenes; create `env_scene`
  additively. Batchmode can't open the project while the editor is running. After a crash, copy
  `Temp/__Backupscenes` to the scratchpad and let the user reopen Unity (it offers to restore).
- **Captures:** one `PreviewRenderUtility` per shot (a reused one turns every render after the
  first white), and `EditorUtility.UnloadUnusedAssetsImmediate()` after each shot (a full
  34-stage capture ran out of GPU memory at stg30 and crashed the editor). After a crash,
  recapture only the missing stages.
- **AssetPostprocessor changes reimport every texture in the project** (a long wait). Textures
  written before a postprocessor compiles import with default settings; the importer's
  `ConfigureTextures` fixes them.
- **Edit-mode code paths** (captures build `EnvironmentScene` outside Play) must use
  `DestroyImmediate`.
- `import_museum_stages.py <stg>` overwrites `stages.json`; test into a scratch `OUT` instead.
- `properties()` from `Tools/OriginalLighting/extract_stage_lighting.py` omits default values:
  a marker at the origin has no `RelativeLocation`.

## Status (2026-10-26)

Fixed and seen in preview captures only (the user has not yet checked them in Play): textures
on all stages (~2,900 of ~2,920 materials), winding, orientation, stg30/31 layers and fog, stg15
slab, stg01 floor and ivy, stg08a ring, sublevel props on most stages, stg01 god rays and sand/fire effects, stg30 light shafts (539 effect placements, 38 light shafts and 97 re-parented meshes across all stages). each stage's original lights, sky, fog and post, foliage wind on 16 stages; stg02/stg26 mist (`PS_EF_STG02_Mist`, HDR red-orange in the data) is tinted pale green-white at the user's request (`TINT_OVERRIDES` in the effects exporter; keep it). Open: stg28 fog cards
show their mask shapes; stg07 has no GAME_START (fighters on uneven rock); `WorldGridMaterial`,
engine Cube/Cylinder, GLOWMASK moons, stg11's live screen, stg07's heat haze and three
`violet_*_fix` textures that aren't 2D can't be recovered (stg90 draws 489 of 503 submeshes
textured); versus
movement bounds are still the generated arena's rectangle, so stage walls have no collision.
