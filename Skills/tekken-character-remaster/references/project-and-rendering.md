# Project integration, editor operation and rendering

## Locate the current project

Original session workspace: `D:\games\Tekken.7.Ultimate.Edition.v5.10`.
Project relative to that workspace: `UnityRemaster/TekkenVersusRemaster`.
Source tools: workspace `t7extract`; source game: `TEKKEN.7`; extracted references: `decrypted`.
These are discovery hints, not required paths. Read the target environment and `Tools/source-paths.json` first.

At creation the project used Unity 2023.2.22f1, HDRP 16.0.6. Read ProjectVersion.txt, manifest.json and the local PackageCache before changing APIs. Main scenes are `Assets/_Project/Scenes/LocalVersus2.unity`, `CharacterMuseum.unity`, `staging_test.unity`, `Wardrobe.unity` and `env_scene.unity` (stage selection; see [stages-and-environments.md](stages-and-environments.md)).

**Layout since 2026-10-25** (`Validation/restructure-map.json` maps every removed duplicate and GUID remap):

- `Assets/_Project/Characters/<Jin|Kazuya|Xiaoyu|Alisa>/`
  - `Source/{Base,Costumes,Wardrobe,Motion}`: importer inputs, kept inside Assets because the wardrobe catalogs reference about 900 source textures at runtime.
  - `Costumes/`, `Wardrobe/`: the imports. `Jin/Base` is the old Jin versus import; it stays separate from `Jin/Costumes` because their material names clash.
