---
name: tekken-versus-battle
description: Work on the UnityRemaster versus battle scene (LocalVersus2; the older LocalVersus was retired 2026-10-27) - the Tekken 7 fighters' original command graphs on the keyboard, hits and reactions, facing, falls and getups, KO and round flow, victory poses, the fight camera and impact effects, hair/cloth in fights, CPU play, original stages in versus (the HUD, round calls, character select and VS screen belong to tekken-versus-ui), and adding another fighter to versus. Use this whenever the user reports something that feels off in a versus match ("they face the wrong way", "the loser shakes after the round", "Jin slides when he walks", "the camera is jerky", "the winner pose is wrong", "the fallen character gets pushed"), asks to wire or match moves, inputs or combos to the original game, asks for camera or KO effects like the original, or wants Jin, Kazuya, Xiaoyu or a new character playable in LocalVersus2, even when they only paste a video or a screenshot of the match.
---

# Tekken versus battle scene

The versus scene of the Unity remaster at `UnityRemaster/TekkenVersusRemaster` (workspace
`D:\games\Tekken.7.Ultimate.Edition.v5.10`; Unity 2023.2.22f1, HDRP 16). Confirm paths before use;
they are hints. Jin (`jin`), Kazuya (`kaz`), Xiaoyu (`xia`) and Alisa (`asa`, added 2026-10-23,
profile `Tools/alisa-profile.json`) and Katarina (`ltn`, added 2026-10-25, `Tools/katarina-profile.json`,
id 21 by roster order only, unconfirmed, since her own moves never test it; prefix `Ln_`, stance 1419) and Lidia (`nsd`, added 2026-10-29,
`Tools/lidia-profile.json`, id 56 by roster order, prefix `Nd_`, stance 1422; DLC, so her staging tracks and
Rage Art cameras sit in the MOTA banks inside her moveset), Bob (`bob`, added 2026-10-29, `Tools/bob-profile.json`, id 31, prefix `Ft_`, stance 1443) and Leo (`leo`, added 2026-10-29, `Tools/leo-profile.json`, id 17, prefix `Un_`, stance 1436) are playable with the game's own movesets. The `VersusGame`
code default is Jin versus Xiaoyu; the saved `LocalVersus2.unity` has Alisa (P1) versus Katarina (P2),
but it opens on the character select screen (`VersusGame.characterSelect`, default true), whose
picks override the scene's. To set the scene's pair, write two names to `Temp/setup-localversus2`
(`Scripts/Editor/LocalVersus2Setup.cs`, e.g. `Katarina Xiaoyu`). It sets players, actions and
prefabs, saves only when the scene has no unsaved edits, and writes
`Validation/localversus2-fighters.json`.

The user keeps Unity open and tests each fix themselves. Work **one issue at a time**:
reproduce it headlessly, fix it, measure it, compile-check, then hand over with a short list of
what to check in the scene. Never batch several fixes into one untested change.

## Map

