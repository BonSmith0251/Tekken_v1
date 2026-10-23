# Project integration, editor operation and rendering

## Locate the current project

Original session workspace: `D:\games\Tekken.7.Ultimate.Edition.v5.10`.
Project relative to that workspace: `UnityRemaster/TekkenVersusRemaster`.
Source tools: workspace `t7extract`; source game: `TEKKEN.7`; extracted references: `decrypted`.
These are discovery hints, not required paths. Read the target environment and `Tools/source-paths.json` first.

At creation the project used Unity 2023.2.22f1, HDRP 16.0.6. Read ProjectVersion.txt, manifest.json and the local PackageCache before changing APIs. Main scenes are `Assets/Remaster/Scenes/LocalVersus2.unity`, `CharacterMuseum.unity`, `staging_test.unity` and `Wardrobe.unity`.

Game sources:

- **Read through `PakSet`.** The `_P` patch paks override 7,781 base files, so never read a single pak.
- **Encrypted entries.** All 4,307 AES entries are in the `_P` paks (DLC characters, stg24, mobs). t7extract selects its built-in key with `--tekken-key`, and decrypted copies exist under `decrypted/`. The user supplies keys; never search for or capture AES or USM keys.
- **Disk space.** The D: drive is nearly full, so test exports with filters into the scratchpad.

## Existing tools and limits

- `Tools/export_mesh.py` is a geometry-only static reference exporter. It does not recover a usable character's rig, UVs, materials, skin weights or animation.
- Character importers and museum setup contain Jin/Kazuya assumptions. Extend actual profile/catalog data and importer inputs; do not rename a Jin rig and treat it as another character.
- `Tools/CombatPoseConverter` supplies the established body decoder/reference-rig conversion. Dependencies include numpy/scipy for current baking tools.
- `Tools/SecondaryMotion` contains the native hand/face research and exporters. These are examples to parameterize, not already roster-wide tools.
- `Assets/Remaster/Scripts` contains runtime action catalogs, staging selection, native camera sampling, HDRP scene lighting and museum UI.
- `Assets/Remaster/Editor/StagingValidation.cs`, `StagingFlowValidation.cs`, `CombatHandValidation.cs`, `HdrpValidation.cs` are editor probes. Some assume current Jin/Kazuya objects or mutate the play state; inspect and generalize them to the requested fighter before using.
- `Validation/Staging`, `Validation/CombatHands`, `Validation/OriginalLighting` retain source records, captures and measured checks. Store fresh evidence with clear character/action attribution.
- `UnityRemaster/CategoryBuild` is a copy of the project that Codex uses for batchmode builds while the editor is open. `deploy_xiaoyu_museum.py` and `deploy_xiaoyu_skirts.py` copy files from it back into the main project, with backups: `XiaoyuImporter.cs`, `XiaoyuSecondaryDynamics.cs`, `CharacterMuseum.cs` and `CharacterMuseum.unity`. The copy lacks later fixes (`WeldTriangleSoup`, `SeparateGarment`) and still has the abandoned `JointCorrection` code, so rerunning a deploy wipes main-project fixes. Sync CategoryBuild from the main project first, and update the deploy asserts (`characters=3, costumes=19, actions=6997`). A copied project also needs `com.unity.modules.cloth` in its package manifest.

## Unity control and screenshots

Check whether Unity is playing before editing scripts or toggling Play. This project does not survive hot domain reload with an active nonserialized battle model; restart cleanly after recompiling. Do not confuse old compile errors in the log with the latest compiler result.

Discover current process/window handles and native menu IDs when using Windows automation. They change after restart or menu/script changes. Do not reuse session PIDs/HWNDs/menu IDs. If a pending test flag was consumed during a compilation error, requeue it after compilation succeeds.

Avoid replacing a dirty scene during validation. Prefer the user's open versus scene or an isolated validation scene; preserve unsaved scene changes. A camera render target captured after actual player-loop frames is reliable for skinned meshes. Desktop screenshots may capture a different application; use a game render target or the verified Unity window when relevant.

Existing test flags include `Temp/validate-staging`, `Temp/validate-staging-flow` and `Temp/hdrp-validate`; they are implementation details, not stable external APIs. Inspect their handlers and check that they stop Play/clear state on success and failure. Do not keep toggling Play when a validator has already stopped itself.

The user often keeps the editor open with an unsaved scene or in Play, so batchmode cannot open the project.

**Flag jobs.** Run editor jobs through `[InitializeOnLoad]` hooks that watch a `Temp/<flag>` file and skip while compiling or playing.

