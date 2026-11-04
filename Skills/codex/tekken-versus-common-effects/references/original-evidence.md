# Original package evidence

## Sources and extraction

Use `scripts/verify_original_common.py` first for fresh original-source verification. It reads the configured installed pak archives with payload verification, decodes the exact `Default__BP_CommonEffectManager_C` export, resolves full particle references and verifies the referenced exports. It extracts manager/direct particle bytes and writes `manager-defaults.json` and `original-common.json` into a new directory. No Unity data is used to discover bindings. It does not extract the full dependency closure, inspect every emitter or decode gameplay triggers.

The helper imports the bounded `Properties` decoder from the sibling installed `tekken-character-battle-effects/scripts/inspect_character_effects.py`; use `--decoder <path>` for a staged skill or alternate install. It resolves `game` and `asset_workshop` from project configuration, with `--game` and `--t7extract` overrides. `--cached-bindings <bindings.json>` compares historical roles, names, offsets, indices, package paths and hashes against the freshly decoded result and fails on disagreement. Omit it when establishing a baseline for another build.

Its report records decoder/extractor hashes, archive paths/sizes/timestamps, selected package offsets, verified stored SHA-1 values and decoded payload SHA-256 values. PakSet scans top-level paks and uses base-then-`_P` sorting; it does not establish the original engine's runtime mount order. Nested paks are reported as excluded. Inspect archive/mod configuration before claiming a clean original baseline, and establish actual mount precedence when it could change a relevant package. Do not describe installed bytes as publisher-authenticated merely because their stored hash matches.

Workspace-relative evidence is under `Artifacts/BattleEffects/`: `bindings.json`, `manager.json`, `catalog.json`, `evidence/`, `packages/`, `decode-gaps.json` and `dependencies.json`. The local HTML artifact is `index.html`. These are prior observations, not an automatically refreshed view of the installed game.

The manager is `TekkenGame/Content/Effect/TK75/Common/BP_CommonEffectManager.uasset`. The inspected package came from `pakchunk0-WindowsNoEditor_P.pak`; its SHA-256 was `5aa6365030ecc410c0a345ee07c486155a3f145c69032e420f94dd95854bade4`. Recheck current packages and patch precedence instead of enforcing that hash on another build.

Resolve `game` and `asset_workshop` from the project's `Tools/source-paths.json`. From the extractor checkout (or with its module import path configured), the existing extraction commands are:

```text
python -m t7extract unpack <game> "*/Effect/TK75/Common/*" -o <new-source>
python -m t7extract unpack <game> "*/Effect/TK75/Character/mrx/Mesh/SM_EF_MRX_*" -o <new-source>
```

These patterns seed extraction; they are not the full dependency closure. Manager fields also reference `Character/cmn` and `Character/mrz`. Resolve full package paths and signed object indices, follow texture/material parents, meshes, vector fields and Blueprint dependencies, and retain archive provenance. Never resolve duplicate basenames by silently selecting the first match.

`Artifacts/BattleEffects/analyze.py` contains the bounded `EvidenceProps` reader and full object-reference chain resolver. Inspect before reusing: it has workspace-relative paths and writes into the existing artifact. Prefer a new evidence directory for new investigations. Its `validate.py` has historical count assertions, so it is not a generic validator for a changed game build.

For each manager binding retain role, original package path, package SHA-256, owning export, property tag/value offset, signed object index and resolved target. Retain all LOD records and enable flags separately from the subset rendered in Unity. Unsupported native structures must remain explicit, not disappear as null values.

## Observed coverage, 2026-10-27

There are 47 recorded common-manager fields. Several fields share the same system, so this is not a count of unique effects. The Unity library exports 12 of these fields plus a separately sourced `KOFX` alias: 13 systems with 71 serialized emitter records. Exported emitters may still be skipped at runtime. Recompute counts after changes.

| Role | Bound system |
| --- | --- |
| HitFX | PS_EF_cmnhit00_ts |
| CriticalHit3FX | PS_EF_cmncritical00_ts |
| GuardFX | PS_EF_cmnguard01_ts |
| PierceFX | PS_EF_cmnhit_line00 |
| BlastFX | PS_EF_cmndown00_ts |
| SmokeFX | PS_EF_cmnsmoke00 |
| RageFX | PS_EF_cmnragestart00_ss |
| RageAuraFX | PS_EF_cmnrageaura_ts |
| PracticePunishNoticeHiFX | PS_EF_PunishNotice_hi1 |
| PracticePunishNoticeMidFX | PS_EF_PunishNotice_Mid |
| PracticePunishNoticeMidSpFX | PS_EF_PunishNotice_MidSP1 |
| PracticePunishNoticeLoFX | PS_EF_PunishNotice_low |

`KOFX` is an exporter alias for `PS_EF_SpecialKO`, attributed to `BP_EF_KO`; it is not a field in the observed common-manager defaults. Verify the Blueprint reference independently before describing its dispatch as recovered. The audit checks the KO system bytes against the catalog, not this Blueprint's execution.

Unexported fields include CriticalHit4/5, special guards, Seize, wall impacts, SpecialBlast, homing effects, Armor0, Charge, practice indicators, Burn, Lightning, Rage Arts/Attack and damage-number effects. Their names establish neither exact conditions nor applicability to normal versus. CriticalHit3/4/5 currently share a system; that does not mean their native thresholds are identical.

## Audit helper contract

`scripts/audit_common_effects.py` takes `--workspace`, `--out`, optional `--project`, `--evidence` and repeatable `--package-root` absolute or workspace-relative paths. It reads only inputs; only a new explicit report is written (existing reports are not overwritten). It verifies manager/target hashes, stored binding indices, exported source hashes, role matches and referenced PNG/mesh presence. It lists uncovered manager fields and non-manager aliases separately. It fails on mismatches or missing required files, not merely on intentionally incomplete export coverage.

The initial `Artifacts/BattleEffects/packages` extraction lacks some out-of-folder common-manager dependencies. The current fuller extraction can supplement it with `--package-root Artifacts/BattleEffects/JinSkillValidation/v3/packages`. All matching copies are hash-checked; conflicting snapshots fail rather than choosing one silently. On another installation, supply the freshly extracted dependency root. Missing packages should remain visible in the report.

A passing report proves consistency with the recorded extraction, not current archive freshness, complete decoding, native triggers or renderer parity. Record original captures with exact file/timestamp and event context if using them to establish behavior. A historical document saying a video matched is not a substitute for inspecting that video.
