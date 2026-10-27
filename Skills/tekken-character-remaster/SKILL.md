---
name: tekken-character-remaster
description: Import, repair, and validate original Tekken character models, body and hand/facial animation, entrance and victory staging, and action-specific cameras in a Unity remaster. Use when extending this character/museum/versus pipeline to additional fighters or fixing its animation, camera, HDRP material, skinning or cloth problems (wrong-looking eyes, skin, faceting, swollen limbs); includes optional HDRP stage-lighting guidance.
---

# Tekken Character Remaster

Extend the existing project using each requested fighter's own source assets and events. This skill captures the tested workflow for Jin, Kazuya, Xiaoyu, Alisa and Katarina across the museum, `staging_test` and versus. Alisa (`asa`, 2026-10-23 to 25) and Katarina (`ltn`, 2026-10-25, about 20 minutes from request to versus) are the newest. The user has not confirmed either in a scene; only headless matches and preview renders passed. `LocalVersus2` opens on a character select screen; its saved pair is Alisa vs Katarina (the `VersusGame` code default is Jin vs Xiaoyu). Original stages and the `env_scene` selector belong to the Claude skill `tekken-stage-environments` (`~/.claude/skills`); [stages-and-environments.md](references/stages-and-environments.md) is a summary of it. It is not evidence that every fighter uses their skeleton, channel layout, motion IDs, or coordinate mapping.