- `Shared/Textures`: byte-identical textures used by more than one fighter. Re-running an exporter can recreate one there; repeat the dedupe if they pile up.
- `Scripts/Runtime` and `Scripts/Editor`: the old `Scripts` and `Editor`.
- `Scenes`, `Resources` (still central, so `Resources.Load` names didn't change), `Museum`, `Wardrobe`, `Rendering`, `Settings`.
- At the repo root: `Docs/` for the feature notes and `START_HERE.txt`.
- In the outer `UnityRemaster/`, the live export, prepare and deploy scripts stay at the top level; old logs and baselines moved to `Archive/`.

**Leftovers from the restructure:**

- **Outfit saves (fixed 2026-10-25).** `FightWardrobe.ProjectFile()` kept writing to `Assets/Remaster/Resources/FightWardrobe` after the move. It now writes to `_Project/Resources/FightWardrobe`; the newest saves were moved there, both sets were backed up in `UnityRemaster/Archive/FightWardrobe-backup-2026-10-25`, and `Assets/Remaster` was removed. `asa.json` got a new GUID in the process, which is harmless because saves load by name.
- **Absolute path.** `Characters/Jin/Motion/CombatPoseLab/calibration.json` still points at `Assets/Remaster/Editor/RetargetedMotions/...`.
- **Unchecked reimport.** No one has confirmed that the museum, wardrobe, `staging_test` and `LocalVersus2` load without missing textures.
- **How to check a move.** A prefix rewrite of full paths misses templated paths (`f"…/{folder}Wardrobe"`, CHARACTERS tables, `run_colors.sh`, `Tools/SecondaryMotion/bake_*`, `Path.Combine(Application.dataPath, "Remaster/…")`). After moving files, grep for path fragments as well, run the versus skill's `compile_check.sh`, and compare GUID references before and after.

Game sources:

- **Read through `PakSet`.** The `_P` patch paks override 7,781 base files, so never read a single pak.
- **Encrypted entries.** All 4,307 AES entries are in the `_P` paks (DLC characters, stg24, mobs). t7extract selects its built-in key with `--tekken-key`, and decrypted copies exist under `decrypted/`. The user supplies keys; never search for or capture AES or USM keys.
- **Disk space.** The D: drive is nearly full, so test exports with filters into the scratchpad.

## Existing tools and limits

- `Tools/export_mesh.py` is a geometry-only static reference exporter. It does not recover a usable character's rig, UVs, materials, skin weights or animation.
- Character importers and museum setup contain Jin/Kazuya assumptions. Extend actual profile/catalog data and importer inputs; do not rename a Jin rig and treat it as another character.
- `Tools/CombatPoseConverter` supplies the established body decoder/reference-rig conversion. Dependencies include numpy/scipy for current baking tools.
- `Tools/SecondaryMotion` contains the native hand/face research and exporters. These are examples to parameterize, not already roster-wide tools.
- `Assets/_Project/Scripts/Runtime` contains runtime action catalogs, staging selection, native camera sampling, HDRP scene lighting and museum UI.
- `Assets/_Project/Scripts/Editor/StagingValidation.cs`, `StagingFlowValidation.cs`, `CombatHandValidation.cs`, `HdrpValidation.cs` are editor probes. Some assume current Jin/Kazuya objects or mutate the play state; inspect and generalize them to the requested fighter before using.
- `Validation/Staging`, `Validation/CombatHands`, `Validation/OriginalLighting` retain source records, captures and measured checks. Store fresh evidence with clear character/action attribution.
- `Tools/HitEffects/export_hit_effects.py` exports the original common hit-effect systems to `Resources/HitEffects`. `Scripts/Runtime/CascadeEffects.cs` plays them in versus (commit `14179dcd6`). The 2026-10-27 audit removed the punish reticle, KO, Pierce and floor effects from versus; see the versus skill and Codex's `tekken-versus-common-effects`.
- `UnityRemaster/CategoryBuild` is a copy of the project that Codex uses for batchmode builds while the editor is open. `deploy_xiaoyu_museum.py` and `deploy_xiaoyu_skirts.py` copy files from it back into the main project, with backups: `XiaoyuImporter.cs`, `XiaoyuSecondaryDynamics.cs`, `CharacterMuseum.cs` and `CharacterMuseum.unity`. The copy lacks later fixes (`WeldTriangleSoup`, `SeparateGarment`) and still has the abandoned `JointCorrection` code, so rerunning a deploy wipes main-project fixes. Sync CategoryBuild from the main project first, and update the deploy asserts (`characters=3, costumes=19, actions=6997`). Since the restructure the deploy scripts are broken anyway: `deploy_xiaoyu_*.py` read `CategoryBuild/Assets/_Project/...` while CategoryBuild still uses `Assets/Remaster` (with 676 uncommitted changes), and the other `deploy_*`/`apply_*` scripts read baselines that moved to `Archive/baselines`. Whether to restructure or retire them is the user's call. A copied project also needs `com.unity.modules.cloth` in its package manifest.

## Unity control and screenshots

Check whether Unity is playing before editing scripts or toggling Play. This project does not survive hot domain reload with an active nonserialized battle model; restart cleanly after recompiling. Do not confuse old compile errors in the log with the latest compiler result.

Discover current process/window handles and native menu IDs when using Windows automation. They change after restart or menu/script changes. Do not reuse session PIDs/HWNDs/menu IDs. If a pending test flag was consumed during a compilation error, requeue it after compilation succeeds.

Avoid replacing a dirty scene during validation. Prefer the user's open versus scene or an isolated validation scene; preserve unsaved scene changes. A camera render target captured after actual player-loop frames is reliable for skinned meshes. Desktop screenshots may capture a different application; use a game render target or the verified Unity window when relevant.

Existing test flags include `Temp/validate-staging`, `Temp/validate-staging-flow` and `Temp/hdrp-validate`; they are implementation details, not stable external APIs. Inspect their handlers and check that they stop Play/clear state on success and failure. Do not keep toggling Play when a validator has already stopped itself.

The user often keeps the editor open with an unsaved scene or in Play, so batchmode cannot open the project.

**Flag jobs.** Run editor jobs through `[InitializeOnLoad]` hooks that watch a `Temp/<flag>` file and skip while compiling or playing.

- **Compiling.** Create `Temp/refresh-assets` (`Scripts/Editor/AssetRefreshHook.cs`) and the open editor refreshes and recompiles without the user clicking in; it waits while Play is on. Wait for the DLL, then create the job flag. Without the hook (or before its first load), scripts only recompile when the editor gets focus. Computer-use resolves "Unity" to a separate 6000.6 install, so Claude cannot focus the 2023.2 editor; ask the user to stop Play or click in.
- **When to create the flag.** Only after `Library/ScriptAssemblies/Assembly-CSharp-Editor.dll` is newer than the edited script; otherwise the stale assembly consumes it.
- **Clean compile check.** Editor.log is `%LOCALAPPDATA%\Unity\Editor\Editor.log`. A compile is clean when the last `error CS` line is above the last `Reloading assemblies` line; old errors stay in the log.
- **Flag conventions.** A flag file is deleted when consumed. Failures go to `Temp/<flag>-error.txt`, and success writes a report (for example `Validation/xiaoyu-model.json` or `Validation/HDRP/eye-shadow.json`). Poll for the flag disappearing, then for the error file or a report newer than the edit. Run long chains (import, scene, capture; about 1,000 textures) as a background loop.
- **Re-imports.** These overwrite meshes and prefabs in place, so museum references survive; they need the editor out of Play.
- **Flag names.** They multiply (`refresh-assets`, `fix-eye-shadow`, `capture-eyes`, `capture-xiaoyu-rig`, `reconvert-xiaoyu-materials`, `reimport-xiaoyu`, `import-<code>-wardrobe`, `tints-<code>-wardrobe`, `capture-<code>-wardrobe`, `create-jin-wardrobe-scene`, `capture-hit-effects`, `create-staging-render-settings`, `add-render-settings-all-scenes`, ...). Read the handler rather than trusting a list.
- **Concurrent sessions.** Several sessions edit the same project at once, so commits mix their work. If the compile breaks in a file another session touched, fix it directly; the user prefers that to a report.

**Preview renders.** For quick close-ups without touching the open scene, render in a `PreviewRenderUtility` scene (`EyeCloseupCapture.cs`, `XiaoyuRigCapture.cs`).

- **Skinning.** Bake skinned meshes and submit `DrawMesh` again before every `Render`; submissions last one render. Pose with the same JRT1 frame the museum applies.
- **Prefab instances.** `InstantiatePrefabInScene` returns a connected prefab instance; unpack it before destroying children.
- **Camera side for face shots.** Pick it from the Head bone: flip forward when `dot(eyeCenter - Head.position, forward) < 0`. Choosing the side where the eyeballs bulge most put the camera inside Xiaoyu's head.
- **Limits.** Previews omit museum cloth, springs and scene lighting, and rear-facing gallery lights and post-scrub motion blur have hidden defects in captures. Confirm in the user's scene.
- **Recompile during Play.** A recompile during museum Play leaves `CharacterMuseum.bones` null (NullReferenceException in `SetFrame`); restart Play rather than debugging it.

**Run in background (2026-10-27).** Every scene keeps running while its window is unfocused: `PlayerSettings.runInBackground` is on, with the editor hook `Scripts/Editor/RunInBackgroundSetting.cs` keeping it on. `Scripts/Runtime/RunInBackground.cs` sets `Application.runInBackground` before each scene loads, and versus no longer pauses on focus loss. Don't add focus pauses back unless the user asks.

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
- **Lip overlays (`*_lip`).** These are an alpha overlay too. Xiaoyu's `T_CH_xia_lip_D` is flat pink, with the lip shape only in alpha (max about 0.31), on a transparent material. Alisa's cooked `T_CH_asa_lip_D` lost its alpha (1.0 everywhere), and with an opaque material a flat pink patch covered her lips.
  - The fix: `prepare_alisa_materials.py` borrows Xiaoyu's lip alpha whenever a lip diffuse has none, which works because the lip mesh and UV layout match, and keeps Alisa's own colour. `AlisaImporter` then makes `*_lip` transparent with `MakeEyeShadowTransparent`.
  - `IsEyeShadow` does not match `_lip`, so other importers need the same special case.
  - Check whether each cooked overlay diffuse kept its alpha. Once it's lost, the original mask can't be recovered from that texture; the borrowed shape is an approximation, so disclose it.
- **Lips, eyebrows and eyelashes are never opaque.** Opaque, all three "look strange" (the user's words, fixing Alisa), and the user set the current state as the reference (2026-10-25):

  | Materials | Surface |
  |---|---|
  | `*_lip` | transparent alpha blend, queue 3000 |
  | `*_eyebrow`, `*_eyelash` (Alisa, Xiaoyu, Jin's versus model) | alpha cutout at 0.25, queue 2475 |
  | `*_eyebrow`, `*_eyelash` (Jin's museum costumes, Kazuya) | transparent alpha blend |

  Both brow and lash conventions render acceptably; match the fighter's existing convention rather than flipping one to the other.
  - Make the importer produce the state, so a re-import keeps it. The prepare script's `cutout` list must contain `eyebrow` and `eyelash` alongside hair and feathers, and the importer must send `*_lip` through `MakeEyeShadowTransparent`.
  - After any import, list these materials' `_SurfaceType`, `_AlphaCutoffEnable` and render queue, and flag any that are opaque with no cutout.
- **Two-tone eye shader.** The eye material's specular slot holds `T_CH_eye_S`, which separates the white iris from the black pupil against a flat grey sclera. `Color01` tints the iris and `Color02` the sclera. Multiplying the whole eyeball by Color01 turned Xiaoyu's sclera red. `prepare_xiaoyu_materials.py` uses `iris = |S - S[0,0]| / min(S[0,0], 1 - S[0,0])`, `tint = iris*C1 + (1-iris)*C2` and `albedo = linear(diffuse.r) * tint`. `prepare_jin_materials.py` uses a different Color01–04 path; check each prepare script.
- **Eyeball gloss.** Smoothness 0.45 was an importer fallback, not a source value, and gave a broad milky reflection. `*_Eyes` use about 0.25, stored in the mask map's alpha by the prepare script; the `.mat` file's `_Smoothness: 0.45` is unused when a mask map is assigned. The real cornea highlight is the separate `Eye_ADD` layer (Ref_Intensity 0.3, cubemap, `T_CH_eye_ref_small`, `T_CH_Eye_outer_N`), which every importer still drops along with `MI_CH_tear`. Disclose it as an open gap.
- **Masks are not regenerated by prepare scripts.** `*_MaskMap.png` is built only inside `ConvertMaterial`. After re-running a prepare script, reconvert the material or copy the changed channel into the mask.
- **Material prep conventions.**
  - Outfit albedo is the colour-mask palette: mask RGB plus a remainder, times `Color01–04`, in linear space. User-chosen item and hair colours are a runtime re-tint (`CostumeColors`, `CostumeTint.shader`); its rules are in the wardrobe skill's `formats.md`.
  - Packed skin specular gives specular = R×0.096 and AO from B.
  - Without a roughness map, roughness comes from the normal map's alpha scaled by `RoughnessMin/Max`.
  - The t7extract glTF exporter wires only the `Diffuse` and `Normal` parameters, so grey diffuse textures usually mean an unapplied tint, not a decode bug.
- **Shared clothing metallic.** Clothing surface maps carry high red values (Jin and Kazuya at about 0.8–1.0 metallic) as the project's existing convention. Compare a new fighter against that before treating high metallic alone as a defect.

Compare a new fighter's material table (MaterialID, mask means, specular map presence, albedo means) against Jin and Kazuya before judging appearance. Matching scalars with different looks usually means a texture or mesh defect.

Scene lights created at runtime default to `LightShadows.None`. Without shadows, light reaches the eyeballs and mouth interior through lids and lips, giving bright lid rims and a glowing lip line. The fix, which the user confirmed in `staging_test` ("Looks fixed"):

- soft shadows on the key light only;
- HDRP contact shadows on both lights (length about 0.04 m, thickness 0.02), because lids and lips are too thin for the shadow map;
- a 12 m shadow distance.

**One shared look for every runtime scene.** All runtime scenes (staging_test, CharacterMuseum, Wardrobe, LocalVersus2, env_scene, and the stage scenes and effect showcases since; LocalVersus was retired 2026-10-27) now carry a **"Render Settings" GameObject**. `StagingRenderSettingsSetup`'s "all scenes" list leaves out env_scene, which got its own copy from `EnvironmentSceneSetup`. The object holds:

- A `StagingRenderSettings` component re-applies the ambient, background and floor colours every frame, plus an Antialiasing section (default SMAA High; TAA quality, sharpen, anti-flicker and history sharpening). `HdrpSceneLighting` forces SMAA only in scenes without the object. MSAA is set on `RemasterHDRP.asset`, not per camera. The antialiasing compiles but hasn't been checked in Play.
- A "Key light" child: white, 1.35×π lux, soft shadows, yaw -45.
- A "Fill light" child: (0.65, 0.75, 1), 0.65×π lux, no shadow map, yaw 45.
- Both lights use contact shadows.
- A priority-10 Volume uses the **shared** `Rendering/StagingTestVolume.asset` (contact shadows, 12 m shadow distance).

Controllers call `StagingRenderSettings.FindOrCreate`, which builds identical defaults only when a scene lacks the object. Versus yaws it 180° (`VersusYaw`), and while it exists `HdrpSceneLighting` leaves the extracted stg29 lighting and sky dormant; delete the object to restore them. The museum and wardrobe keep their own priority-100 motion-blur-off volumes.

How to change the look:

- Change it in the scene object (lights and colours, separate in each scene) or in the profile (shared by all five scenes), not in code.
- Lights and colours changed during Play are lost; profile edits are kept.
- Menu: Remaster → Rendering → "Add staging render settings to (all) scenes".
- For a warm or soft look, add your own Global Volume above priority 10 (White Balance, Color Adjustments, Bloom, Vignette), plus light angular diameter. Exposure is fixed in the project profiles, and `HdrpSceneLighting` replaces the runtime global volume's profile, so don't add overrides there.
- Only the staging_test close-ups were checked by eye; the museum, wardrobe and versus haven't been. The user turned down a ScriptableObject in favour of a visible Hierarchy object with real lights, and was asked before the original stg29 lighting was replaced. Keep that kind of change reversible.

Open fidelity gaps to keep reporting:

- `Eye_ADD` and tear layers are dropped.
- Hair renders black by default: the wardrobe colour pass defaults hair to black, and the user can pick another colour. Grey hair seen earlier came from unapplied tints.
- Xiaoyu's body, arm and leg skin uses shared female textures that are lighter and pinker than her face.

## HDRP and original lighting (only when requested)

Prefer current HDRP-compatible material templates and validate all costume materials. Keep texture color-space, masks, skin weighting, emission and transparency distinct. A successful shader conversion is not proof of appearance fidelity.

For source stage lighting use `Tools/OriginalLighting/extract_stage_lighting.py`, `prepare_hdrp_sky.py`, `OriginalStageLightingData.cs`, `OriginalStageLightingSetup.cs`, and the matching validation reports. The original session extracted stg29, not a universal lighting setup for every arena, and it is not per-stage: loading stg05 in versus does not bring up stg05's lights.

Separate environment lights from character-only lights using HDRP rendering layers. Preserve original colors, intensity ratios and light directions with an explicit world-basis conversion. Do not copy UE Euler values directly into Unity or equate unknown native units with physical lux. Source omitted/inherited fields remain unknown until resolved.

Import all six HDR cubemap faces with verified orientation and row order, preserve HDR RGB, and inspect alpha semantics. The original capture used auxiliary alpha; treating it as opacity made the sky black. A face-order/up-axis error put the sky below the horizon. Validate actual background, ambient light and reflections.

Treat UE exposure, bloom, AO, SSR and tone-curve mappings as calibration choices when the renderers lack matching controls. In the example, locked source exposure became fixed HDRP exposure; disabled fog stayed disabled; a global reflection multiplier approximated an SSR-specific intensity and affects more than SSR. Do not apply KO/Rage-only parameters as neutral global grading.

The gameplay camera's observed 65-degree horizontal FOV, 16:9 gate and 36x20.25 mm equivalent sensor were measured/adopted for this reference. Verify another reference rather than declaring these Tekken-wide constants.

## Completion and source control

Report imported fighters/costumes/actions and what remains missing, approximate or fallback. Preserve unrelated local work and stage only the requested changes when committing is authorized. A local commit is not a successful push. Do not repeatedly retry a stalled upload indefinitely or infer permission to publish extracted assets from permission to build a local character.

**Git:**

- **Ownership.** The `TekkenVersusRemaster` repository is owned by another Windows user (CodexSandboxOffline). Use `git -c safe.directory='*' …` and never change global git config without asking.
- **Git LFS (finished 2026-10-26).** GitHub and D: are both on the rewritten history (`main` =
  `origin/main` = `1559ba873` on 2026-10-26, 110 commits): about 0.45 GiB of git plus 2.6 GB in LFS.
  - `.gitattributes` sends png, jpg/jpeg, tga, psd, exr, hdr, tif/tiff, fbx, wav, ogg, mp3, mp4,
    bytes, t7motion, t7sky, t7native and c8 to LFS; Unity text assets stay in plain git (they
    compress about 10:1).
  - Every pre-LFS SHA changed (`b71b8341e` is `14179dcd6` now). The old history lives only on
    `pre-lfs-main` (`58fcbc89d`) and the other backup branches (`backup-before-split`,
    `local-snapshot`, `original-snapshot`, `upload`). Any other clone must be cloned again.
  - **Keep the repo-local `lfs.storage`** (`C:/Users/Administrator/.git-lfs-storage/Tekken_v1`):
    LFS writes on D: fail, and removing the setting breaks `status` and `add` for LFS files.
  - `git status` is slow now (every binary goes through the LFS filter): limit it to paths, or run
    it in the background.
  - Leftovers awaiting the user's decision:
    - the C: scratch clone `…\1b5e5056-…\scratchpad\repo-lfs` (2.7 GB; its objects are in the C: store);
    - D:'s `.git/lfs` (6.3 GB, filled by an earlier copy, probably unused now; unverified);
    - the stash `pre-lfs working changes` (38 files restored, the newer working copies of the
      other 39 kept).
- **The D: drive breaks heavy git work.** It fails to create and rename files ("unable to write
  new index file", "could not write index", `git init` "couldn't set HEAD"). TGitCache is at most a
  contributor. Retry `reset`, `add` and `status` in a loop (staging has needed up to 20 tries), and
  do history rewrites in a clone on C:.
- **Pushing.**
  - Command: `env -u HTTPS_PROXY -u HTTP_PROXY git -c safe.directory=* -c pack.useSparse=false push origin main`.
    The env proxy `127.0.0.1:3128` is usually dead, and the user prefers pushing without the VPN.
  - `pack.useSparse=false` matters for commits that move files: the restructure commit packed at
    411 MB with sparse packing and 3 MB without.
  - `~/.gitconfig` sets `http.postbuffer=524288000` globally and `core.autocrlf=true` while the repo
    is LF; override per command and normalise copied files, but never edit global config.
  - Check github.com itself before a long push (github.com:443 was blocked for a while on
    2026-10-26 while api.github.com answered). Astrill VPN can look connected without carrying
    traffic (route metric 9999 against 256 for Ethernet).
  - Run long pushes in a detached window (`Start-Process` on a `.cmd` file that logs and retries
    until `git ls-remote` succeeds); Claude background jobs die with the session, and an orphaned
    `git-lfs` can sit in SYN_SENT indefinitely.
  - LFS uploads resume. For a rewrite force-push, upload the LFS objects first, then
    `--force-with-lease=main:<old sha>`, chained with `&&`, never `;`.
  - The old "~300 MB chunks, one commit per push" advice is obsolete under LFS. Side-branch chunk
    commits never shrank a later push anyway.
- **Partial-file commits with other sessions' work mixed in.** Build the file from HEAD plus your
  hunks, then `git hash-object -w --no-filters` and `git update-index --cacheinfo`. `update-index`
  can fail with no lock present; retry it in a loop.
- **Remote.** `origin` is `BonSmith0251/Tekken_v1`. Commit or push only when the user asks;
  "commit (don't push yet)" means hold the push. Report the real status from `git log origin/main..`.
- **Rewriting history.** Before splitting or rewriting commits, list unpushed commits from other sessions and branches. A split once dropped another session's local commit; its content survived only because an identical commit existed.
- **Index lock.** TortoiseGit's `TGitCache` intermittently blocks `git add` with "unable to write new index file". Loop the retry: staging has needed up to 20 attempts and committing up to 9.
- **Bulk output.** Commit regenerable bulk output separately so it can be dropped: the textured stages alone are about 3 GB.
- **User data.** Treat `Resources/FightWardrobe/*.json` as user data.

The repository has a reference copy of this skill under `Skills/tekken-character-remaster`. The original is `~/.codex/skills/tekken-character-remaster`; read that, and sync the copy when the skill changes.