- **Compiling.** Scripts only recompile when the editor gets focus. Computer-use access resolves "Unity" to a separate 6000.6 install, not the 2023.2 editor, so Claude cannot focus it. Ask the user to stop Play and click into Unity. A fix usually takes two round trips: compile, then the flag.
- **When to create the flag.** Only after `Library/ScriptAssemblies/Assembly-CSharp-Editor.dll` is newer than the edited script; otherwise the stale assembly consumes it.
- **Clean compile check.** Editor.log is `%LOCALAPPDATA%\Unity\Editor\Editor.log`. A compile is clean when the last `error CS` line is above the last `Reloading assemblies` line; old errors stay in the log.
- **Flag conventions.** A flag file is deleted when consumed. Failures go to `Temp/<flag>-error.txt`, and success writes a report (for example `Validation/xiaoyu-model.json` or `Validation/HDRP/eye-shadow.json`). Poll for the flag disappearing, then for the error file or a report newer than the edit. Run long chains (import, scene, capture; about 1,000 textures) as a background loop.
- **Re-imports.** These overwrite meshes and prefabs in place, so museum references survive; they need the editor out of Play.
- **Flag names.** They multiply (`fix-eye-shadow`, `capture-eyes`, `capture-xiaoyu-rig`, `reconvert-xiaoyu-materials`, `reimport-xiaoyu`, `import-<code>-wardrobe`, `capture-<code>-wardrobe`, `create-jin-wardrobe-scene`, ...). Read the handler rather than trusting a list.

**Preview renders.** For quick close-ups without touching the open scene, render in a `PreviewRenderUtility` scene (`EyeCloseupCapture.cs`, `XiaoyuRigCapture.cs`).

- **Skinning.** Bake skinned meshes and submit `DrawMesh` again before every `Render`; submissions last one render. Pose with the same JRT1 frame the museum applies.
- **Prefab instances.** `InstantiatePrefabInScene` returns a connected prefab instance; unpack it before destroying children.
- **Camera side for face shots.** Pick it from the Head bone: flip forward when `dot(eyeCenter - Head.position, forward) < 0`. Choosing the side where the eyeballs bulge most put the camera inside Xiaoyu's head.
- **Limits.** Previews omit museum cloth, springs and scene lighting, and rear-facing gallery lights and post-scrub motion blur have hidden defects in captures. Confirm in the user's scene.
- **Recompile during Play.** A recompile during museum Play leaves `CharacterMuseum.bones` null (NullReferenceException in `SetFrame`); restart Play rather than debugging it.

**Editing hygiene.**

- Wrap prefab builds in try/finally: a failed build once left a half-built root in the user's open scene.
- Rename scenes together with their `.meta` so the GUID survives.
- Many scripts use CRLF line endings; keep them when writing.
- Local `.ps1` scripts are blocked by execution policy; run them as `powershell -ExecutionPolicy Bypass -File …`.

## HDRP material conversion defects (check on every import)

All importers convert legacy Standard materials through `HdrpProjectSetup.ConvertMaterial`. These defects were each invisible until a later change exposed them:

- **Blank mask maps.** HDRP's `StandardsToHDLitMaterialUpgrader` builds `_MaskMap` with `TextureCombiner` on the GPU, which writes black once HDRP is the active pipeline. Every Xiaoyu mask came out RGBA 0: no ambient light (G occlusion) and fully matte (A smoothness). `ConvertMaterial` now rebuilds the mask on the CPU from the decoded inputs with the upgrader's channel rules. After any import, check that no `*_MaskMap.png` is all zero.
- **Stale `_MaterialID`.** Importers reuse existing material assets and the upgrader never resets `_MaterialID`. A leftover SpecularColor ID (4) with no `_SpecularColorMap` and white `_SpecularColor` turns into an energy-conserving mirror (museum Jin's eyes rendered chrome). `ConvertMaterial` now resets it to Standard (1) when the source has no specular map.
- **Alpha face overlays.** `*_eye_shadow` textures are black with a soft alpha gradient over the upper eyeball. Opaque or alpha-tested, they paint a hard black shape over the eye. Xiaoyu's `*_eye_corner` (alpha 0–255) showed as pink-red patches at the inner corners when opaque. `HdrpProjectSetup.IsEyeShadow` matches both suffixes, `MakeEyeShadowTransparent` sets transparent alpha blend, every importer calls it, and the menu item/`fix-eye-shadow` flag repairs existing assets. Audit any face material whose diffuse has meaningful alpha.
- **Two-tone eye shader.** The eye material's specular slot holds `T_CH_eye_S`, which separates the white iris from the black pupil against a flat grey sclera. `Color01` tints the iris and `Color02` the sclera. Multiplying the whole eyeball by Color01 turned Xiaoyu's sclera red. `prepare_xiaoyu_materials.py` uses `iris = |S - S[0,0]| / min(S[0,0], 1 - S[0,0])`, `tint = iris*C1 + (1-iris)*C2` and `albedo = linear(diffuse.r) * tint`. `prepare_jin_materials.py` uses a different Color01–04 path; check each prepare script.
- **Eyeball gloss.** Smoothness 0.45 was an importer fallback, not a source value, and gave a broad milky reflection. `*_Eyes` use about 0.25. The real cornea highlight is the separate `Eye_ADD` layer (Ref_Intensity 0.3, cubemap, `T_CH_eye_ref_small`, `T_CH_Eye_outer_N`), which every importer still drops along with `MI_CH_tear`. Disclose it as an open gap.
- **Masks are not regenerated by prepare scripts.** `*_MaskMap.png` is built only inside `ConvertMaterial`. After re-running a prepare script, reconvert the material or copy the changed channel into the mask.
- **Material prep conventions.**
  - Outfit albedo is the colour-mask palette: mask RGB plus a remainder, times `Color01–04`, in linear space.
  - Packed skin specular gives specular = R×0.096 and AO from B.
  - Without a roughness map, roughness comes from the normal map's alpha scaled by `RoughnessMin/Max`.
  - The t7extract glTF exporter wires only the `Diffuse` and `Normal` parameters, so grey diffuse textures usually mean an unapplied tint, not a decode bug.
- **Shared clothing metallic.** Clothing surface maps carry high red values (Jin and Kazuya at about 0.8–1.0 metallic) as the project's existing convention. Compare a new fighter against that before treating high metallic alone as a defect.

Compare a new fighter's material table (MaterialID, mask means, specular map presence, albedo means) against Jin and Kazuya before judging appearance. Matching scalars with different looks usually means a texture or mesh defect.

Scene lights created at runtime default to `LightShadows.None`. Without shadows, light reaches the eyeballs and mouth interior through lids and lips, giving bright lid rims and a glowing lip line. `StagingTestPlayer.CreateSet` fixes this:

- soft shadows on the key light only;
- HDRP contact shadows on both lights (length about 0.04 m, thickness 0.02), because lids and lips are too thin for the shadow map;
- a volume local to the scene with a 12 m shadow distance, leaving the shared `RemasterEnvironment` profile unchanged;
- HDRP intensity set the same way `HdrpSceneLighting` sets it.

The user confirmed it ("Looks fixed"), and `Wardrobe.unity` reuses the setup. The museum and versus lights have not been checked.

Open fidelity gaps to keep reporting:

- `Eye_ADD` and tear layers are dropped.
- Jin's hair renders light grey; it is black in the original. Not investigated.
- Xiaoyu's body, arm and leg skin uses shared female textures that are lighter and pinker than her face.

## HDRP and original lighting (only when requested)

Prefer current HDRP-compatible material templates and validate all costume materials. Keep texture color-space, masks, skin weighting, emission and transparency distinct. A successful shader conversion is not proof of appearance fidelity.

For source stage lighting use `Tools/OriginalLighting/extract_stage_lighting.py`, `prepare_hdrp_sky.py`, `OriginalStageLightingData.cs`, `OriginalStageLightingSetup.cs`, and the matching validation reports. The original session extracted stg29, not a universal lighting setup for every arena.

Separate environment lights from character-only lights using HDRP rendering layers. Preserve original colors, intensity ratios and light directions with an explicit world-basis conversion. Do not copy UE Euler values directly into Unity or equate unknown native units with physical lux. Source omitted/inherited fields remain unknown until resolved.

Import all six HDR cubemap faces with verified orientation and row order, preserve HDR RGB, and inspect alpha semantics. The original capture used auxiliary alpha; treating it as opacity made the sky black. A face-order/up-axis error put the sky below the horizon. Validate actual background, ambient light and reflections.

Treat UE exposure, bloom, AO, SSR and tone-curve mappings as calibration choices when the renderers lack matching controls. In the example, locked source exposure became fixed HDRP exposure; disabled fog stayed disabled; a global reflection multiplier approximated an SSR-specific intensity and affects more than SSR. Do not apply KO/Rage-only parameters as neutral global grading.

The gameplay camera's observed 65-degree horizontal FOV, 16:9 gate and 36x20.25 mm equivalent sensor were measured/adopted for this reference. Verify another reference rather than declaring these Tekken-wide constants.

## Completion and source control

Report imported fighters/costumes/actions and what remains missing, approximate or fallback. Preserve unrelated local work and stage only the requested changes when committing is authorized. A local commit is not a successful push. Do not repeatedly retry a stalled upload indefinitely or infer permission to publish extracted assets from permission to build a local character.

**Git:**

- **Ownership.** The `TekkenVersusRemaster` repository is owned by another Windows user (CodexSandboxOffline). Use `git -c safe.directory='*' …` and never change global git config without asking.
- **Remote.** The remote is `origin` (`BonSmith0251/Tekken_v1`). Commit or push only when the user asks; report the real status.
- **Index lock.** TortoiseGit's `TGitCache` intermittently blocks `git add` with "unable to write new index file"; retry the step.
- **Bulk output.** Commit regenerable bulk output (for example the stage layouts) separately so it can be dropped.
- **User data.** Treat `Resources/FightWardrobe/*.json` as user data.

The repository has a reference copy of this skill under `Skills/tekken-character-remaster`. The original is `~/.codex/skills/tekken-character-remaster`; read that, and sync the copy when the skill changes.