Sibling skills cover the rest. Codex skills in `~/.codex/skills`: `tekken-character-battle-effects` (each fighter's own 0x802E effects), `tekken-versus-common-effects` (the shared hit/guard/rage effects) and `tekken-versus-camera-effects` (shakes, zooms, Rage Art cameras). Claude skills in `~/.claude/skills`, including `tekken-stage-environments` for stages:

- `tekken-costume-wardrobe`: costumes, outfits and worn items (hair, hats, glasses, customization pieces). The wardrobe stays in its own `Wardrobe.unity` scene.
- `tekken-versus-battle`: the versus scene (command graphs, hits, facing, KO, fight camera, impact effects, CPU) and adding a fighter to it.

## Start with the requested scope

- Locate the Unity project, installed source game and extraction tools. Read `Tools/source-paths.json` if present; discover current paths rather than assuming the original machine layout.
- Inspect the working tree and current Unity editor/play state. Preserve existing museum, costume and render work. Stop an active Play session before script changes: this project's nonserialized battle state is lost on domain reload.
- For a named fighter, process that fighter. For an explicit request for all characters, inventory the available roster and process it incrementally; report coverage by character without presenting one successful fighter as roster-wide success.
- Follow the user's chosen scene, engine version and rendering pipeline. A new character does not require a pipeline migration, stage replacement or publishing operation.
- Confirm which scene a visual report comes from before fixing it. The museum, `staging_test`, versus and the wardrobe each have their own copy of the "Render Settings" lights and their own dynamics, but all share one volume profile. A user once had to stop a museum investigation with "no, no. in the staging_test scene."
- When the user asks how to do something ("I'll do it by myself, just let me know how"), explain it without editing anything.
- Other Claude sessions and Codex may edit this project concurrently, and Codex keeps a batchmode copy (`UnityRemaster/CategoryBuild`) whose deploy scripts overwrite main-project files. Check recent changes and linked sessions before rebuilding something; see [project-and-rendering.md](references/project-and-rendering.md).
- Stay within the requested scope. Offer follow-ups such as wiring a fighter into another scene, and wait for the user to ask. They first declined Xiaoyu in versus ("Not now"), then asked for it later.

## Build a character profile first

Read [character-pipeline.md](references/character-pipeline.md) for body, skin, hand and face conversion. Record source character code, animation prefix (it can differ from the code: Xiaoyu is `xia` with `fu_` animations), moveset/archive paths, skeleton asset, costume assets, proven coordinate conversions, stance move, staging candidates and imported resource locations. Resolve these per fighter. `Tools/xiaoyu-profile.json` and `Tools/alisa-profile.json` in the Unity project are the worked examples of a profile (Alisa's `fallbacks` entry still says she has no wardrobe, which is out of date).

Run the bundled read-only inspector to find candidate staging records and audit their references:

```text
python <skill>/scripts/inspect_character.py --moveset <moveset.json> --archive <character-rotsm.arc>
```

The JSON output identifies candidates and unresolved references, not semantic proof or decoded motion. Inspect source records and previews before accepting a candidate. Missing archives, unsupported bank layouts and malformed data are visible gaps to resolve, not reasons to substitute Jin's assets.

## Implement only the relevant layers

1. Import the requested costume/skeleton with original skin weights; map the body and secondary bones explicitly.
2. Convert and verify a stance, then the requested moves. Retain body/secondary frame relationships and distinguish source move IDs from Unity catalog indexes.
3. For staging, read [staging-and-cameras.md](references/staging-and-cameras.md). Bind body, hands, face and the actual camera reference for each take. Keep entrances and wins in separate selection pools.
4. Connect the character to the requested museum/versus scene. Preserve character-specific catalogs rather than expanding Jin/Kazuya ternaries or shared global arrays.
5. Read [project-and-rendering.md](references/project-and-rendering.md) only for this project's integration points, editor operation, HDRP materials, or source stage lighting.

## Verify the observable result

- Validate source/frame/bone/channel compatibility and finite normalized rotations. Reject mismatched resources with the character and move ID in the error.
- Inspect both hands and the mouth in representative poses; a closed fist, nonzero bone motion or a successful import alone does not prove correct deformation.
- Exercise each connected staging action through its full duration, including joins, source frame edits, hand/face synchronization, camera motion and subject visibility. Source camera files must affect the rendered path, not merely be loaded.
- Capture real rendered frames after skinning updates. Inspect screenshots for mesh occlusion as well as viewport bounds; an on-screen head can still be hidden by arena geometry.
- After every import, audit the materials and meshes against Jin and Kazuya:
  - no all-zero `*_MaskMap.png`;
  - no SpecularColor material ID without a specular map;
  - every alpha face overlay transparent (`*_eye_shadow`, `*_eye_corner`, `*_lip`), with its cooked diffuse still carrying alpha (Alisa's lip lost it);
  - eyebrows and eyelashes alpha-cut (0.25) or transparent, never opaque;
  - eyes tinted two-tone (iris versus sclera) with smoothness around 0.25;
  - roughly 0.55–0.8 vertices per triangle (3.00 means triangle soup and flat shading);
  - re-imported meshes really updated (the `CopySerialized` trap);
  - every `ATH_*_Btop` detach bone parented to the limb whose skin it drives, with limbs measured under a bent pose (Alisa's upper arm bowed with each elbow bend).

  See [project-and-rendering.md](references/project-and-rendering.md) and [character-pipeline.md](references/character-pipeline.md). After fixing one material, re-check skin separately from clothing: one fix has exposed a hidden mesh defect before ("costumes are ok, but the body skin is not ok now").
- Where runtime `Cloth` is added, keep skin off the cloth renderer. Check neck and thigh close-ups in the user's museum with dynamics running; captures with cloth disabled hide the defect.
- The user's verdict in their running scene ("Looks fixed") is the accepted final check. Report preview renders, JSON validators and log lines as not yet checked in their scene.
- Verify a fresh startup, skip-to-fight, winner playback and rematch cleanup when those flows changed. Avoid unrelated exhaustive tests once the changed behavior is sufficiently checked.
- Report exact coverage, remaining fallbacks and whether changes were only saved, committed locally, or pushed. This skill does not itself authorize GitHub publication.

## Session defaults to preserve unless changed by the user

Randomize one entrance per character at startup, avoid immediate repeats, and reserve victory actions for wins. Preserve smooth joins and readable front-facing shots while retaining each action's native camera movement and shot scale.

Outside the wardrobe, every scene (versus, `staging_test`, museum) dresses each fighter with one random item per slot from the items the user saved in the Wardrobe scene, ignoring preset costumes. The museum's costume list is replaced by a SAVED ITEMS list with a Shuffle button. Characters with nothing saved fall back to their first preset, with warnings. Details are in the wardrobe skill.

These are project preferences, not Tekken format rules.
