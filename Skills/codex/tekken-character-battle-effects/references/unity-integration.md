# Wiring character effects into this remaster

## Current integration points

Re-read these files before editing; the project is concurrently maintained. Paths below are relative to `UnityRemaster/TekkenVersusRemaster` and were inspected on 2026-10-27.

| File | Current responsibility |
|---|---|
| `Tools/HitEffects/export_hit_effects.py` | Common effect-manager role extraction and adapted Cascade-to-Unity conversion |
| `Assets/_Project/Resources/HitEffects/effects.json` | Converted common library, source hashes and limitations |
| `Assets/_Project/Scripts/Runtime/CascadeEffects.cs` | Loads a resource folder's library, resolves string roles, creates `CascadeInstance` objects and materials |
| `Assets/_Project/Scripts/Runtime/VersusGame.cs` | `OnHit`, `Contact`, `UpdateBattleEffects`, `ResetImpactEffects`, KO playback and actor attachment updates |
| `Assets/_Project/Scripts/Runtime/BattleModel.cs` | Gameplay state and `HitEvent` production |
| `Assets/_Project/Scripts/Runtime/CommandGraph.cs` and `CommandInterpreter.cs` | Original move graph and source-frame progression; inspect retained properties before adding a new event reader |
| `Tools/CommandGraph/` | Exporters, original-property data and headless command references |

The sibling Claude skill `~/.claude/skills/tekken-versus-battle` contains relevant battle rules and a compile/effects harness. Read it or its harness when changing this integration; do not assume all its heuristics are recovered original behavior. `tekken-character-remaster` covers rig import and animation when the effect work actually requires those layers. Stage ambient effects and wardrobe customization have their own workflows.

As inspected, `VersusGame.OnHit` chooses common Guard/Counter/Hit roles. `Contact` estimates impact placement from nearest striking bones and a defender body line. A damage threshold enables Pierce; floor and Rage effects use remaster state transitions. Those are existing remaster behaviors, not proof of a native character-effect dispatch map. Character-specific effect IDs need their own verified registry/event path.

`BattleModel.MoveProperty` currently exposes `Action<int,long,long>` for player, property ID and value. `VersusGame` subscribes through `OnMoveProperty`, which handles camera shake, follow-rate and cinematic properties, not character effects. There is no `PropertyTickEvent` in this inspected checkout; do not implement against that imagined API.

The legacy `MoveProperty` path has coverage gaps relevant to effect wiring:

- `Tools/CommandGraph/export_command_graph.py` exports the move's `extra_props` at offset `0x80` as triples `[rawFrame, id, value]`. The moveset reader also knows start/end property tables, but the current command-graph move schema does not retain them.
- `RaiseProperties` matches `(rawFrame & 0x3FFF)` to the current frame and ignores frames below 1. The event drops the raw frame flag bits, original move ID and graph owner.
- `BattleModel` steps the interpreter before reading the current move/frame. A transition can replace the acted move and reset its frame; properties on the outgoing final frame need a targeted check before this hook is relied upon. `CommandInterpreter` already exposes `ActedGraph`, `ActedMove` and `ActedFrame`; inspect and reuse their actual semantics rather than inventing another clock.

When proven effect events require these missing cases, extend the export/event contract to retain original provenance and acted-frame information. Test entry, final frame, transition, multi-frame advance and borrowed reaction moves. Do not silently treat this timed-extra-only hook as a complete original effect event stream.

Jin's recovered `0x802E` path now uses `BattleModel.ActedMoveProperty`, carrying actor, graph, move, acted frame and raw flags before a cancel replaces them. `VersusGame` queues those events until actors are posed; `CharacterBattleEffects` resolves numeric ID 0 and the native bone map in `CharacterEffects/jin/native-dispatch.json`. Both player slots have separate owners and round reset clears them. Existing camera/logic hooks retain their old behavior. Multiple fixed ticks preserve every event but resolve at the next displayed pose; exact attachment placement across skipped rendering frames/transitions remains a presentation limitation.

`CharacterEffectsShowcase` (renamed from `JinEffectsShowcase`, scene `CharacterEffectsShowcase.unity`) holds all five fighters and switches between them; `Temp/capture-katarina-action-effects` captures her reachable effect moves into `Validation/KatarinaEffects`. The Claude battle harness `run.sh CharacterEffects` (`CHARACTER_EFFECTS_CODE`, default `ltn`) forces every manifest row in both slots. `JinEffectsShowcase.unity` demonstrates original moves 1779/1840 and separately browses all 18 manual candidates. Its action captures check no trigger at frame 5 and one at frame 6 in both facings. `JinEffectsValidation` checks actual particle Custom2 atlas indices, exact bone spawn, world-space persistence, cleanup, and common/stage compatibility. `Tools/CharacterEffects/test_property_events.py` checks final-frame/borrowed ownership, duplicates and unchanged gameplay state. Captures bake temporary visual mesh snapshots because synchronous HDRP previews otherwise cache an old skin pose. These passes do not establish visual parity with the original.

## Conversion layer

