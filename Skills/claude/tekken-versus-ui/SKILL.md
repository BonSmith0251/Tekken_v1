---
name: tekken-versus-ui
description: Work on the UnityRemaster's original Tekken 7 battle UI - the life gauges and their damage/red/rage effects, timer and round pips, portraits and names, the ROUND / READY / FIGHT / K.O. / DOUBLE K.O. / FINAL ROUND / TIME UP / PERFECT / DRAW calls, the character select screen and the VS loading screen - rebuilt from the game's Scaleform (GFX/AS3) movies. Use this whenever the user says a HUD element or round call looks or times wrong ("the gauge is too long", "FIGHT doesn't fly in like the original", "the red damage drains too fast", "ROUND shows (0)", "the K.O. looks different"), asks to rebuild, decode, re-export or re-time any battle UI from the original game, wants a new fighter on the select or VS screen, or pastes a screenshot or clip of the HUD.
---

# Tekken versus UI

The battle UI of the Unity remaster at `UnityRemaster/TekkenVersusRemaster` (workspace
`D:\games\Tekken.7.Ultimate.Edition.v5.10`; Unity 2023.2.22f1, HDRP 16). Confirm paths; they are
hints. Everything here lives in the versus scene `LocalVersus2`; battle logic, effects and the
fight camera belong to the `tekken-versus-battle` skill, which this skill was split from on
2026-10-28. `Docs/BATTLE_HUD.txt` in the project is the detailed user doc; read it first.

## Map

| Piece | Path (under the project) |
|---|---|
| Battle HUD: gauges, timer, round pips, portraits, names | `Assets/_Project/Scripts/Runtime/BattleHud.cs` (fed by `VersusGame`) ← `Resources/BattleHud/*.png` from `Tools/BattleHud/export_battle_hud.py` (Scaleform `SW_UI_HUD_Gauge` / `SW_UI_Timer` + ReplaceImages gauge skin 000); import rule `Scripts/Editor/BattleHudTextureImport.cs` (no mips, clamp, uncompressed; `Art_`/`VsArt_` CompressedHQ at 2048) |
| Round calls (ROUND/READY/FIGHT/K.O./…) | `Scripts/Runtime/GfxClip.cs` (Flash clip player) + `Resources/BattleHud/GfxLayer.shader` (`Hidden/BattleHud/GfxLayer`, drawn with `Graphics.DrawTexture`) ← `Resources/BattleHud/GameMessage.json`, 49 images and 22 `Mask_<id>.png` from `Tools/BattleHud/export_game_message.py` (`--work …`, `--out <dir>` for isolated re-exports); `BattleHud.DrawCall` / `RoundClip` port `Round_mc.ChangeRound` |
| Decoders | `Tools/BattleHud/gfx_dump.py` (sprites, labels, placements, colour transforms, blur, blend, cacheAsBitmap, HasVisible, shape paths), `abc_dis.py` (AS3 disassembly), `motion_dump.py` (fl.motion 3D tweens from the AS3 constructors) |
| Character select → VS loading screen | `Scripts/Runtime/CharacterSelect.cs` (roster, static picks; `VersusGame.Awake` disables itself and adds it unless `CharacterSelect.Chosen`, then reloads the scene) and `VersusLoadingScreen.cs` (DontDestroyOnLoad; `Holding` gates `VersusGame.Update/FixedUpdate`; `MatchReady()` at the end of Awake; minimum hold 3.2 s) ← `Resources/CharacterSelect` from `Tools/BattleHud/export_character_select.py` (`SW_UI_CharacterSelect`, `SW_UI_Makuai`, ReplaceImages `CS_CH_*`, `VS_CH_*_TK7_DEF`) |
| Fixed-tick round-call captures | `Scripts/Editor/AnnouncementEffectsCapture.cs`: flag `Temp/capture-announcements` (contents `before`/`after`) → `Validation/Announcements/` without touching a scene; `Temp/play-announcements` enters Play in LocalVersus2 (skips a dirty scene, restores the setup) |
| Red-damage timing harness | `~/.claude/skills/tekken-versus-battle/harness/run.sh HudTrail` (needs the battle model; lives with that skill) |
| Reference clip | `C:/Users/Administrator/Videos/Captures/tk7/TEKKEN 7  2026-10-23 08-04-08.mp4` (double space) |

