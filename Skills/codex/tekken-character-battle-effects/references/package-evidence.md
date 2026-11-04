# Original package evidence

## Source locations and object relationships

This project uses the local `t7extract` checkout for cooked UE4 package and pak reads. `Tools/source-paths.json` records `asset_workshop`, `game` and the script-reference bundle. `t7extract.cli.Source` / `pak.PakSet` resolve effective entries with patch overrides. Record the contributing archive and disclose scan scope; the current top-level scan does not recursively include `~mods`.

Typical paths, to discover and verify per fighter:

```text
TekkenGame/Content/Effect/TK75/Character/jin/BP_jinEffectManager.uasset
TekkenGame/Content/Effect/TK75/Character/jin/Particle/*.uasset
TekkenGame/Content/Effect/TK75/Common/BP_CommonEffectManager.uasset
TekkenGame/Content/Effect/TK75/Common/BP_EF_KO.uasset
```

The character manager's `Default__..._C` object can serialize an `Effects` array containing `EffectID`, `ParticleSystem`, `Blueprint` and `BlueprintLifeSpan`, plus character `Tags`. Preserve the record as stored. `BlueprintLifeSpan` is not established as a particle-system lifetime. A missing or null Blueprint reference must remain missing or null.

Common manager defaults use named particle references such as `HitFX` and `GuardFX`; this is a different dispatch namespace. KO's `ParticleSystemComponent.Template` is a separate reference path. Neither implies a particular native trigger rule on its own.

## Reproducible Jin baseline

Observed in the installed packages on 2026-10-27:

| Evidence | Original value |
|---|---|
| Manager | `Effect/TK75/Character/jin/BP_jinEffectManager.uasset` |
| Default export | `Default__BP_jinEffectManager_C` |
| Character Tags | `JIN` |
| Explicit `Effects` records | one |
| `EffectID` | `0` |
| `ParticleSystem` | `/Game/Effect/TK75/Character/jin/Particle/PS_EF_CHR_JIN_NOMAL.PS_EF_CHR_JIN_NOMAL` |
| `Blueprint` | null |
| `BlueprintLifeSpan` | `1.0` |
| Manager package SHA-256 | `da319715aacd32e5d4852e2bb8122098fdaa3c6af529c9e46c0f950fa7f0625d` |

`NOMAL` is the original asset spelling. That system contains four stored emitters. The character folder audit found 18 ParticleSystem packages, including `PS_EF_CHR_JIN_unique1` through `unique4` and `ITM`-named systems. Folder membership and these names do not establish usage, move IDs, activation conditions or whether an asset is active in versus. Do not reduce the inventory to the one default record or assign the remaining systems by name similarity.

The broader local evidence bundle, if still present, is `Artifacts/BattleEffects`: `bindings.json`, `catalog.json`, `dependencies.json`, `evidence/`, and `decode-gaps.json`. It is useful as a cross-check, not a required dependency of this skill or a substitute for fresh original reads. The audit covered 1,421 particle packages under the Effect directory and exposed unsupported tags in `PS_EF_CHR_DVJ_beam`; those numbers are historical observations, not required constants.

## Reading the helper's report

`character-effects.json` lists inventories, manager defaults, dependencies and per-package evidence files. `packages/` contains the verified cooked payloads when `--extract` is used. Each evidence file preserves decoded export properties, property offsets/statuses, full import paths, package hashes and original archive identity.

Extraction success and decoding completeness are separate results. Exit code 0 and zero extraction `errors` mean the selected dependency traversal succeeded; they do not mean all properties or native code were decoded. Inspect `propertyStatus`, `taggedPropertyDecodingComplete`, `nativeExportTailsFullyDecoded` and each export's parse errors. The combined Jin/common validation extracted 357 packages with no unresolved selected dependencies, but still reported 230 property errors and 227 unsupported tags in the broader material/mesh/Blueprint closure. Jin's own particle packages had no reported tagged-property errors in that run. Recheck the current report instead of asserting these counts for another installation.

Dependency completeness applies only to the helper's selected import classes. It does not cover arbitrary native references, dynamic resource selection or shader execution. Aggregate property statuses cover the selected closure; an inventoried candidate outside that closure can have a separate evidence file. Treat nested data under error/unsupported/partial properties as untrusted until its structure is established. In particular, an unknown native struct can resemble a tagged list and yield nonsensical property types; a plausible-looking nested value does not rescue that decode.

## Recovered Jin dispatch and remaining investigation