Keep original extraction in a reference/evidence folder. Write converted data to a character-owned library, for example `Resources/CharacterEffects/jin/`, or extend the existing format with a character/effect-ID namespace. Reuse the actual serializer/runtime conventions rather than adding a competing global renderer.

`CascadeEffects.Create(parent, folder, name)` can load a different resource folder and `Play(role, worldPosition, rotation, scale, bones)` instantiates a system. It currently loads the shared particle shader from `HitEffects/CascadeParticle`. Its role lookup is string-based; preserve numeric original IDs in an explicit character registry rather than treating role strings as native IDs. Use full package identity in the evidence even if Unity resources need safe local names.

Audit the current exporter and runtime before reusing them:

- The conversion changes centimetres and axes for effect space. Its convention is not automatically the same as the character mesh converter. Establish each axis/bone-local basis with a directional test and both fighter facings.
- The exporter header's half-extent description is stale in the inspected checkout. Read the actual conversion body and the full-size convention recorded in `Docs/BATTLE_EFFECTS.txt`; do not introduce another factor of two from that header.
- The exporter samples distributions and gradients, caps some bursts/rates, widens some KO meshes and skips unsupported modules. The runtime adjusts intensity/exposure. Preserve these as explicit adaptations, not original constants.
- `PM_CharacterBone`, GPU type data, vector fields, dynamic material parameters, refraction, collision and Blueprint logic can need additional support. A module appearing in the JSON does not prove the renderer implements it.
- Material imports and parameter names do not establish the full cooked shader program. Do not invent missing additive tint, alpha, distortion strength, or replacement sprites and call them extracted assets.

The current renderer now supports Jin's explicit linear SubUV grids and index curves through Custom2.x and shader tile blending, plus explicitly serialized local simulation. Legacy common/stage exports without the interpolation contract keep their existing UV behavior. Three Random SubUV emitters are omitted, not shown as a false tile-0 reconstruction; the missing `Head_Socket` item emitter is also explicit. Native `BoneIndex` overrides the cooked Neck source for verified dispatch, while manual preview retains the packaged fallback. Do not parent existing NOMAL particles to the animated bone.

Produce a support ledger per system: source module/material, reference and source value; supported conversion; adaptation if any; unresolved behavior. Leave raw source floats, curve tables and original hashes in evidence even when runtime data is sampled or transformed.

`StageEnvironment` also uses `CascadeEffects`. A shared renderer or shader change needs a focused stage-effect regression check as well as the character-effect check.

## Event and attachment contract

Choose the narrowest existing hook that matches proven source behavior. A useful event record identifies:

```text
character code + original effect ID or common role
original move ID / action identity, if applicable
event kind and source frame, with frame-number convention
owner / target and bone or socket
local/world position, orientation and scale basis
start / stop / interruption / loop policy
source package or moveset hash + property offset or trace reference
verification status and explicitly unsupported fields
```

This is an integration checklist, not an assertion that every original event stores all these fields. Preserve absent fields; derive behavior only when the applicable engine/native semantics are established.

- Impact effects should subscribe to resolved gameplay impact events when that dispatch is verified. A move animation reaching a frame does not by itself prove an impact occurred.
- Move-local effects should follow the animation's source-frame progression. Handle a frame crossing once, including a jump over several frames; define pause, seek, looping and reverse-preview behavior. Do not use render-frame equality or add a duplicate AnimationEvent path.
- Resolve bones on the actual owning fighter, including the other player slot and randomized wardrobe rigs. Never reuse Jin's transform handles on another character or rematch instance.
- Use confirmed local/world semantics for following particles and source travel. Avoid multiplying model scale twice or reflecting the effect twice when facing changes.
- Stop owned loops and detach/clear instances on the relevant action interruption, round reset, scene unload and character replacement. Preserve a proven effect that intentionally survives its action.

When native triggers are still unknown, a manually selectable character-effect preview is useful for decoder/render testing. Label it as a preview and keep its arbitrary playback button out of the native-trigger manifest. Do not report this as completed battle wiring.

## Acceptance evidence

Use the existing effects/gameplay harness for event counts and cleanup when those paths change. Add focused checks only for unresolved correctness risks: duplicate spawns on a frame skip, a wrong owner after rematch, missing referenced assets, or an invalid attachment. Successful matches are not a test of native timing.

The current `Assets/_Project/Scripts/Editor/HitEffectsCapture.cs` writes common-effect captures to `Validation/HitEffects`; extend its explicit cases for a new character library. With the project already open, use its existing `Temp/refresh-assets` editor workflow, wait for compilation, then submit the appropriate capture job flag. Inspect that helper before use; do not launch a second batch Unity process on the open project.

Capture the fighter and effect together at spawn, peak and termination, both facing directions, and while moving. Check that the actual source textures, mesh and material parameters are used. Compare against the original game's same event before claiming visual parity. Record what was manually selected versus natively dispatched and what the user has actually reviewed.

Deploy only changed assets/scripts from a current build copy, with backups or a reviewable diff. Never run old `CategoryBuild` whole-project deployment scripts over newer Claude work. Report the exact scene and character library updated; installation of this skill alone does not modify live battle behavior.