| Piece | Path (under the project) |
|---|---|
| Scene driver: input, camera, KO/round UI, victory, entrance, dynamics | `Assets/_Project/Scripts/Runtime/VersusGame.cs` |
| Shared effect tuning every battle reads (common effects, hit shake, camera smoothing, trade, KO) and its panel (F6; scene `BattleEffectTuning.unity`, made by `Scripts/Editor/BattleEffectTuningSetup.cs`, flag `Temp/create-effect-tuning-scene`) | `Scripts/Runtime/BattleEffectTuning.cs` → `Resources/BattleEffectTuning.asset`; `Scripts/Runtime/VersusGameTuningPanel.cs` (partial VersusGame); `Scripts/Runtime/BattleEffectDemo.cs` directs the exchanges (hit, counter, guard, trade, KO, Rage Drive KO) by probing presses on a scratch `BattleModel`; `VersusGame.effectDemosOnly` idles the fighters |
| Pure C# battle model: movement, facing, separation, hits, rounds | `Scripts/Runtime/BattleModel.cs` (`FighterState`, `HitEvent`, `MatchRules`) |
| Original command interpreter (cancels, buffer, requirements, borrow) | `Scripts/Runtime/CommandInterpreter.cs` |
| Exported command graph classes and loader | `Scripts/Runtime/CommandGraph.cs` → `Resources/CommandGraph/<code>.json` |
| CPU: replays movelist demos, guards, original getups | `Scripts/Runtime/CpuPlayer.cs` |
| Clip playback, crossfade, root-motion remap | `Scripts/Runtime/FighterActionMotion.cs`, `FighterActionCatalog.cs` |
| Model transform, heading easing, hit flash | `Scripts/Runtime/JinRig.cs` (used by every fighter) |
| Rage Art look-at cameras, shake presets | `Scripts/Runtime/FightCameraTrack.cs` → `Resources/FightCameras/<code>_<track>.bytes`; `BattleCameraEffects.cs` ← `Resources/CameraEffects/catalog.json` (native shake 0x8001-4/0x8006-9 → presets 3-6, 0x8005/0x800A → min(value,9)); exporter `Tools/CameraEffects/export_camera_effects.py`; demo `Scenes/BattleCameraShowcase.unity`; notes `Docs/BATTLE_CAMERA_EFFECTS.txt`; Codex skill `~/.codex/skills/tekken-versus-camera-effects` (read manually) |
| Entrance / victory takes | `Scripts/Runtime/LocalVersusEntrance.cs`, `VictoryStaging.cs`, `StagingCatalog.cs` |
| Hair and cloth in fights | `Scripts/Runtime/WardrobeDynamics.cs` (`XiaoyuSecondaryDynamics` only as a no-wardrobe fallback) |
| Battle sound and music (voices, impacts from each move's own sound list, announcer, footsteps, stage BGM with the final-round switch, subtitles; original Wwise banks) | **See the `tekken-sound` skill.** `Scripts/Runtime/BattleSound.cs`, `BattleMusic.cs` ← `Resources/BattleSound` / `Resources/BattleMusic` from `Tools/BattleSound/export_battle_sound.py` / `export_battle_music.py`; hooks in `VersusGame` (`PlayHitSound`, `PlayMoveSound`, `Announce`, `VoiceEntrance`, `SayTake`, `UpdateWinnerCall`); notes `Docs/BATTLE_SOUND.txt`. Harnesses `Sounds` (coverage) and `SoundEvents` (property counts) |
| Original battle effects (hit, counter, guard, floor, Rage) | `Scripts/Runtime/CascadeEffects.cs` ← `Resources/HitEffects/effects.json` from `Tools/HitEffects/export_hit_effects.py`; triggers in `VersusGame.OnHit` / `UpdateBattleEffects`; notes `Docs/BATTLE_EFFECTS.txt` |
| Effect preview (flag `Temp/capture-hit-effects`) | `Scripts/Editor/HitEffectsCapture.cs` → `Validation/HitEffects/<role>-<ms>.png` |
| Character's own effects (native move property 0x802E → manager effect ID 0 on an own bone; all five fighters since 2026-10-27) | `Scripts/Runtime/CharacterBattleEffects.cs` ← `Resources/CharacterEffects/<code>/{effects,registry,native-dispatch}.json`, fed by `BattleModel.ActedMoveProperty` → `VersusGame.OnActedMoveProperty`; tools `Tools/CharacterEffects/` (`dispatch_character_manifest.py`, `install_native_dispatch.py --character`); notes `Docs/CHARACTER_BATTLE_EFFECTS.txt` (all five, regenerate steps), `Docs/JIN_BATTLE_EFFECTS.txt`, `Docs/KATARINA_BATTLE_EFFECTS.txt`; extraction is the Codex skill `~/.codex/skills/tekken-character-battle-effects` (read it manually). Showcase scene `Scenes/CharacterEffectsShowcase.unity` (all five, ←/→ switches; was JinEffectsShowcase), check `Temp/check-character-effects-showcase`. Captures: `Temp/capture-jin-action-effects`, `Temp/capture-character-action-effects` (Kazuya, Xiaoyu, Alisa, Katarina → `Validation/<Name>Effects`) |
| Battle HUD, round calls | `Scripts/Runtime/BattleHud.cs`, `GfxClip.cs` and the `Tools/BattleHud` exporters; see the `tekken-versus-ui` skill |
| Character select → VS loading screen | `Scripts/Runtime/CharacterSelect.cs`, `VersusLoadingScreen.cs`; see the `tekken-versus-ui` skill |
| Scene look: lights, ambient, background, shadow volume | "Render Settings" object (`Scripts/Runtime/StagingRenderSettings.cs`, `Rendering/StagingTestVolume.asset`) |
| Exporter, Python reference, demo oracle | `Tools/CommandGraph/*.py`, `References/CommandGraph/<code>-movelist.json` |
| Unity validation (flag `Temp/validate-command-graph`) | `Scripts/Editor/CommandGraphValidation.cs` → `Validation/CommandGraph/command-graph-unity.json` |
| User docs (controls, regenerate commands) | `Docs/ORIGINAL_COMMANDS.txt`, `Docs/LOCALVERSUS2_ENTRANCE.txt` |

The original-game semantics behind the interpreter are in
`references/battle-model.md`. Read it before touching cancels, requirements, reactions, facing
or properties.

## Tools bundled with this skill

- `harness/compile_check.sh [project]` compiles both assemblies with Unity's Roslyn from the
  newest editor-build `Library/Bee` response files (ones with `-define:UNITY_EDITOR`; a
  player build's would hide editor-only members and report false errors), with fresh source lists. Run it after every edit.
  Unity itself only compiles when the user focuses it.
- `harness/run.sh <Name> [project]` builds a headless .NET harness against the real
  `CommandGraph`, `CommandInterpreter`, `BattleModel` and `CpuPlayer` sources (`Shim.cs` fakes
  `Resources` and `JsonUtility`) and runs it. Existing harnesses, each a baseline to re-run
  after related changes:

  | Harness | Measures | Current good result |
  |---|---|---|
  | `Facing` | defender heading at hits and at landings, misfaced ticks, heading snaps (CPU matches, mostly Xiaoyu) | hits 28 of 646 off >60°; **landings 47 of 267 off >60°** (top: `Co_UkemR0DE`); 1,766 of 85,760 ticks off >90°; 74 snaps (2026-10-29, after the lying-line fix). **Landings keep getting worse as falls lengthen (21 → 38 → 47), and wrong-way landings are what the user complained about: flag this before building on knockback.** |
  | `Jitter` | loser movement reversals and turning after a KO | 1.9 reversals and 2° of turning per round end (2026-10-29; was ~0.9, then ~0.2 before the KO knockback). Attributed to the new lying-line heading turns during the death fall; unverified |
  | `Pushed` | displacement of a fallen fighter walked over | 0.000 m |
  | `HudTrail` | how long the HUD's red damage holds after the last hit (hit reaction stands in for the native StopGaugeAnim) | single hits median 30 frames (reference capture ~28); p90 77, max 186 (knockdowns) |
  | `Effects` | battle-effect triggers per role over 3 CPU matches, shortest gap between floor effects | per pair: ~60–90 Hit, ~45–85 Counter, ~15–25 Guard, ~35–45 Blast, ~8–10 Rage, 1–9 Punish, 3 KO; floor gap ≥ 26 ticks (counters stay high because the CPU attacks constantly). Since 2026-10-27 Pierce/Blast/Smoke/Punish/KO are only model conditions here: versus no longer draws them |
  | `RageCamera` | each fighter's Rage Art forced in Rage, both slots: 0x8439/0x843A events VersusGame acts on, track file present | all 16 cases play, 8 fighters (2026-10-29): archive tracks L96/R97 (Jin, Kazuya, Katarina, Bob), L98/R99 (Xiaoyu, Leo), and moveset-bank tracks L0/R1 for Alisa and Lidia (plain value, no flag, files `asa_m0/1`, `nsd_m0/1`). The harness treated flag-less values as "none" until 2026-10-29 and exited 1 |
  | `RageButton` | the Rage Art button's `RageArtTrigger` pressed 2 s into a CPU round, every fighter, both slots | all 16 OK, 8 fighters (2026-10-29): RageArts move within 300 ticks, armoured 7 ticks in (RageArts01), camera event incl. moveset-bank `m0` (Alisa, Lidia), grant released after. Bob slot 0 absorbs Xiaoyu's `Fu_s_tlk` and plays on; Alisa slot 0 is struck in the unarmoured 7-frame `RageArts00` (it carries 0x82BE = 7, meaning unknown) |
  | `CharacterEffects` | every 0x802E manifest row forced in both slots, then CPU matches vs Xiaoyu (code from `CHARACTER_EFFECTS_CODE`, default `ltn`) | all rows on their frame and bone (Kazuya 460/470: `Kz_Transform2` cancels to `Kz_sKAM00_` at frame 1 when forced); CPU rows in 3 matches: Jin 18, Kazuya 12, Alisa 8, Katarina 4, Xiaoyu 0 (most rows are item moves `Co_I*`) |
  | `Knockback` | defender travel from a clean hit to landing on the floor, away from the attacker (all 25 pairings) | 8 fighters (2026-10-29, after the JACKup fix): falls median 1.25 m, p90 2.60; air hits median 1.89 m. Earlier, 5 fighters: falls median 1.30 m, p90 2.55; air hits median 1.50 m (2026-10-29). **The slot 13/14 fix first dropped air hits to 0.99 m (`Dw_AIR00_Png`/`aDw_ym_y01*` carried 0.00 m, `AIRBY=1` lists by reaction); the lying-line fix brought them back to 1.50, still below the earlier 1.85. The user has complained before that air hits fall too short, and wasn't told about this dip.** `sDw_KAO00_` ~2.1 m, juggle `sDw_AIR00_` ~1.8 m |
  | `DownYaw` | heading jumps >20°/tick of a fighter on the floor or within 30 ticks of it, by cause | 18 jumps over 20° in 46,001 floor ticks (2026-10-29), 10 of them the intended `sDm_ROL00RD/LD>dwDWN_00U` heading cuts; was 118 when getups snapped the lying body to the opponent |
  | `Overlap` | ticks where the two bodies (feet-to-head lines) are closer than 0.35 m, by state pair | up+up 0.1%, down+up 0.4%, air+up 0.6%; air+air/air+down pass by design and run ~30–48% (air+down 48.2% on 2026-10-29) |
  | `KoCamera` | KO schedule from catalog `koCamera` (freeze, camera weights, victory start) and KOs / Rage Drive KOs in CPU matches | 1% for 90 frames (0.9 ticks advance), weights 0.2/1/0.75/0 at frames 1/5–95/100/115, victory 2.7 s; 13 KOs in 6 pairings, 0 by Rage Drive (the CPU never starts one) (2026-10-28). Since 2026-10-28 the default slows only the winner (`koFreezeWinnerOnly`); the global 1% freeze is the ORIGINAL preset. The header comment "orbiting camera not wired" in `harness/KoCamera.cs` is stale: mode 8 is wired |
  | `Sounds` | all 64 pairings: clean hits whose impact comes from the attacker's own sound list, and sound properties with / without an exported clip, by category | 93% list impacts (2,467 of 2,654); only category 10 has no clip (121) (2026-10-29) |
  | `ShakeEvents` | data shakes per round, by preset and move | 0–3 per round |
  | `ShakeReach` | whether shake-carrying moves reach their property frame (pairs jin/xia, kaz/ltn, asa/jin) | read the source for its report |
  | `EffectDemo` | the tuning scene's directed exchanges: does each (hit, counter, guard, trade, KO, Rage Drive KO) produce its event, with the press, startup and distance used | 30 of 30 over 5 pairings (2026-10-28) |
  | `KoAction` | what the loser does right after a KO at full speed: moves and ticks, when the death move starts and when it lies down, by reaction kind (`KOACTION_COLLAPSE=1` with the immediate collapse) | off: standing KOs reach `fatal_s` at tick 31 (26-44), lying at 111; on: 29 of 55 KOs `fatal_s` at tick 0, lying at 80; launched unchanged (2026-10-28) |
  | `Trade` | trades (both fighters clean-hit on one tick) in 30 CPU matches, how many reach 17 damage (the original's slow-down), and the move pairs | 70 rounds: 13 trades, 9 slow-downs (2026-10-28) |
  | `FollowRate` | 0x84CC camera requests in CPU matches: old-zoom firings per round, ticks the original follow rate is off its base, and the moves that set it | 0-3 per round; jin vs xia 24% of ticks at x2 after one `Jz_rplp01`, kaz vs xia 5% at x1.5 after `Fu_RrollA`, others 0% (2026-10-28) |
  | `KoStand` | after a KO, does the loser get upright again before the death fall (all 25 pairings × 2 seeds) | 0 of 297 KOs over all 64 pairings of the 8 fighters (2026-10-29). Was 4: KOs by the `sDw_JACKup` hop (airborne frames 1-24, lands back in stance) skipped the immediate collapse, so the loser landed, stood and then fell; `BattleModel.StandingReaction` now counts a hop that returns to the stance. A one-tick airborne flag on the KO tick is ignored |
  | `KoOverlap` | after a KO: loser's slide from the blow, and bodies closer than 0.35 m until the round ends (`SHORT=1` lists slides < 0.5 m) | slide median 1.24 m, close in 7 KOs, 0.2% of ticks (2026-10-29 run before the last fix; with the winner-only KO slow-down the loser slide was 1.26 m) |
  | `DoubleFall` | defenders upright between two falls with no hit in between (follows each defender after a hit) | 96 of 2,188 before the lying-line fix; not re-run after it |
  | `RiseHit` | hits on rising bodies (Katarina vs Jin by default, `RISEALL=1` for all pairs) | read the source for its report |
  | `HipYaw` | drawn hip heading (dir + carried turn + clip hip yaw) of a body that was on the floor the tick before; jumps >45°/tick (`HITS=1` lists only those on a hit) | 7 of 45 hits on floor bodies jump (2026-10-29; 201 jumps in all, mostly hidden by the crossfade). `ALL=1` scans every tick and `ROUNDEND=1` adds round ends: ~2,700 turns over 45° in 50 matches, nearly all normal (`Jz_round_RK`, screws), so a whole-match scan can't find the user's moment; ask for mm:ss and who turned first |
  | `BodyFlip` | ticks where a body's feet-to-head line turns >100° or its head jumps >0.6 m without a hit (pose snaps between moves); also hits on lying bodies (the flip shows the tick after the hit) | 892 of 430k ticks (2026-10-29); 0 flips for `sDm_ASI10LD`→lying (was 20), rolls→lying (was 27) and hits on lying bodies. `ALLKINDS=1` lists every kind |
  | `Armour` | power crush (property 0x81BD): each fighter's armoured moves (power crush, `*arm` variant, RageArts01/02, `Kz_Transform`) forced while Xiaoyu's (Jin's vs Xiaoyu) fastest high, mid, low and throw lands inside the window | PASS, 160 cases (2026-10-29): highs and mids absorbed (damage dealt, move kept), lows and throws hit. One throw does not reach `Kz_Transform` (range, not counted) |
  | `Ko`, `Falls`, `Getup`, `Props`, `Trace` | KO flow, falls, getup choice, move properties, sidestep alignment | read the source for its report |
  | `Pair` | CPU matches for a pairing (edit the pairs in the source): rounds, hits, moves used, longest stay in one move | Katarina vs Xiaoyu/Alisa, 9 matches: all end by KO in 29–80 s; Katarina's longest stay ≤ 108 ticks. Alisa once stayed 463 ticks in `Aa_chain4RPLP5`, a chainsaw move that cancels into itself at frame 37 (probably a held-button loop whose requirement isn't evaluated) |

  Pairwise harnesses cover all eight fighters (`nsd`, `bob`, `leo` added 2026-10-29); add each new fighter's code to their `codes` lists.
  Write a new `<Name>.cs` next to them for a new issue: a `static Main(string[] a)` that sets
  `UnityEngine.Resources.Root = Path.Combine(a[0], "Assets/_Project/Resources")`, builds
  `new BattleModel(new MatchRules { health = 170 }, graphs)`, drives it with `CpuPlayer`s or
  scripted `PlayerCommand`s, and prints a before/after metric.
- `harness/footskate.py` measures planted-foot slide with FK on the bind skeleton (it verified the
  root-motion remap; usage is in its header).

The command oracle stays in the project: `python Tools/CommandGraph/test_movelist_demos.py`
(see `Docs/ORIGINAL_COMMANDS.txt` for the exact arguments). Current results are Jin 66/66,
Kazuya 71/71, Xiaoyu 115/115, Alisa 121/123, Katarina 64/65, Lidia 91/92, Bob 89/90 and Leo 72/75 plain commands (Alisa's 37 Quick Slider `d+3` and
123 10 Hit Combo press buttons the reference doesn't consume; `CommandGraphValidation` allows them
as 2 known gaps; Katarina's 10 Hit Combo fails the same way, 1 known gap; Bob's Quick Langouste `b,B+2+3` starts no move in the reference, 1 gap; Leo's two Jin Ji Du Li stance strings and one While rising move need setups, 3 gaps). Any interpreter change must keep these, and
must keep the Unity traces identical to the Python reference.

## Workflow for a reported issue

1. **Pin it down.** If the user gives a video, extract frames (ffmpeg contact sheets) and say
   which moment shows the fault. Name the move by its original name when you can (`Fu_zenten`,
   `Jz_tate_sok`).
2. **Reproduce headlessly** with a harness and print a number that shows the fault. Rendering-only
   faults (sliding feet, jerky camera, hair) get a script or FK measure instead.
3. **Fix at the right layer.** Gameplay truth goes in `BattleModel` and the interpreter; anything
   that only changes what is drawn goes in `JinRig`, `FighterActionMotion` or `VersusGame`.
   Easing is presentation; never smooth state the battle reads.
4. **Measure again** and re-run the related baselines above.
5. **Compile-check**, then tell the user what changed, the before/after numbers, and three or so
   concrete things to look at in LocalVersus2. Ask them to focus Unity so it recompiles. They
   can't see your harness, so say what was not verified in the scene.
6. If the issue persists, ask what exactly they see or for a short clip before guessing again.

## Decisions already made (keep them unless the user asks otherwise)

Two of these were never confirmed. The fighting session ended right after their last change:

- **Facing:** the user re-opened it with "characters should face to each other and they should fallen(landing) on the other's facing direction… especially Xiaoyu's long special attack movements".
- **Hair and cloth:** the first version was "too glitchy"; there has been no verdict on the retune.

Ask about both before building on them.

- **Auto-fight is the default and has its own code path.** `automaticFight = true` (F2 toggles it; LocalVersus2 stores 1 since 2026-10-27, user request, flag `Temp/localversus2-auto-fight` on/off saves it). **Rage Art button** (auto fight only): RAGE ART P1/P2 buttons or F3/F4 → `RageArtTrigger` (in `CpuPlayer.cs`) grants Rage via `BattleModel.rageGranted`, the CPU closes in and replays `CommandGraph.rageArt` (the movelist's Rage Art demo, added by `Tools/CommandGraph/add_rage_art_demo.py`; the exporter keeps it out of the random demos), retries if no RageArts move comes out, releases after it ends or 10 s; Rage visuals follow the grant, and `Update` returns early on that path. A final-round KO freeze once happened only there. Auto-fight also forces the held victory button to 0, so victory takes picked by a button only work in manual play. Check every round-end change in both modes.
- **Presentation smoothing.**
  - Moves crossfade over 5 frames with SmoothStep (`FighterActionMotion.CrossfadeFrames`), which removed a one-frame idle pop between the KO hit and the death fall.
  - Fighters are drawn interpolated between 60 Hz ticks (`VersusGame.Interpolated`), with no lerp across jumps of more than 1 m (round reset).
  - The camera stays side-on to the fight line, and left/right input follows that screen axis.
- **Facing (unconfirmed).** Fighters face each other outside attacks and reactions, and an attack keeps
  tracking through its startup (before `firstActive`). A struck or guarding body is turned square
  to the attacker on contact so its reaction goes away from them; a body lying on the floor, or still
  rolling or getting up (`rising`), keeps its heading. A body on the floor that changes move keeps
  its feet-to-head line: the interpreter takes the face-up/face-down twin of a lying alias that matches
  the landing (`CommandInterpreter.OrientLying`, slots 4/8, 6/9, 7/10), and `BattleModel.KeepLyingLine`
  turns the heading by any remaining swing >30° (`FighterState.headingCuts`; `JinRig` shows it without
  easing, `VersusGame.Interpolated` doesn't sweep it, `FighterActionMotion.HoldFadeAcrossTurn` keeps the
  fading pose in place). User report 2026-10-29: lying Katarina swapped ends after `sDm_ASI10LD`. Bodies on the floor or in the air, and a KO'd fighter, never re-face toward the opponent; `KeepLyingLine` may still turn a lying body's heading by design, to keep its body line (swings over 30°).
  `JinRig.ShownDirection` eases the visible yaw (rate 18/s) and snaps after a 0.5 m position
  jump. The carried turn `f.turn` is not eased, because the clip's hip yaw compensates for it.
- **Getting up (2026-10-26, unjudged).** After a fighter has been on the floor, getups, rolls and
  getup kicks don't snap to the opponent: `BattleModel.rising` pivots the heading at most
  `RisingTurn` (5°) per tick until the fighter is back in stance/crouch and facing within 5°
  (or `RisingLimit` 120 ticks). A hit on a rising or rolling body keeps its heading (`BattleModel`: `if (!d.downed && !rising[hit.defender]) FaceOpponent`): the reaction plays in the direction the fighter already faces, while the pushback still goes away from the attacker. The user was told about this trade-off.
- **After a KO, separation keeps running** in RoundOver and MatchOver (`BattleModel.Tick`), and the KO blow pushes the loser at least 1 m (`KnockOutPush` duration 20, displacement 50). Both are this remaster's choices, not original data.
- **Backturned.** The interpreter tracks `BodyYaw` (carried turn plus the clip's hip yaw).
  Property 0x81A3 holds the turn between moves. Backturned moves (`*_R_*`) are authored already
  turned, so entering one resets the carried turn.
- **Knockback (2026-10-26, unjudged in a match).** Reaction clips carry no root travel; the fly-back is
  the reaction's pushback record for the struck side (0 front, 1 backturned, 4 counter hit,
  5 downed, 6 block; `refs[0]` fallback), exported by `Tools/CommandGraph/pushbacks.py` (also called
  by the exporter) into `graph.pushbacks` + `reactions[].pushbacks`. `BattleModel.StartPushback`
  pushes the defender along attacker→defender: the curve is millimetres per frame, then the signed
  displacement is millimetres every frame for `duration` frames (inferred from the data, not game
  code; the first reading, centimetres in total, gave ~1 m falls and the user found falls and air
  hits too short). A body off its feet (downed or airborne) takes slot 5, the long slide. A new hit replaces the push;
  hit-stop (slow-down) pauses it. Vertical pushback, the side pushbacks (2/3) and the direction and
  rotation fields are not used.
- **Separation.** Besides 0.8 m between positions, the bodies keep `TorsoClearance` (0.5 m) apart
  (`BattleModel.SeparateTorsos`, 2026-10-26; the user found the first version incomplete): each
  fighter's body is the floor line from its feet midpoint to its head, posed by the clip's body
  track (`clip.body`, `bodyFields` 6 values every `bodyStep` frames: hip, head, feet; from
  `Tools/CommandGraph/body_points.py`: FK on `<code>_bdl_1P.json` + the museum asset's
  `boneNames`, a few minutes per fighter; the hip sits on BASE, the head is ~12 cm forward in
  stance, ~40 cm in lunges; lying, head −52 cm and feet +1 m along the floor). Stance spacing
  (0.56 m) is untouched; leaning attacks and stepping onto any part of a lying body are pushed
  out, so a walker can no longer cross a fallen fighter (`Pushed` walker stops in front). Same shares as below. Two standing fighters split their overlap. A downed or airborne body is never
  pushed; only the standing one moves. Two non-standing bodies pass through each other.
- **Root motion.** Battle travel comes from the exported clip `forward`/`lateral` (mm) along the
  body heading. For presentation (entrance, victory) the JRT1 root travel must be remapped
  `(dx, dy) → (−dy, −dx)`: under `BODY_SCALE__group` it lands 90° off the body, which caused
  "walking slippery/in the air". The remap also changed entrance travel, which the user has
  not checked. The museum's "Source travel" toggle and `staging_test` have no remap and probably
  still slide sideways (unverified).
- **Round end.** After a KO both fighters keep ticking with neutral input in RoundOver and
  MatchOver for 240 ticks, so the original death fall plays. Condition 135 "on death" reads own
  health. **KO (2026-10-28, from the executable, `export_ko_camera.py` → catalog `koCamera`):** game
  speed 1% (by default only the winner is slowed, `BattleEffectTuning.koFreezeWinnerOnly` → `BattleModel.SlowFighter`, with `Time.timeScale` left at 1; the ORIGINAL preset restores the global .01 near-freeze) for 90 camera frames (150 if the winner's Rage Drive
  0x801B is active), shake preset 6, and the KO camera (mode 7): the last shown view, FOV 65 → 35,
  weight 5 frames in / 90 held / 20 out over the live view; same every round. Camera effects step per
  60 Hz frame of real time (`VersusGame.AdvanceCameraFrames`), not per battle tick. The reference match
  shows the freeze (146.0–147.47 s). The "K.O." call shows for 1.8 s, then the result panel. The
  victory starts 1.2 s after the freeze ends (2.7 s; `VictoryFallSeconds`), and the winner is
  frozen at `victoryAnchor`, facing the loser. A Rage Drive KO plays camera mode 8 instead
  (`koCamera.rageDrive`, `BattleCameraEffects.RageDriveKoShot`): 150 frames, no blend, a low (0.1 m)
  telephoto from (distance to opponent + 4 m) off the fight line, FOV 20 → 35, swinging -30 → -60° or
  -60 → -45° (random variant), roll 15°; its viewer-side swing and roll direction are interpretations not yet
  checked on footage. Not wired: the original skips the KO camera on throw KOs. **Trade** (both fighters
  take a clean hit on one tick, either ≥ 17 damage; catalog `tradeCamera`, hit resolution 0x140713DD0):
  10% speed for 24 frames, shake preset 5, held view FOV 35 (4 in / 20 held / 30 out), through the same
  `VersusGame.StartHold` as the KO; about one per 8 rounds in CPU play. The TRADE EFFECT button / F5 (either mode,
  `ForceTradeEffect`, user request 2026-10-28) plays it on demand without changing the fight. Trades run `tradeLength` 2.5× longer and eased (`smoothTrade`;
  remaster tuning, user request; 1 / off = original) and `centreTrade` aims the close-up at both fighters'
  framing-bone centre, widening past FOV 35 only to fit them. **All of these, the hit shake, common effects
  and camera smoothing are fields of `BattleEffectTuning.Current` (Resources asset, 2026-10-28), not of
  VersusGame**; the KO has its own speed/length/FOV/ease/centre there (defaults = original). `koCollapseAtOnce`
  (→ `BattleModel.immediateKoCollapse`) makes a standing KO play `fatal_s` at once, and `koFreezeWinnerOnly`
  slows only the winner (`BattleModel.SlowFighter`) so the loser acts at full speed (user request, not original).
- **Victory takes.** Only takes the winner's own moveset reaches (`VictoryTargets`). They keep their
  source root travel (jz_w00_n 5.8 m, kz_w00 4.5 m, fu_w00 3.6 m, aa_w01 6.7 m forward + 3.2 m
  aside; measured from the JRT1 roots), so `VersusGame.FitVictoryAnchor` shifts the anchor before
  the take starts until the whole path lies inside the arena minus 0.35 m (the cut to the take's
  camera hides the shift), and `KeepOnFloor` clamps feet/hips per frame as a safety net (user
  report 2026-10-26: winners walked off the floor). `VictoryStaging.Travel()` gives the path. The paired
  `_y` partner takes (`jz_w00_y`, `kz_w01_y`, `fu_w01_y`) are never the winner's. A button held
  at match end picks the take whose cancel needs it (`(command >> 40) & 0xF`).
- **Camera.** Spring rig (SmoothDampAngle 0.38, distance 0.32, target 0.2, each divided by the current 0x84CC follow rate; no zoom of its own). Camera **Shake (2026-10-27): original, from the executable** — `catalog.json` shakePresets (10 per-frame pitch tables, unit 2π/65536 rad, `Tools/CameraEffects/export_shake_presets.py`); dispatch 0x8001-4/0x8006-9 → presets 3-6, 0x8005/0x800A → min(value,9); pitch-only nod per camera frame (`BattleCameraEffects.OriginalShakePitch`, `VersusGame` shakePreset/shakeFrame). Per-hit shake removed (not in the data; the KO's preset 6 is native code, see Round end); then, at the user's request, `BattleEffectTuning.hitShake` (default on, labeled remaster choice; preset and strength sliders in the tuning panel) plays preset 3 on clean hits and 4 on counter hits without ever replacing a data shake. Details `Docs/BATTLE_CAMERA_EFFECTS.txt`.
  impact comes from move properties: shakes 0x8001–0x8004/0x8006–0x8009, slow-down 0x828C
  (immunity 0x828D, strength a guess), Rage Art tracks 0x8439/0x843A. **0x84CC is not a zoom
  (2026-10-28, from the executable):** it sets a sticky per-fighter camera request that scales the
  follow rate (base 0.1/frame; 11 ×3, 12 ×2, 13 ×1.5, 14 ×0.5, 15 ×0.25; stepped +0.003/−0.002 per
  tick; `export_follow_rates.py` → catalog `followRate`, `BattleCameraEffects.TargetFollowRate`,
  `VersusGame.followRate` divides the smoothing times). The old 0.84 launch zoom is gone; harness
  `FollowRate`. The framing guard keeps the head, hands, feet and BASE bones of both fighters in view
  (pads X 0.18, Y 0.05).
- **Effect audit (2026-10-27, user: "find the wrong effects like the punish").** Against the reference
  match, removed from versus: PierceFX (≥20 damage guess), KOFX/`PS_EF_SpecialKO` (none of 9 KOs shows
  it), BlastFX/SmokeFX on landings and falls (none on solid-floor landings), PracticePunishNotice
  (Hwoarang's own effect). Rage aura is now a red fresnel rim (`RageRim.cs`, `HitEffects/RageRim.shader`,
  sRGB ~(192,0,16); `Power` 3.5, `Intensity` 2.2, `Push` .004, tuned by eye). **Note:** `VersusGame.UpdateBattleEffects` still plays RageFX, the looping RageAuraFX *and* the rim, although the user was told the aura was replaced; decide with the user which is intended before changing it. The rim sits behind `if (!effects) return`, so with `commonBattleEffects` off there is no rim either. Kept: HitFX (orange fire spray is the common hit on every attacker; ours is
  smaller/shorter), CriticalHit3FX, GuardFX, RageFX. Details and timestamps in `Docs/BATTLE_EFFECTS.txt`.
  Don't re-add the removed triggers without original evidence.
- **Power crush armour (2026-10-29, user request).** `BattleModel.Armoured`: the last 0x81BD at or before the move frame decides
  (non-zero arms, 0 disarms; frame 0x8001 = frame 1). Highs and mids are absorbed (`HitEvent.absorbed`: damage dealt,
  no reaction, pushback or turn, attacker `connected`); lows, special lows, throws (hit level bit 0x400000, `HitEvent.throwHit`)
  and a hit that would KO are not. Armoured: every RageArts01 (whole startup) and RageArts02 to frame 135, each fighter's
  power crush (`Jz_lrk`, `Kz_tukirpsp`, `Fu_3lk`, `Aa_46RP`/`Aa_3wp`, `Ln_lrk00`, `Nd_3lrk`/`Nd_tukirpsp`, `Ft_BodyAttack`,
  `Un_bokuhoLRP`/`Un_spam`, from frame 8; `*arm` variants from frame 1) and `Kz_Transform` 5-21. VersusGame's `OnHit`
  plays Armor0FX, `armourSound` and the hit shake for an absorb.
- **Common battle effects are on in LocalVersus2, alongside character effects (user request 2026-10-27, after being off earlier that day).** `BattleEffectTuning.commonBattleEffects` in `Resources/BattleEffectTuning.asset` (flag `Temp/localversus2-common-effects` "on"/"off" writes the asset; the per-scene VersusGame switches for common effects, hit shake and the trade are gone) gates every `CascadeEffects` common role and the orange fallback sphere, and is followed live in Play (`SyncCommonEffects`); `ResetImpactEffects` also clears finite common instances (`CascadeEffects.Clear`). Codex skill `~/.codex/skills/tekken-versus-common-effects` covers recovering/wiring them (read manually); the characters' own wired effects (`CharacterBattleEffects`, 0x802E) always play. Don't switch the common set off again unless the user asks.
- **Battle effects (commit `14179dcd6` on the LFS history, `b71b8341e` before the rewrite; revised 2026-10-25 against the reference clips in
  `C:/Users/Administrator/Videos/Captures/tk7` (moved there by 2026-10-27); checked in editor captures, not yet judged by the user in a match).**
  Still to judge in a match: spark placement and brightness, counter-line intensity, `glareGain`,
  the Rage rim and Rage never being spent. (The KO flash, PierceFX, punish and floor effects were
  removed on 2026-10-27; see the effect audit above.)
  Reference findings (TEKKEN 7 2026-10-23 08-04-08.mp4): a normal hit is a compact ~30 cm orange
  glow with white sparks for ~0.1 s; counter hits show the orange torus rings; hits in the
  opponent's recovery showed a blue ring that was **not** a punish notice: it is Hwoarang's own
  `PS_EF_CHR_HWO_nomal` (0x802E on his kicks; corrected 2026-10-27 after the user said the punish
  effect didn't match). `PS_EF_PunishNotice_*` is the practice-mode crosshair and is not played in
  versus; in the original the KO blow adds slow motion, zoom and `PS_EF_SpecialKO` from `BP_EF_KO`, but
  none of the 10 reference KOs shows it, so versus plays only the native KO camera (a 1% near-freeze plus the held mode-7 view at FOV 35; see Round end; the old .72 zoom and 30% slow motion are gone); Rage start is red ribbons that travel with the body.
  **Counter rule** (`BattleModel.CommandContact`): counter only while the defender's move is in
  startup/active (`moveFrame <= max(firstActive, lastActive)`); a hit in its recovery sets
  `HitEvent.punish`. `HitEvent` now carries `punish` and `height`.
  `VersusGame.OnHit` falls back to the old orange sphere only when `effects` lacks the role. The original's common Cascade systems (`BP_CommonEffectManager` roles) are
  rebuilt with Unity particles: clean hit → HitFX, counter → CriticalHit3FX, (punish: nothing; the
  practice crosshair is exported but not played), guard → GuardFX, Rage start → RageFX + looping RageAuraFX + the red rim glow (`RageRim`) on body
  bones until the round ends (Rage is never spent in the model, except that the RAGE ART button's grant is released after the Rage Art, which stops the aura). The struck point is the
  attacker's nearest striking bone against the defender's hip–head line. Effects run on game time.
  Vector fields are approximated (tightness → drag, strength → noise); AlphaClip is inferred
  (static: subtractive sharpen; per-particle: power erosion), since cooked materials keep no
  maths. Refraction emitters are skipped; glare is `CascadeEffects.glareGain`. Needs
  `com.unity.modules.particlesystem` in the manifest. Review with `Temp/capture-hit-effects`
  after `Temp/refresh-assets` (Scripts/Editor/AssetRefreshHook.cs recompiles without the user focusing Unity).
- **Falls and getups.** Lying loops are alias slots 7/10. A human who stays down without input
  stands up after `IdleGetupTicks` (45; a convenience, not original). The CPU picks an original
  getup after a random delay.
- **Hair and cloth (unconfirmed).** Versus builds `WardrobeDynamics` from the fighter's wardrobe
  after dressing. The retune also changes the Wardrobe scene:
  - stiffness: fringe 320, hair 130, other 190;
  - limits: 28°, 60°, 40°;
  - retention: 0.86, 0.90, 0.88;
  - hair detected by `_OPT`, `Head_` and piece-prefixed bone names.

  Springs run on those helper chains and follow 80% of the root translation. The
  update is split evenly every frame (frame-rate independent). Short Jin/Kazuya hair has no
  chains and stays rigid, as in the original. The saved fight outfits have no APEX cloth.
- **Lighting.** Versus is lit by the scene's "Render Settings" object (`StagingRenderSettings`,
  shared with staging_test, the museum and the wardrobe), yawed 180° (`VersusYaw`) because the
  versus camera looks along +Z. The match uses an original stage when one is picked
  (`VersusGame.BuildArena` → `EnvironmentSelection.Load().PickForMatch`, chosen and saved in
  `env_scene`; stage export, import, placement and fixes belong to the `tekken-stage-environments` skill). Otherwise it builds the
  generated arena, which keeps its floor and neon materials on the settings' solid background.
  Unverified in Play: movement bounds are still the generated arena's invisible rectangle
  (`arenaHalfWidth/Depth`), so stage walls have no collision; the far clip is 2000 with a stage. While that object exists, `HdrpSceneLighting` leaves
  the extracted stg29 lights and sky dormant; delete it to restore them. `VersusGame` creates
  the old key and rim fallback lights only when the object is missing. Unverified risk: the
  shared 12 m shadow distance against the arena's ~13 m size (and much larger stages) may fade
  shadows at the edges.
- **Outfits and colours.** `FightWardrobe.DressSaved` dresses each fighter in the saved items
  and applies the saved colours plus the game's default tints (wardrobe skill).
- **Controls.** These are Tekken 7 PC defaults: P1 WASD + U I J K; P2 arrows + Num 4 5 1 2 (or
  O P L ;). F1 shows the command list; F2 toggles CPU auto-fight.

## Battle UI

The HUD (life gauges and their effects, timer, round pips, portraits, names), the round calls
(ROUND/READY/FIGHT/K.O. and the rest), character select and the VS loading screen moved to the
`tekken-versus-ui` skill on 2026-10-28. Use it for any UI work. Timing hooks such as
`ReadyTick`, `FightTick` and `KoCallSeconds` live in `VersusGame`, so change them with both skills
in mind.

## One versus scene (LocalVersus2)

On 2026-10-27 the user asked for a single versus scene: `LocalVersus.unity` was retired (moved to
the Recycle Bin, still in git) and `LocalVersus2` is the only versus scene, first in Build Settings
(then env_scene), so `CharacterSelect.ReloadScene`'s `buildIndex` path should work in a player
(a player build at `Builds/LocalVersus2` exists since 2026-10-29 and its automatic fight runs, but the select-to-match reload wasn't exercised). `ProjectSetup`, `JinVersusRetargetSetup`, `JinMaterialSetup` and
`StagingRenderSettingsSetup` now point at LocalVersus2 ("Remaster > Open LocalVersus2 scene").
Don't recreate LocalVersus. The versus feature itself is kept and growing (HUD, select, effects).
`Settings/LocalVersus.asset` still exists because `JinImporter` loads it. Ask before removing
anything else. The earlier removal analysis, kept for reference:

- **GUID reachability.** From the versus scenes, minus everything the kept scenes and Resources
  reach, only `Settings/LocalVersus.asset` is versus-only, and `JinImporter` still loads it.
  Removal is mostly a code job.
- **`JinRig` must stay.** It sits on every outfit prefab and depends on `FighterActionMotion`,
  `MatchRules`, `FighterState`, `MoveSpec`, `JinRetargetMotion` and `IFighterPresentation`.
- **Shared code that names versus types** and needs untangling:
  - `HdrpSceneLighting` (`FindFirstObjectByType<VersusGame>`);
  - `OriginalStageLightingData.Apply`;
  - `StagingRenderSettingsSetup`'s scene list;
  - the editor setups and validations;
  - `EditorBuildSettings`.
- **Keep** `FighterView.Surface`, `EntranceCameraLens/Track`, `StagingCatalog` and
  `SecondaryMotionTrack`; the other scenes use them.
- **After a removal,** retire this skill and its memory entries only if the user asks.

## Impact effects (original game data)

- **Where the data is.** The effects live in the plaintext paks under
  `TekkenGame/Content/Effect/TK75/`: Common 475 files (about 96 MB), Character 3,108, Stage 501.
  `decrypted/` holds only the AES entries, not the effects.
- **Effect roles.** The class defaults of `Effect/TK75/Common/BP_CommonEffectManager.uasset`
  name one Cascade system per role:

  | Role | System |
  |---|---|
  | HitFX | `PS_EF_cmnhit00_ts` |
  | CriticalHit3/4/5FX | `PS_EF_cmncritical00_ts` |
  | GuardFX | `PS_EF_cmnguard01_ts` |
  | PierceFX | `PS_EF_cmnhit_line00` |
  | BlastFX | `PS_EF_cmndown00_ts` |
  | SmokeFX | `PS_EF_cmnsmoke00` |
  | RageFX | `PS_EF_cmnragestart00_ss` |
  | RageAuraFX | `PS_EF_cmnrageaura_ts` |
  | PracticePunishNotice{Hi,Mid,MidSp,Lo}FX | `PS_EF_PunishNotice_{hi1,Mid,MidSP1,low}` (found by `grep -a Crosshair` over the Common `.uasset` files; textures `T_EF_CrossHairs03a/b/center`, glare `T_EF_CrossHairs02Glare`, parent material `M_EF_Crosshair01_Outline`) |
  | KOFX | `PS_EF_SpecialKO` (exporter `EXTRA_ROLES`; spawned by `BP_EF_KO`) |
  | Armor0FX | `PS_EF_cmnpowerclash00` (manager role, the original's OnArmorSucceeded): played instead of the hit spark when `HitEvent.absorbed` (power crush), with its own tone (`armourSound`, 230 Hz) and the normal hit shake (2026-10-29). Two emitters: a near-white aura (start colour 2.0, glare 20, refraction not reproduced) and a faint flash; the white glow comes from bloom, which editor captures skip |

  Further roles: WallHit/WallHitHard, SeizeFX (throw), SparkFX (floor), Armor0FX (power clash),
  SpecialGuard, Homing (Armor0FX is now wired, above). Spawning itself is native code; its event parameters are HitPosition,
  HitDirection, Damage and PlayerID.
- **Exporter.** `Tools/HitEffects/export_hit_effects.py` writes
  `Resources/HitEffects/effects.json` (roles, emitters, limitations), 20 textures and 14 mesh
  JSONs. Run it as `--source <unpacked fx> --t7extract <workspace t7extract>`. The Cascade and
  material dumper used to study the systems (`dump_cascade.py`) and the unpacked `fx/` tree lived only
  in a session scratchpad; move the dumper into `Tools/HitEffects` before relying on it again. Its conversions:
  - UE cm become metres, and UE (X forward, Y right, Z up) maps so that Unity local +z is UE +X;
  - Cascade sprite sizes are full sizes (verified: the 50-unit punish reticle is ~0.5 m in the
    video); zero size axes are editor-locked and follow X; `SubEntryStride: 0` means the whole stride;
  - module values come from the baked `RawDistribution` table (Op 1 constant, Op 2 min/max).

  Its limits: bursts are capped at 600 (GPU emitters reach 2,000); refraction-only emitters and
  the GPU vector-field swirl are missing; vector fields and dynamic parameters are approximated.
  RawDistribution tables can store `SubEntryStride: 0` (constant tables); treat 0 as the whole
  stride, or those curves read as zero.
- **Runtime.** `CascadeEffects.cs` rebuilds each system as Unity `ParticleSystem`s with a small
  HDRP-compatible particle shader (`_AlphaClip` on the material, Custom1 per-particle data).
  A material instance's graph lives in its parent (e.g. `M_EF_TextureDistotion_BLD_EA`), and
  cooked builds strip its expression nodes, so the AlphaClip formula was fitted to captures, not
  decoded.
- **Hit position.** `HitEvent` carries `height`, `punish`, `counterHit` but no position (`BattleModel.cs`).
  `VersusGame.Contact()` estimates the struck point from the attacker's nearest striking bone
  (hand, elbow, knee, foot, head) against the defender's hip–head segments, offset up to 0.12 m
  toward the limb. The effect faces a 50% slerp between the across-axis and the blow. (`hit.height` no longer picks
  a punish reticle: none plays in versus.)
- **Punish rule** (drives `HitEvent.punish` only; no effect). Punish requires the defender to be in an attacking move (`firstActive > 0`) and
  not reacting; recovery from non-attack moves doesn't count. Not yet compared with the original.
- **Brightness and KO (approximations).**
  - Source colours are linear; `CascadeEffects.Linear()` gamma-encodes every particle, gradient,
    light and material colour (Unity decodes them as sRGB). Without it hues over-saturate (rings red).
  - Intensity is `compress(peak) * sqrt(max(1, intensity)) * glareGain` (peaks above 4 → `4*(p/4)^.25`),
    and `CascadeParticle.shader` applies a per-channel shoulder (linear to .35, exp to 1) instead of
    whitening: output stays ≤ 1 so the default HDRP bloom (threshold 0) can't turn rings white.
    Fitted 2026-10-26: counter rings (234,162,89) vs reference (226,177,110). The spark AlphaClip
    eases off under ~6 px (fwidth), or the 1 cm hit sparks vanish in play.
    Editor captures (`Temp/capture-hit-effects`) skip the game's bloom: check in-game recordings too.
    Reference guard (TEKKEN 7 2026-10-23 08-04-08.mp4 at 135.9 s): a white spiky burst ~40 cm, then
    white streaks for ~0.4 s.
  - HDRP's default volume does have Bloom (threshold 0, intensity 0.2), which is what turned rings
    white. Tonemapping and exposure matching with the original remain an open decision for the user.
  - KOFX, the KO dust and the floor ring are no longer played in versus (effect audit, 2026-10-27).
    They remain in the effect library and previews: flash-line rays widened from 4 mm to about
    3 cm, KO dust capped at 600 fragments/s and 60 smoke sprites/s, the floor ring ~24 m over 3 s.
  - RageFX and RageAuraFX are re-positioned every tick to the spine bone, so they travel with the
    body.
  - Still unjudged in a match: normal-hit dimness and whether the Rage ribbons follow the body.
  - **Atlas (SubUV) playback** (Codex, 2026-10-27): `_SubUVGrid`, frame in Custom2.w, tiles left to
    right from the top row; `PSUVIM_Linear_Blend` blends frames. `PSUVIM_Random` emitters are omitted
    with a warning (they used to render tile 0). `HasSubUV` needs the exported
    `subImageInterpolation` field, which older common and stage exports lack, so re-exporting them
    changes how they look. `simulationLocal` (exported for 60 emitters) switches the simulation
    space.
  - Bursts are capped at 600 in the remaster; the original data has 1,000-particle bursts.
- **Capturing effects** (`Temp/capture-hit-effects`):
  - Original clips (`TEKKEN 7  <date>.mp4`, with a double space) now live in
    `Videos/Captures/tk7/`; Unity recordings stay in `Videos/Captures`; recordings of the player build are named `Versus Remaster Prototype <date>.mp4` (the product name). The user sometimes re-sends an
    old clip with a new timestamp, so check which fixes predate a clip before blaming one. Unity recordings (`Administrator_ TekkenVersusRemaster - LocalVersus2 - ….mp4`). Start
    with a 1 fps `tile=10x6` overview, then 15 fps sheets around each moment, and compare the
    original and Unity at the same event.
  - Frames come from the clips with the bundled ffmpeg: `pip install imageio-ffmpeg` put it at
    `C:/Python311/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe`
    (`-vf "fps=15,scale=600:-1,tile=5x9" -update 1 out.png` makes a contact sheet).
  - Prime systems in edit mode with `ps.Simulate(0, false, true, false)`. A ParticleSystem that
    has never simulated silently drops particles emitted at t = 0, so only delayed emitters
    showed. Capture PNGs of identical size are the tell.
  - RageFX and RageAuraFX must be oriented by the fighter's facing, or they appear off to the side.
  - RageAuraFX spawns only 1–4 times a second, so capture it at 0.5–3 s, not 0.55 s.
  - Big sizes come from the data, not a scale bug: RageFX shells are about 4 m across and the
    BlastFX ring about 1.8 m. Counter-hit lines move at 10–100 m/s in the data.
- **Extractor gotchas.**
  - `t7extract unpack` patterns go before `-o`.
  - `package.read_properties` skips structs and arrays; Cascade needs a full struct/array decoder,
    plus a `Box` struct handler (6 floats + 1 byte).
  - `texture.iter_textures` yields `(export, Texture|Exception)` tuples.
  - The vendored `Tools/CombatPoseConverter/vendor/t7extract` lacks the texture and mesh readers;
    pass the workspace `t7extract` with `--t7extract`.
  - Unpack into the scratchpad after checking free disk space.

## Character effects (0x802E) and why `ActedMoveProperty` exists

- `interpreter.Step` resets `Frame` to 0 on a transition, so `MoveProperty` misses properties on the
  outgoing move's last frame; `BattleModel.ActedMoveProperty` reads the frame that actually played.
- Only timed `extra_props` (offset 0x80) are exported; `export_command_graph.py` drops the
  `start_props`/`end_props` tables.
- Don't bind character effect ID 0 to every hit: the inherited Blueprint `OnHit` is an empty stub.
  Native `PM_CharacterBone` overrides the stored `Neck` source with the event's VBN bone
  (`28 + id*76` record).
- Before naming an effect wrong, compare the candidate system's actual textures against the
  reference frame. That would have shown at once that the "punish reticle" was Hwoarang's
  `PS_EF_CHR_HWO_nomal`. Remove an effect only with video evidence, and only one at a time; the
  user asked to "find the wrong effect like the punish and fix them all, one-by-one".
- The details live in Codex's skills `~/.codex/skills/tekken-character-battle-effects`,
  `tekken-versus-common-effects` and `tekken-versus-camera-effects` (read manually). The user wants
  these "based on the original game": triggers need code, data or trace evidence, never guesses.
  In the user's words: "work on the original package, not to use the video clips. the video clips
  are the reference, not the full truth."
- **Why ordinary hits barely shake.** Ordinary knockdowns (`sDw_UP_01_`, `sDw_AIR00_`, `sDm_ROL00`,
  `sDw_KAO00_`, `sDm_PNG00_`) carry no shake property; only spins, screws, bounds, wall hits and
  Rage Art finishers do, and no native shake request runs on ordinary hits. That is why the labelled
  `hitShake` exists (the user chose it). Don't add another per-hit shake or "fix" the quiet camera.
- **Wired since 2026-10-28:** preset 5 on the both-fighter slow-down (the trade path) and the Rage Drive
  KO camera (mode 8 via `koCamera.rageDrive` and `BattleCameraEffects.RageDriveKoShot`; its swing side
  and roll direction are interpretations). **Not wired yet** (only in `Docs/BATTLE_CAMERA_EFFECTS.txt`):
  preset 4 at 0x140711298, preset 3 at 0x1407911C2, throw-camera requests (the model has no throw
  state), the special camera for requests 1–6/25–41, and the 0x8FD guard. Exe addresses hold only
  for the build pinned in each exporter (SHA `f46f9ff5…`). The 50 MB exe needs an mmap loader; the
  one used lived only in `%TEMP%`, so recreate it or move it into `Tools/CameraEffects`.

## Adding a fighter to versus

1. Make sure the fighter has a museum `MuseumCharacter` asset with converted clips
   (`Assets/_Project/Museum/<Full>/catalog.json`) and a prefab; the wardrobe skill
   (`tekken-costume-wardrobe`) covers outfits. The catalog needs move lengths: without them a
   Xiaoyu getup kick stayed stuck for 1,173 frames. Give the asset a `characterCode`; combat
   hands load `Resources/SecondaryMotion/<code>_combat_hands` when present, otherwise the body
   clip's hands.
2. Export the graph with `export_command_graph.py`. You need the T7 character id and the stance
   move id; find the stance in the alias table, slot 1. Export the movelist demos with
   `t7extract` (`movelist.py`).
3. Run `test_movelist_demos.py` until all plain commands pass. A failure means a semantics gap;
   fix the reference and the C# interpreter together and keep the other fighters passing.
4. Export the Rage Art cameras (`export_fight_cameras.py`, `mothead/mask/<chr>rotsm.arc`, and
   `--moveset bin/<chr>mot.bin`). Camera properties 0x8439/0x843A carry `0x4000|i` (track i in the
   archive's bank 4) or a plain `i` (DLC-style: bank 4 inside the moveset file), written as
   `<code>_m<i>.bytes`; `VersusGame.StartCinematic` loads either. Only look-at tracks (11, 11, 15, 3, 21)
   are exported. Alisa's and Lidia's Rage Arts use the moveset bank (`asa_m0/1`, `nsd_m0/1`); Bob's and Leo's use the archive (`bob_96/97`, `leo_98/99`).
5. Add the fighter to `VersusGame.Fighter`, `Setup` (code, move-name prefix, actions) and `Spawn`.
   Look bones up case-insensitively, since rigs spell `Base`/`HEAD` differently. Each scene
   object is used once, so mirror matches, Xiaoyu and Alisa spawn from a prefab (`JinPlayable`,
   `Karate_Gi.prefab`, `Characters/Alisa/Costumes/Prefabs/Default_1P.prefab` via `alisaPrefab`, Katarina via
   `katarinaActions`/`katarinaPrefab` defaulting to `Museum/Katarina.asset` and her `Default_1P.prefab`).
   Use `Temp/setup-localversus2` rather than hand-editing the scene. The scene chooses fighters with `playerOneFighter`/`playerTwoFighter`.
   The oracle reports demos plus setup entries (Xiaoyu: 115 + 25, with 140 C#/Python parity
   traces).
6. Add the code to `CommandGraphValidation`, re-run the harnesses with the new pair, and have the
   user play it.

## Editor jobs

Unity stays open, so batchmode can't run. Queue editor work through `[InitializeOnLoad]` +
`EditorApplication.update` hooks that watch `Temp/<flag>` files (for example
`Temp/validate-command-graph`). Create the flag only after
`Library/ScriptAssemblies/Assembly-CSharp-Editor.dll` is newer than your edit, or the old code
consumes it.

**Recompiling.** Create `Temp/refresh-assets` (`Scripts/Editor/AssetRefreshHook.cs`) and the open editor
refreshes and recompiles without the user clicking in. Then wait for the DLL, then create the job
flag. The hook waits while Play is on, and needs one manual focus after it is first added. A
recompile during Play wipes state, so ask the user to stop Play. Computer-use resolves "Unity"
to a separate 6000.6 install, so Claude cannot focus the editor itself.

**Concurrent sessions.** Other sessions edit `VersusGame.cs`, `manifest.json` and the HDRP assets
at the same time, so commits end up mixing their work. Check `list_sessions {linked:true}`
first. If the compile breaks in a file another session touched (it once was a raw newline inside
a string in `WardrobeViewer.cs`), fix it rather than reporting it as someone else's.

**Tuning panel and directed demos (2026-10-28).**
- `Resources/BattleEffectTuning.asset` holds the effect and camera switches and sliders (common effects,
  `hitEffectScale`, `glareGain`, hit shake, the KO freeze, the trade's length/easing/centring, `centreKo`),
  with ORIGINAL and DEFAULTS presets. F6 opens the panel in Play; F5 forces a trade.
- On/off switches in the panel must use `GUI.skin.toggle`; drawn with a label style they show only the
  label (user report, fixed in `VersusGameTuningPanel.cs`).
- The user can't spot short native effects in auto-fight. Give each effect a force button and a directed
  demo that acts out the real exchange and can be replayed (the tuning scene does this;
  `BattleEffectDemo` forces Rage Drive moves directly, since they have no movelist input).
- Open: the user says the counter-hit effect isn't identical to the original. From the reference video
  (54.9 s), our rings look thicker and softer than the original's thin orange tori, our sparks are long
  streaks instead of short white sparks, and we show an extra ring on the attacker's side. Not fixed.

**Player builds and smoke tests (2026-10-29).**
- `Temp/build-localversus2` builds the player (file content = output folder, empty = `Builds/LocalVersus2`);
  `WardrobeBuildPackager` packages only saved wardrobe items (wardrobe skill). A build is ~7.2 GB; check
  free space on D: first.
- Verify with `LocalVersus2.exe --smoke-test -screen-width 1280 -screen-height 720 -screen-fullscreen 0
  -logFile <log>`: it writes `runtime-smoke.json` (legacy `jin*`/`kazuya*` field names whatever the
  fighters) and `arena-preview.png` next to the exe.
- `compile_check.sh` uses only editor-build response files (`-define:UNITY_EDITOR`); a player build's
  response file gave a false `GfxClip.TraceLayer` error.

**More editor lessons (2026-10-27).**
- **The refresh hook can stall after Play stops.** The unfocused editor throttles its update loop,
  so `Temp/refresh-assets` can sit unconsumed. Compare the `Assembly-CSharp*.dll` time with your
  edit; if it is stale, ask the user to click into Unity once.
- **Check `Temp/*-error.txt` after queueing a job.** Jobs queued by other sessions can fail when
  the editor is in Play mode (`play-jin-effects-showcase-error.txt` did). Showcase and setup flags
  throw in Play ("cannot be used during play mode").
- **Editor previews skip `LateUpdate`.** Overlays that switch on there don't render in captures, so
  `RageRim.On` applies immediately; rim previews are `Validation/HitEffects/RageRim-front/side.png`.
- **Preview capture gotchas.** Synchronous HDRP previews reuse a stale skinned pose, so bake
  temporary meshes. Reset the preview light intensity on every render, or exposure drifts.
- **Re-exports change Unity inputs.** After the `body_points`/`pushbacks` exports, the five
  command-graph JSONs grew about 20% twice and Unity must re-import them; refresh before testing.
- **The game keeps running in the background** (2026-10-27, user request).
  - `PlayerSettings.runInBackground` is on: `ProjectSettings.asset` has it at 1, and the editor hook
    `Scripts/Editor/RunInBackgroundSetting.cs` re-enables it if anything turns it off.
  - `ProjectSetup` sets it true, and `Scripts/Runtime/RunInBackground.cs` sets
    `Application.runInBackground` before every scene loads.
  - Versus no longer pauses on focus loss; `OnApplicationFocus(false)` only clears held inputs.
  - Don't reintroduce focus pausing unless the user asks. Not yet checked by the user in a scene.
- **Ambiguous wording.** The user's phrasing can invert the meaning ("should stand inside the
  torso of other lying character" meant should *not*). State your reading, or ask, before
  building on it.

**Git.** The repo is owned by another Windows user: read it with `git -c safe.directory='*' ...`,
and never change global git config, commit or push unless asked. Check `git log origin/main..`
for what's unpushed. The repo has used Git LFS since 2026-10-26 (`main` = `origin/main` =
`1559ba873` then), so pre-LFS commit SHAs no longer match; push with the proxy unset and
`-c pack.useSparse=false` (character skill's git notes). On 2026-10-27 all of that day's versus
work was uncommitted. The user often says "commit/push and update the monitor". Run
`harness/compile_check.sh` first (it also scans manifest modules), group commits by topic, and
keep saved-item JSON in its own commit.