Controls on the select screen: P1 A/D, confirm U/Enter; P2 arrows, confirm Num4/O. Roster order
Jin, Kaz, Xia, ?, Asa, Ltn, Nsd (Lidia, added 2026-10-29). "Character Select" is also on the match-end panel and pause menu;
`VersusGame.characterSelect` (default true) turns the screen off.

## Workflow

1. **Data first, video second.** The UI is Scaleform, and its GFX/AS3 decodes fully, including the
   motion tweens. Rebuild from the data, then use the user's capture only to check offsets and cue
   timing, measured at the capture's full rate (30 fps), not 15 fps sheets. The user: "analyze the
   original game again, and disobey the video clips", then "now reference the video clips" once the
   data version existed; and generally "the video clips are the reference, not the full truth."
2. **Decode** the movie: `SW_UI_*.uasset` holds an uncompressed GFX (the gauge movie is SWF 12, 60 fps,
   at byte ~1276). Dump it with `gfx_dump.py`, disassemble its AS3 with `abc_dis.py`, and pull 3D tweens
   with `motion_dump.py`.
3. **Mock before Unity.** Render the same player in Python from the data over a reference frame. It
   catches most bugs offline (a gauge 3% too long, wrong pivots), but not Unity-only failures such as
   the `GUI.matrix` rejection.
4. **Port, compile-check** (`~/.claude/skills/tekken-versus-battle/harness/compile_check.sh`), then
   capture with `Temp/capture-announcements` and check Editor.log for "Ignoring invalid matrix".
5. **Hand to the user for Play**, naming the moments to watch. Only the user's verdict in Play counts.

## Flash playback rules a port must copy

- **3D fly-ins** live in the AS3 constructors (fl.motion `MotionBase`/`Animator3D`), not in the
  PlaceObject timeline. Composition is `T(xyz) * Rz*Ry*Rx * ScaleSkew` about the recovered
  `transformationPoint` (XY plus `transformationPointZ`) pivot. `Animator3D.setTargetState` captures the
  placement only when there is no motionArray, so don't pre-multiply it. Perspective: FOV 60 centred at
  (960,540), focal ≈ 1662.8.
- **Visibility.** Most layers are placed hidden (HasVisible) and revealed on cue.
- **Loops keep their instances.** Looping clips must keep persisting instances, or script overrides
  are lost (the ROUND digit "(0)" bug).
- **Masks** can be the union of two shapes (ignoring it gave ROUND a white sheet).
- **Frame scripts.** Follow the real stop/play/gotoAndPlay/gotoAndStop commands decoded with `abc_dis`
  (e.g. ReadyFight frame 1 holds Moji_Fight at its Fight label, frame 41 releases it), not a "named
  `*_mc` clips stop on their last frame" heuristic. A new motion span resets the previous span's colour,
  blend and blur, or an earlier hidden span's alpha 0 suppresses READY, FIGHT or ROUND.
- **Colour transforms.** In PlaceObject a colour-transform flag with no terms means alpha 1; no flag
  keeps the previous value. Blend modes come after filters in PlaceObject3.

## Unity rendering rules