Jin's `ParentClass` leads to `TekkenGame/Content/Effect/Character/cmn/BP_CharacterEffectManager.uasset`. In the inspected patch package its SHA-256 is `e95b7d920854d494e3c92f9e7f53f980fab475f22f45c073db09b4ac764caa70`. Its `BP_CharacterEffectManager_C` exports functions named `SpawnBone`, `SpawnWorld` and `OnHit`; the helper decoded empty tagged dictionaries for these functions and preserved unparsed export tails. It also imports `/Script/TekkenGame.TekkenCharacterEffectManager.SpawnBone` and `.SpawnWorld`; there is no matching native `OnHit` import in that package.

The project's `Tools/CharacterEffects/dispatch_bytecode.py` now decodes the inherited manager's 42 UFunctions with serialized/VM bounds and branch checks. `OnHit` reaches a return stub; it does not itself spawn Jin's effect. Native executable analysis then recovered visual property `0x802E` at `0x1407179A4`: effect ID 0, owner self, bone owner self, bone ID from the unsigned 16-bit property value. `dispatch_jin_manifest.py` found 143 matching original records across 51 moves. `dispatch_bone_attachment.py` verifies that native `PM_CharacterBone` replaces its packaged `SourceLocations[0]` from the `BoneIndex` parameter; existing NOMAL particles remain in world space under the verified constructor defaults.

The native skeleton loader chooses VBN for the original `mothead/nua/jin.vbn` header, and its lookup uses `28 + boneID * 76`. Jin's bone 7 is `R_Arm`, 9 `R_Hand`, 15 `R_Leg`, 19 `L_Foot`; these are not animation-channel indices. The VBN SHA-256 is `f889bf9aac5a27006e1a47fc4413228a41dfc0faea3b8fed83dc89ecbecec777`; the inspected executable SHA-256 is `f46f9ff526fe5d00a0d1ee52126f61628cdc13c402264e2764e4010d3b9c1892`. Do not reuse native addresses for a different executable hash.

Evidence is under `Artifacts/BattleEffects/JinUnityUpdate`: `jin-timed-dispatch.json`, `dispatch-bone-attachment-proof.json`, and `bone-id-jin-vbn-map.json`. `Tools/CharacterEffects/install_native_dispatch.py` checks matching hashes and current graph rows, then installs the character manifest. Read `Docs/JIN_BATTLE_EFFECTS.txt` for regeneration. Other collision paths, event kinds and item overrides remain unresolved. This is static package/executable proof, not an original-game playback comparison.

## Parsing rules that matter

- An Unreal object index is zero for null, negative for an import, positive for an export. Retain the index and resolve its full outer/package chain; identical short names can exist in different packages.
- `/Game/Effect/.../Asset.Asset` resolves to `TekkenGame/Content/Effect/.../Asset.uasset`. `/Engine/...` is a separate content root. Do not guess unresolved paths by picking the first matching stem.
- These packages have tagged properties even though their cooked UE4 file version is unversioned. Use the installed package reader's observed header layout, export bounds, property sizes and FName table. Keep unknown struct/native bytes and their offsets instead of silently replacing them with defaults.
- A decoded null ObjectProperty is valid data. Do not mark it as a decoder failure merely because its value is `None`.
- A `ParticleSystem` references `Emitters`; a `ParticleSpriteEmitter` references `LODLevels`; each LOD references `RequiredModule`, `SpawnModule`, `TypeDataModule` and `Modules`. Preserve all linked LODs, explicit flags and order. Export-table presence alone does not prove a module is linked or enabled.
- Distribution tables (`Op`, `EntryCount`, strides, bias/scale, `Values`) and curve objects are original data. Do not replace them with sampled midpoints in the evidence layer. Interpret only operations whose semantics have been established.
- Package imports establish dependencies, not necessarily active renderer use. Follow material instances and parents, textures, meshes, vector fields, effect Blueprints, and particle systems outside the character folder. Other runtime references can require further analysis beyond this dependency closure.

## Rendering boundaries

Existing `Tools/HitEffects/export_hit_effects.py` converts original data into a Unity-friendly library. Its current output is an adaptation: it samples curves, caps particle counts/rates, widens selected KO lines and omits or approximates some material/custom-module operations. Never use its converted `effects.json` as proof of untouched original values.

Use its reusable parser or converter only after inspecting the current implementation and recording the supported modules and adaptations. Cooked shader/native data may remain unresolved even when tagged material parameters decode. A decoded PNG is a source texture preview, not a render of the complete effect.

Keep original package bytes, decoded evidence and converted Unity outputs in separate locations. No web upload, public artifact or cross-account publication is part of this skill's default workflow.
