---
name: tekken-versus-common-effects
description: Recover and integrate Tekken 7 shared versus effects from original game archives, native dispatch evidence and original-game observations. Use for common hit, counter, guard, KO, floor and Rage effects, BP_CommonEffectManager bindings and fidelity checks. Re-read original assets before trusting cached reports or Unity behavior. Fighter-owned effect IDs belong to tekken-character-battle-effects.
---

# Tekken Versus Common Effects

Work from original packages and verified runtime evidence. Separate package bindings, original dispatch evidence, current remaster behavior and rendering approximations. The user's requirement is original asset truth; a plausible effect name or existing Unity implementation does not establish original gameplay behavior. Keep assets, evidence and previews local unless requested otherwise.

Scope: the shared battle effects (hit, guard, rage and the other `BP_CommonEffectManager` roles). The ROUND/READY/FIGHT/K.O. calls are HUD work, not effects: use `Docs/BATTLE_HUD.txt`, `GfxClip`/`GfxLayer` and the `Temp/capture-announcements` flag (Claude skill `tekken-versus-ui`).

## Locate and audit

The current workspace is `D:/games/Tekken.7.Ultimate.Edition.v5.10`; the project is `UnityRemaster/TekkenVersusRemaster`. Read its `Tools/source-paths.json` and `Docs/BATTLE_EFFECTS.txt`, then inspect current source before editing. Paths in the references are relative to this workspace or project as specified. Do not replace the live project with `UnityRemaster/CategoryBuild`.

Read [original-evidence.md](references/original-evidence.md) for extraction, provenance and coverage. Start asset recovery or fidelity work by re-reading the installed original archives, without touching Unity:

```text
python <skill>/scripts/verify_original_common.py --workspace <workspace> --cached-bindings Artifacts/BattleEffects/bindings.json --out <new-local-evidence-directory>
```

This verifies stored payloads and freshly decodes property names, offsets, object references and target exports. Cached bindings are optional comparison input, never the source. It uses the bounded decoder from the installed character-effects skill; see the reference for overrides and archive-selection limits. If original archives are unavailable, identify the work as a cached-evidence audit; do not claim current-original verification.

The separate `audit_common_effects.py` checks cached extraction consistency and Unity resource coverage only. Neither helper establishes when an effect plays or how it looks in the original game.

## Establish the requested behavior

- Common-manager roles are distinct from character `EffectID` values and original move IDs. Keep their registries and dispatch paths separate. Jin's recovered `0x802E` path is character effect ID 0, not a common hit effect.
- Inspect the common manager defaults, referenced systems and dependent Blueprints. Follow dependencies outside the Common folder. Trace native dispatch or a controlled original recording for trigger conditions, owner, attachment, direction, timing, lifetime and stop behavior.
- For each proposed binding, record the evidence source and whether it is package-confirmed, trigger-confirmed, an existing remaster adaptation or unresolved. Preserve unsupported values and raw bytes; do not synthesize native behavior from a field name.
- Unresolved systems can be extracted and shown in a labeled manual preview. Do not enable an unproven wall, armor, throw or Rage Arts trigger simply because the asset exists.

Before adding or changing automatic combat dispatch, use [original-behavior.md](references/original-behavior.md). Require evidence for the particular rule being implemented, including mode, condition, timing and attachment. This applies to existing common effects too: the impact-position estimate and Rage behavior are remaster choices (the Pierce damage threshold and punish selection were removed from versus on 2026-10-27) until independently established. Do not promote them to original rules through reuse. An explicitly requested approximation is permitted when labeled separately.

## Integrate within the requested scope

Read [unity-integration.md](references/unity-integration.md) before changing export, runtime or rendering code. Reuse the existing common library and battle outcome interfaces. Changes to `CascadeEffects` or `CascadeParticle.shader` also affect character and stage effects.

Common effects were disabled at the user's request on 2026-10-27 using `VersusGame.commonBattleEffects`, then re-enabled in `LocalVersus2` the same day at the user's request, alongside character effects. Preserve the current scene/default setting for audits and repairs. If the user requests enabling common effects, that request authorizes enabling them in the requested scene; do not demand extra confirmation. Since 2026-10-28 the switch lives in `Resources/BattleEffectTuning.asset` (`commonBattleEffects`, plus the `hitEffectScale` and `glareGain` sliders; ORIGINAL and DEFAULTS presets; F6 panel in Play), not in the scene: inspect that asset. `LocalVersus2` is the only versus scene (the first `LocalVersus` was retired 2026-10-27); the flag `Temp/localversus2-common-effects` "on"/"off" writes the asset, and the switch is followed live in Play. Skill creation alone changes no runtime state.

Stop an active Play session before source edits, but leave Play alone for read-only analysis or skill work. Target the correct Unity project; another editor may be open. An unchecked Win32 Play menu has falsely reported stopped in this environment: confirm actual editor state before toggling. Preserve dirty scenes and unrelated ongoing work.

## Verify and show

Check export coverage and raw-source provenance separately from runtime behavior. Exercise the requested common event, its negative case, both players/facings, pause/KO slow motion, repeat contacts and round restart. Validate continuous Rage cleanup as well as finite particles. Avoid changing battle outcomes merely to make an effect appear.

Use the existing isolated capture menu for asset previews and a Unity versus replay to check implementation. Original dispatch needs original code/data or original execution evidence; a Unity replay cannot establish it. Shared shader changes need representative common, character and stage renders. Compare matched original and remaster events before claiming appearance or timing parity; a package hash or harness pass is insufficient.

Deliver local captures or a local gallery with a concise result: what was recovered, what was wired, what was observed and which behaviors remain unresolved. Report incomplete coverage explicitly; the currently exported set is not all common battle effects.
