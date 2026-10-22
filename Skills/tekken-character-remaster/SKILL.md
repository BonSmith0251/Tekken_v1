---
name: tekken-character-remaster
description: Import, repair, and validate original Tekken character models, body and hand/facial animation, entrance and victory staging, and action-specific cameras in a Unity remaster. Use when extending this character/museum/versus pipeline to additional fighters or fixing its animation and camera integration; includes optional HDRP stage-lighting guidance.
---

# Tekken Character Remaster

Extend the existing project using each requested fighter's own source assets and events. This skill captures the tested Jin/Kazuya workflow; it is not evidence that every fighter uses their skeleton, channel layout, motion IDs, or coordinate mapping.

## Start with the requested scope

- Locate the Unity project, installed source game and extraction tools. Read `Tools/source-paths.json` if present; discover current paths rather than assuming the original machine layout.
- Inspect the working tree and current Unity editor/play state. Preserve existing museum, costume and render work. Stop an active Play session before script changes: this project's nonserialized battle state is lost on domain reload.
- For a named fighter, process that fighter. For an explicit request for all characters, inventory the available roster and process it incrementally; report coverage by character without presenting one successful fighter as roster-wide success.
- Follow the user's chosen scene, engine version and rendering pipeline. A new character does not require a pipeline migration, stage replacement or publishing operation.

## Build a character profile first

Read [character-pipeline.md](references/character-pipeline.md) for body, skin, hand and face conversion. Record source character code, animation prefix, moveset/archive paths, skeleton, costume assets, proven coordinate conversions, stance move, staging candidates and imported resource locations. Resolve these per fighter.

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
- Verify a fresh startup, skip-to-fight, winner playback and rematch cleanup when those flows changed. Avoid unrelated exhaustive tests once the changed behavior is sufficiently checked.
- Report exact coverage, remaining fallbacks and whether changes were only saved, committed locally, or pushed. This skill does not itself authorize GitHub publication.

## Session defaults to preserve unless changed by the user

Randomize one entrance per character at startup, avoid immediate repeats, and reserve victory actions for wins. Preserve smooth joins and readable front-facing shots while retaining each action's native camera movement and shot scale. These are project preferences, not Tekken format rules.
