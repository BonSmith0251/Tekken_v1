---
name: tekken-character-battle-effects
description: Find, extract, decode and integrate a Tekken 7 fighter's original battle effects into the existing Unity remaster. Use for character hit effects, trails, projectiles, rage effects or character-effect manager bindings, with Jin as a verified starting example. Establish effect IDs and runtime triggers from original packages or observed original behavior; keep unsupported mappings explicit.
---

# Tekken Character Battle Effects

Recover the requested fighter's own effects and connect them to that fighter in the requested scene. Keep original asset evidence separate from Unity rendering adaptations and from gameplay-trigger evidence. The user's established preference is original-package truth: do not invent missing timing, bone attachments, effect meanings or move mappings. Keep extracted assets and reports local unless the user asks otherwise.

## Discover the current project

- Read `Tools/source-paths.json` in the Unity project, and inspect current runtime files and recent work before editing. The current project is `UnityRemaster/TekkenVersusRemaster`; its code and resources have moved to `Assets/_Project`. Rediscover paths rather than using the older `Assets/Remaster` layout.
- Review existing character-effect, common-effect and stage-effect code separately. `CascadeEffects` already renders some common/stage systems; that does not prove a character's own effects are integrated.
- Preserve other agents' work and the user's chosen scene. `UnityRemaster/CategoryBuild` may be stale; do not copy it over the live project. If a build copy is needed, make it from the current project and deploy only reviewed changes.
- Stop an active Unity Play session before changing its source files. Use the existing editor-state helper if available. Skill creation or asset analysis alone does not require stopping Play.

## Extract original evidence

Read [package-evidence.md](references/package-evidence.md) for the package relationships, Jin baseline and decoder boundaries. Run the bundled helper with explicit locations; its `--help` describes extraction options:

```text
python scripts/inspect_character_effects.py --workspace <workspace> --character jin --out <new-local-evidence-folder> --extract --include-unbound
```

`--extract` preserves original cooked bytes; `--include-unbound` also follows all particle systems inventoried in the character folder. Without the latter, extraction covers the manager's reference closure and the other systems remain inventoried candidates. `--include-common` adds common manager bindings without conflating them with character IDs. The script is a package inspector/extractor, not a Unity converter or a gameplay event decoder. Resolve the asset-workshop checkout and installed game through the project configuration or explicit `--game` and `--t7extract` arguments.

Inspect the character manager's default object, `Effects` records and referenced particle/Blueprint objects. Resolve full Unreal package paths and signed object indices. Record the archive, package SHA-256, export, property offset, effect ID and source reference. Inventory other particle systems in the character's original folder separately from manager-bound records. Follow shared and out-of-folder dependencies; do not restrict dependency lookup to the character directory.

Jin's observed manager binds `EffectID=0` to `PS_EF_CHR_JIN_NOMAL`. His folder contains additional systems. This proves neither that every attack uses ID 0 nor that a file named `unique1` belongs to a particular move. Recheck the installed packages each time rather than enforcing historical counts.

The current project also has a recovered native path: timed property `0x802E` dispatches effect ID 0 to the specified own bone. Read `Docs/JIN_BATTLE_EFFECTS.txt` and `Resources/CharacterEffects/jin/native-dispatch.json` before repeating that investigation. The inspected Jin moveset has 143 events across 51 moves. This establishes that specific path, not every collision-driven or customization effect.

The 0x802E case reads no character code, so it carries to other fighters once their own manager binds effect ID 0. Katarina (`ltn`) was done that way on 2026-10-27: `Tools/CharacterEffects/dispatch_character_manifest.py --character <code>` re-checks the native case, reads the moveset and VBN from `tkdata.bin`, writes the timed rows and the VBN bone map; `install_native_dispatch.py --character <code>` installs them. Read `Docs/KATARINA_BATTLE_EFFECTS.txt`. Kazuya, Xiaoyu and Alisa followed the same day; `Docs/CHARACTER_BATTLE_EFFECTS.txt` has all five and the per-fighter commands. The exporter now lists manager bindings outside the character folder as `unconvertedBindings` and tolerates a duplicated package name unless something references it; alpha-less `T_EF_COL_*` textures in character libraries keep their hue (`_MaskKeepsColour`), an adaptation, not recovered material behaviour. Older museum assets (Jin, Kazuya) store no character code: pass it to `CharacterEffectsShowcase.characterCode`. The showcase scene is now `Scenes/CharacterEffectsShowcase.unity` (renamed from `JinEffectsShowcase.unity`) with all five fighters; switch with Left/Right. Most of a fighter's 0x802E rows can sit in shared customization-item moves (`Co_I*`) that versus never reaches, so count the reachable moves separately.

## Establish when and where the effect plays

There are three different identifiers: character `EffectID`, common manager field (such as `HitFX`) and original move ID. Do not equate them.

Find dispatch evidence in the original Blueprint properties/bytecode and its native call references, decoded moveset/event records, or a controlled recording/trace of the original game. Retain the source frame convention, effect ID, target bone/socket, transform basis, actor ownership and stop condition. A string occurrence or asset dependency is a lead, not a proven trigger.

For each integration record, distinguish:

- **Package binding:** the original object points to this system or Blueprint.
- **Trigger verified:** original data or observed execution establishes this event's dispatch and parameters.
- **Unresolved:** the system exists, but trigger, attachment or rendering behavior has not been recovered.

Proceed with extraction, decoding and isolated rendering for unresolved systems. Do not fill gaps with guessed per-move bindings or a shared effect presented as character-specific. An isolated/manual preview can demonstrate an asset, but must not be reported as verified native battle playback. If the user explicitly requests an approximation, label its chosen behavior separately.

## Decode and wire into Unity

Read [unity-integration.md](references/unity-integration.md) before modifying the runtime. Reuse the current effect library and battle-event interfaces where appropriate; maintain a character-owned effect-ID registry so one fighter cannot accidentally use another fighter's effects.

Preserve original emitter order, LOD records, explicit enable flags, distributions, materials, textures, meshes, vector fields and custom modules in the evidence data. Missing serialized values are absent, not zero or an invented engine default. Keep unsupported modules and native data visible in the conversion report. Source values and hashes must survive any unit/basis conversion or rendering adaptation.

Use gameplay events for confirmed impact effects and the verified source-frame event stream for move-local effects. Resolve attachment transforms from that fighter's actual rig. Handle actor turns, both player slots, pauses, timeline jumps, interruptions, round reset and object cleanup. Do not spawn the same event on every render frame or let a looping effect survive its owner/action unintentionally.

## Validate the observable result

- Check original payload hashes, references and decoder bounds. Validate manager bindings separately from the larger folder inventory; report unresolved dependencies and raw/native fields.
- Confirm supported module conversion against source values. Keep particle caps, color/exposure changes, synthesized textures and omitted shader operations out of claims of exact original fidelity.
- Exercise the wired trigger and its negative case (for example, no impact on a whiff when that rule is proven). Verify the correct fighter, effect ID, source frame, position, orientation and cleanup. Use relevant existing battle harnesses rather than rerunning unrelated gameplay suites.
- Render close captures with both facing directions and moving attachments. For an original-fidelity claim, compare the same original event/move/frame against an original-game capture. A numeric harness pass alone is not visual equivalence.
- Report what was extracted, what was wired, what was tested and what remains unresolved. Distinguish manual preview, tested integration and user-confirmed appearance. Do not call an asset inventory a complete move-to-effect mapping.
