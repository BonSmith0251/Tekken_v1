# Project integration, editor operation and rendering

## Locate the current project

Original session workspace: `D:\games\Tekken.7.Ultimate.Edition.v5.10`.
Project relative to that workspace: `UnityRemaster/TekkenVersusRemaster`.
Source tools: workspace `t7extract`; source game: `TEKKEN.7`; extracted references: `decrypted`.
These are discovery hints, not required paths. Read the target environment and `Tools/source-paths.json` first.

At creation the project used Unity 2023.2.22f1, HDRP 16.0.6. Read ProjectVersion.txt, manifest.json and the local PackageCache before changing APIs. Main scenes are `Assets/Remaster/Scenes/LocalVersus2.unity` and `CharacterMuseum.unity`.

## Existing tools and limits

- `Tools/export_mesh.py` is a geometry-only static reference exporter. It does not recover a usable character's rig, UVs, materials, skin weights or animation.
- Character importers and museum setup contain Jin/Kazuya assumptions. Extend actual profile/catalog data and importer inputs; do not rename a Jin rig and treat it as another character.
- `Tools/CombatPoseConverter` supplies the established body decoder/reference-rig conversion. Dependencies include numpy/scipy for current baking tools.
- `Tools/SecondaryMotion` contains the native hand/face research and exporters. These are examples to parameterize, not already roster-wide tools.
- `Assets/Remaster/Scripts` contains runtime action catalogs, staging selection, native camera sampling, HDRP scene lighting and museum UI.
- `Assets/Remaster/Editor/StagingValidation.cs`, `StagingFlowValidation.cs`, `CombatHandValidation.cs`, `HdrpValidation.cs` are editor probes. Some assume current Jin/Kazuya objects or mutate the play state; inspect and generalize them to the requested fighter before using.
- `Validation/Staging`, `Validation/CombatHands`, `Validation/OriginalLighting` retain source records, captures and measured checks. Store fresh evidence with clear character/action attribution.

## Unity control and screenshots

Check whether Unity is playing before editing scripts or toggling Play. This project does not survive hot domain reload with an active nonserialized battle model; restart cleanly after recompiling. Do not confuse old compile errors in the log with the latest compiler result.

Discover current process/window handles and native menu IDs when using Windows automation. They change after restart or menu/script changes. Do not reuse session PIDs/HWNDs/menu IDs. If a pending test flag was consumed during a compilation error, requeue it after compilation succeeds.

Avoid replacing a dirty scene during validation. Prefer the user's open versus scene or an isolated validation scene; preserve unsaved scene changes. A camera render target captured after actual player-loop frames is reliable for skinned meshes. Desktop screenshots may capture a different application; use a game render target or the verified Unity window when relevant.

Existing test flags include `Temp/validate-staging`, `Temp/validate-staging-flow` and `Temp/hdrp-validate`; they are implementation details, not stable external APIs. Inspect their handlers and check that they stop Play/clear state on success and failure. Do not keep toggling Play when a validator has already stopped itself.

## HDRP and original lighting (only when requested)

Prefer current HDRP-compatible material templates and validate all costume materials. Keep texture color-space, masks, skin weighting, emission and transparency distinct. A successful shader conversion is not proof of appearance fidelity.

For source stage lighting use `Tools/OriginalLighting/extract_stage_lighting.py`, `prepare_hdrp_sky.py`, `OriginalStageLightingData.cs`, `OriginalStageLightingSetup.cs`, and the matching validation reports. The original session extracted stg29, not a universal lighting setup for every arena.

Separate environment lights from character-only lights using HDRP rendering layers. Preserve original colors, intensity ratios and light directions with an explicit world-basis conversion. Do not copy UE Euler values directly into Unity or equate unknown native units with physical lux. Source omitted/inherited fields remain unknown until resolved.

Import all six HDR cubemap faces with verified orientation and row order, preserve HDR RGB, and inspect alpha semantics. The original capture used auxiliary alpha; treating it as opacity made the sky black. A face-order/up-axis error put the sky below the horizon. Validate actual background, ambient light and reflections.

Treat UE exposure, bloom, AO, SSR and tone-curve mappings as calibration choices when the renderers lack matching controls. In the example, locked source exposure became fixed HDRP exposure; disabled fog stayed disabled; a global reflection multiplier approximated an SSR-specific intensity and affects more than SSR. Do not apply KO/Rage-only parameters as neutral global grading.

The gameplay camera's observed 65-degree horizontal FOV, 16:9 gate and 36x20.25 mm equivalent sensor were measured/adopted for this reference. Verify another reference rather than declaring these Tekken-wide constants.

## Completion and source control

Report imported fighters/costumes/actions and what remains missing, approximate or fallback. Preserve unrelated local work and stage only the requested changes when committing is authorized. A local commit is not a successful push. Do not repeatedly retry a stalled upload indefinitely or infer permission to publish extracted assets from permission to build a local character.
