# Unity integration and validation

All paths below are project-relative unless marked otherwise.

## Existing implementation

- `Tools/HitEffects/export_hit_effects.py` decodes the selected manager roles and KO alias. It writes directly to `Assets/_Project/Resources/HitEffects`; it has no output-directory CLI argument. Inspect current output before regenerating. For an isolated conversion, use a temporary project-shaped copy of the exporter or add an explicit output option deliberately.
- `Assets/_Project/Resources/HitEffects/effects.json` contains roles, systems, original system hashes and conversion limitations. Textures and meshes sit alongside it. The JSON is adapted output, not the raw original schema.
- `Assets/_Project/Scripts/Runtime/CascadeEffects.cs` provides `Create`, `Has`, `Play` and effect instances with `SimulateStep`/`Stop`; `Resources/HitEffects/CascadeParticle.shader` is shared by common, character and stage libraries.
- `VersusGame.cs` consumes `BattleModel.Hit` in `OnHit`; `AfterTick` and `UpdateBattleEffects` handle KO, floor and Rage state. `Contact` estimates hit location from current rig bones. `ResetImpactEffects` stops aura state, clears character queues and, since 2026-10-27, clears finite common instances through `CascadeEffects.Clear`.

Regenerate from project root only when conversion changes are intended:

```text
python Tools/HitEffects/export_hit_effects.py --source <source-containing-TekkenGame> --t7extract <asset_workshop>
```

The exporter can silently leave a role without a system if a dependency is absent. Run the audit afterward. It also flattens only a selected set of properties: preserving a field in its intermediate dictionary does not prove it reached JSON or the renderer.

## Current trigger choices, not native dispatch proof

This table describes legacy Unity code for review; it is not an implementation specification for original fidelity. Follow `original-behavior.md` before adopting or changing a rule. Compilation and Unity playback alone cannot verify these choices against the original game.

| Remaster event | Current selection |
| --- | --- |
| Resolved blocked hit | GuardFX |
| Resolved unblocked counter | CriticalHit3FX |
| Other resolved hit | HitFX |
| Unblocked damage >= 20 | Nothing since 2026-10-27 (PierceFX threshold was a guess; not seen in the reference) |
| `hit.punish` | Nothing since 2026-10-27: the practice crosshair is not played in versus. The blue ring once matched to it in the reference video is Hwoarang's `PS_EF_CHR_HWO_nomal` (character effect ID 0) |
| Fight ends, result not TIME UP | Nothing extra since 2026-10-27: nine reference KOs show only the blow's hit/counter effect, never PS_EF_SpecialKO |
| Newly downed | Nothing since 2026-10-27: solid-floor landings/knockdowns in the reference show no ring or dust |
| Alive in Fight at <= quarter health | RageFX once, looping RageAuraFX, and the red fresnel rim `RageRim` (the original's visible Rage aura) |

Rage Art consumption is not implemented in this path. Aura stops when the Rage predicate ends; both Rage instances are moved with the spine while active. Impact position is a nearest-striking-bone/body-line estimate, not recovered original effect hitboxes. Keep these distinctions when documenting accuracy.

`commonBattleEffects` is followed live: `VersusGame.SyncCommonEffects` (start of `UpdateBattleEffects`) loads the library when it is ticked during Play and removes the library and its instances when it is unticked. Since 2026-10-28 it lives in `Resources/BattleEffectTuning.asset` (on since the user re-enabled common effects on 2026-10-27), not in the scene. LocalVersus2 is the only versus scene; the first LocalVersus scene was retired on 2026-10-27. When common effects are off, the old impact sphere is also suppressed; audio, shake and the KO near-freeze continue independently. Do not gate character effects with this switch or route character ID 0 into HitFX.

## Rendering boundaries

The current converter uses centimetres to metres and maps UE local +X to Unity local +Z. Sprite sizes use `*.01` as full sizes; mesh values are scales. The exporter header still claims half-extents, contradicting its conversion code and project notes. Do not double sizes based on that stale header. Verify locked-axis treatment against actual source records before generalizing the converter's zero-to-X substitution.

Record approximations separately: eight-sample gradients, linearized curves, particle/rate caps (600; large smoke 60/s), vector-field noise/drag substitute, glare/exposure shoulder and fitted AlphaClip erosion (KO ray widening and the punish glyph only matter in previews: versus stopped playing KOFX and PunishNotice on 2026-10-27). Refraction-only emitters, GPU floor collision and some dynamic material operations are omitted. Inspect `effects.json.limitations`, code and raw modules together; the limitations list is not necessarily exhaustive.

`CascadeEffects.Linear` compensates Unity color conversion in this project; arbitrary extra gamma correction can change orange to red. The shader's material cache keys, color/exposure controls and masks are shared, so scope adjustments carefully. New SubUV behavior requires the explicit interpolation contract; merely seeing an atlas grid in old common/stage data must not activate a new UV path. Unsupported modes need explicit reporting.

## Observable verification

The menu **Remaster > Rendering > Capture battle effects** (`Scripts/Editor/HitEffectsCapture.cs`) renders ten selected roles at several ages into `Validation/HitEffects`. The edit-mode flag is `Temp/capture-hit-effects`; errors go to `Temp/capture-hit-effects-error.txt`. This is an isolated asset preview, not all-role coverage or an original dispatch test. Inspect image files, not just the log. For animated captures, the newer `CharacterEffectsShowcaseSetup.cs` demonstrates baked-mesh snapshots to avoid stale skin matrices and resetting HDRP preview light intensities on every render.

For changed runtime behavior, use a deterministic versus replay or focused harness. Check: whiff produces no impact; blocked contact uses guard; normal/counter routing follows the actual outcome (punish, floor, Pierce and KO triggers were removed from versus on 2026-10-27; re-add only with original evidence); multi-hit contacts do not repeat every render frame; both actors and facings attach correctly; pause and the KO near-freeze (1% game speed) use game time, so effects freeze with it; Rage and finite effects do not leak across round/scene reset. Check the disabled-common setting as well as enabled behavior.

Compile relevant assemblies after C# or shader changes. The existing `JinEffectsValidation.cs` includes representative common/stage renderer checks but is not a comprehensive common-versus trigger suite. Use local close captures and an actual running versus scene to show the result. A preview of an original texture is not proof that its blend, timing or shader matches the original game.