- `GUI.matrix` silently rejects non-TRS matrices (Unity logs "Ignoring invalid matrix assigned to
  GUI.matrix", once 3,074 times). Projective transforms go through `GfxLayer.shader` with
  `Graphics.DrawTexture`: mul+add colour transforms in gamma space, additive blend, alpha-weighted blur,
  a union of up to 2 masks, the full 3D projection.
- GL drawing in OnGUI renders nothing under HDRP; slanted gauge fill cuts are IMGUI strips.
- Mirror by flipping rects and UVs, never with a negative-scale `GUI.matrix`.
- Drive the HUD with unscaled time so the KO near-freeze doesn't stall it.
- IMGUI can't blend additively, so the gauge's additive red copy is approximated at 96%; `GfxLayer`
  now can, so the gauges could move to it (not done).

## Screens and decisions

- **Life gauges** (decoded 2026-10-26; `Main.SetHealth`, `StartGaugeAnim`, `onRedScale`, `onDamage`,
  `RageOn/Off/End`). Port in `BattleHud.Step`:
  - shake 15×5 movie px decaying (onDamage);
  - two-layer damage frame with a hold until the shake's 3rd stage;
  - eff_damage glow 49 frames sheared via `GUI.matrix` (right side conjugated by the mirror, texture
    flipped, positive determinant);
  - red held while `f.reacting`, then a linear 24-frame drain (`HudTrail`: single hits median 30 frames
    against ~28 in the reference capture);
  - `LifeGrow` (redgrow, 35%) at 0.85 speed;
  - Rage = normal frame plus a pulsing rage outline (18-frame loop).

  Layout was measured from the reference clip at 1440 px (gauge scale 0.69, user-confirmed).
- **Round calls.** `VersusGame` timing: `ReadyTick 68`, `FightTick 108`, `KoCallSeconds 2.0`, ROUND
  lead 12, control from tick 150; rounds 2+ run ROUND → READY → FIGHT. The K.O. call plays over the
  native KO camera near-freeze (versus skill).
- **Character select and VS screen.** A new fighter needs its code in both exporters' `CHARACTERS`
  (`export_battle_hud.py`, `export_character_select.py`) and in `CharacterSelect.Roster`, plus the
  HUD's icon and name textures (`HUD_CH_ICON_?_<CODE>`, `HUD_CH_NAME_?_<CODE>`).
  DLC fighters have no `VS_CH_*_<CODE>_TK7_DEF` art, only `_7FR_DEF`; `export_character_select.py`
  maps that per code (`VS_ART`, Lidia `NSD`). Both exporters rewrite every fighter's images; the
  output is deterministic, so check `git status` shows only the new code's files.
- **Placeholder images.** `Rep_*` images in the movies are swapped at runtime from
  `UI_common/Texture2D/ReplaceImages`: HUD `HUD_GAU_LIFE_*_000`, `HUD_CH_ICON_?_<CODE>`,
  `HUD_CH_NAME_?_<CODE>`; select and VS `CS_CH_PANEL/L/R/NAME_<CODE>`, `VS_CH_L/R_<CODE>_TK7_DEF`.
- **Player builds.** `CharacterSelect.ReloadScene` uses `EditorSceneManager.LoadSceneInPlayMode` in the
  editor and `buildIndex` in a player; LocalVersus2 is first in Build Settings (then env_scene), so the
  player path should work. A player build exists at `Builds/LocalVersus2` (2026-10-29) and the user played it, but the select-to-match reload wasn't specifically checked.

## Status (2026-10-28)

- **Confirmed by the user in Play:** the gauge fills; the VS loading screen; the round calls **as
  committed in `862ab87ab`** ("They look right").
- **Pending, not judged:** Codex's later repair of the round-call renderer (pivots, real frame scripts,
  span resets, premultiplied gamma-space blending of the red echoes, the last labelled an adaptation),
  uncommitted on top of `862ab87ab`; see "ORIGINAL-EFFECT REPAIR (2026-10-28)" in `Docs/BATTLE_HUD.txt`.
- **Unverified:** the gauge effects in a match; the FINAL ROUND 12-frame lead; PERFECT timing; DOUBLE
  K.O. against the original; the 150-tick control start; the select layout and its controls; the
  player-build reload path.

## Working with the user and the editor

- The user keeps Unity open and checks each change in Play. Queue editor jobs through `Temp/` flags,
  refresh with `Temp/refresh-assets`, and wait for `Assembly-CSharp*.dll` to be newer than your edit
  (see the versus skill's "Editor jobs").
- All open Unity editors write the same `%LOCALAPPDATA%\Unity\Editor\Editor.log`; another project's
  editor (Truxton) can overwrite it, so match the project path before trusting a log line.
- Other sessions and Codex edit the same files (`VersusGame.cs`, `GfxClip.cs`, `GfxLayer.shader`).
  Commit only your hunks (temp `GIT_INDEX_FILE` or `hash-object` + `update-index --cacheinfo`),
  compile-check the committed version, and commit or push only when the user asks.
