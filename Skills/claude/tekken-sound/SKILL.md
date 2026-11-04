---
name: tekken-sound
description: Extract, decode and wire Tekken 7's original audio (Wwise banks) in the UnityRemaster project - fighter voices (attack, pain, death, entrance and victory lines with their subtitles), hit/whoosh/guard and other battle sounds, the announcer, footsteps, Rage cues, stage music with its final-round switch, and the character-select sounds - and add a new fighter's or stage's sound. Use this whenever the user asks for sound, audio, SFX, BGM, music, voices, shouts, announcer, "conversations"/lines or subtitles in the remaster, says something sounds wrong or is silent ("no hit sound", "the music doesn't change in the final round", "Leo has no name call", "Lidia doesn't shout"), wants to extract or decode .bnk/.wem audio, or adds a fighter and needs their voices, even when they only paste a clip or name a character.
---

# Tekken 7 sound in the remaster

Workspace `D:\games\Tekken.7.Ultimate.Edition.v5.10` (hints; confirm paths): game `TEKKEN.7`, extractor
`t7extract`, decrypted tkdata `decrypted/tkdata`, Unity project `UnityRemaster/TekkenVersusRemaster`
(Unity 2023.2.22f1, HDRP; the user keeps it open). Versus-scene rules (one issue at a time, harnesses, compile check,
editor flags) are in the `tekken-versus-battle` skill; read its "Editor jobs" section before queueing Unity work.
The user wants everything "based on the original game": wire from data, label anything chosen by us as a remaster
choice in code comments and in `Docs/BATTLE_SOUND.txt`, and never present a guess as original.

## Map

| Piece | Path |
|---|---|
| Wwise bank reader (v118 HIRC: events, actions, sounds, switch tables, FNV-1) | `t7extract/t7extract/wwise_hirc.py`, test `t7extract/tests/test_wwise_hirc.py` |
| .wem/.bnk → .wav/.ogg (PCM, IMA ADPCM, Vorbis) | `python -m t7extract audio <src> -o <out> [--format wav]` |
| Battle sound exporter | `Tools/BattleSound/export_battle_sound.py` → `Resources/BattleSound/*.wav`, `sounds.json`, `moves_<code>.json` |
| Music exporter | `Tools/BattleSound/export_battle_music.py` → `Resources/BattleMusic/<track>__normal/final.ogg`, `music.json` |
| Sound player (per scene; voices on one channel per fighter) | `Scripts/Runtime/BattleSound.cs` |
| Music player (survives scene reloads, crossfades, FinalRound) | `Scripts/Runtime/BattleMusic.cs`; import rule `Scripts/Editor/BattleMusicImport.cs` (streaming) |
| Versus hooks | `VersusGame.cs`: `PlayHitSound`, `PlayMoveSound`, `Announce` (+ music final switch), `SayTake` + `DrawSubtitle`, `UpdateWinnerCall`, `BattleMusic.Instance.PlayStage` in `Awake` |
| Select screen hooks | `CharacterSelect.cs` (CharaSelect track, menu tones, name call, `Sting` for SE_VS1) |
| Settings asset (volumes by channel, music choice, final-round music, subtitles) | `Scripts/Runtime/SoundSettings.cs` → `Resources/SoundSettings.asset` (player: `persistentDataPath/sound-settings.json`); players read `SoundSettings.Active` (the settings scene's working copy while it is open) |
| Settings scene `Scenes/sound_settings.unity` (volumes, music choice, audition every track and sound) | `Scripts/Runtime/SoundSettingsScene.cs`; built by flag `Temp/create-sound-settings-scene` (`Scripts/Editor/SoundSettingsSetup.cs`, menu Remaster > Sound) |
| Unity validation (flag `Temp/validate-battle-sound`) | `Scripts/Editor/BattleSoundValidation.cs` → `Validation/BattleSound/validation.json` |
| Headless check | `~/.claude/skills/tekken-versus-battle/harness/run.sh Sounds <project>` |
| Name recovery | `scripts/wwise_names.py` (this skill) |
| User doc | `Docs/BATTLE_SOUND.txt` (regenerate commands, data vs choices, gaps) |

## Regenerate

From `t7extract` (patterns go **before** `-o`; check free space on C:/D: first; scratch output in the session
scratchpad):

```
python -m t7extract unpack ../TEKKEN.7 "*/WwiseAudio/Windows/AkBankBattle.bnk" "*/WwiseAudio/Windows/AkBankFoot.bnk" "*/WwiseAudio/Windows/AkBankBGM.bnk" "*/WwiseAudio/Windows/AkBankVoiceJin.bnk" ... "*/Content/Sound/Common/*" "*/Content/Sound/Character/*" -o <src> --tekken-key
python -m t7extract audio <src> -o <wav> --format wav
python <P>/Tools/BattleSound/export_battle_sound.py --source <src> --wav <wav> --t7extract . --movesets ../decrypted/tkdata/mothead/bin --subtitles ../decrypted/tkdata/subtitles
python <P>/Tools/BattleSound/export_battle_music.py --t7extract . --source <src> --list-media > media.txt
python -m t7extract unpack ../TEKKEN.7 --paths-file media.txt -o <bgm> --tekken-key
python -m t7extract audio <bgm> -o <ogg>
python <P>/Tools/BattleSound/export_battle_music.py --t7extract . --source <src> --audio <ogg>
```

Then `Temp/refresh-assets`, wait until `Library/ScriptAssemblies/Assembly-CSharp-Editor.dll` is newer than your
edits, then `Temp/validate-battle-sound`. Run the bash commands from the project root, never from inside
`Resources/BattleSound`: a shell sitting in the folder blocks it.

## How the original wires sound (verified)

- **Banks** are Wwise v118. HIRC node layout: Sound = plugin u32, stream u8 (0 in bank, 1/2 streamed loose
  `<id>.wem`), source id u32, size u32, bits u8, then node base params: override-FX u8, FX count u8 [bypass u8 +
  7-byte slots], **override-attachment u8**, override bus u32, direct parent u32. Containers start straight at the
  base params. A switch container's children list (u32 n + ids) follows unparsed params; the 10 bytes before it are
  group type u8, group id u32, default switch u32, continuous u8, and the switch table follows (u32 groups, then
  switch id, item count, item ids). Switch targets may live in another bank (most BGM jukebox tracks do).
- **Names**: every event/group/value ID is FNV-1 of the lower-case name. Sources of names: `Content/Sound/**` asset
  names, sound managers' imports, executable strings (ASCII + UTF-16), decrypted tkdata (the BGM_Selector stage names
  like `stg29_uyuni_vr` came from there). `scripts/wwise_names.py` does the hashing.
- **Fighter managers** `Sound/Character/<code>/BP_<code>CharacterSoundManager`: `Sounds` (SoundID global number +
  event, attack → damage → death order) and `Streams` (entrance `xx_s##`, victory `xx_w##`, named like the staging
  takes). Banks: `AkBankVoice<CODE>` (`Jin` keeps its casing).
- **Common manager** `Sound/Common/BP_TekkenCommonSoundManager`: `Snd*` announcer properties, `BattleSysTones`
  (544 system/UI/story/Rage tones), `FighterNameVoices` (by character id), `ItemTones`, `SoundHolder`
  (select/VS screen SEs).
- **Move sound properties** 0x84C4 (own) / 0x84C6 (opponent), value `category << 24 | index`: 2/3/4 the fighter's
  n-th attack/pain/death voice; **1, 6, 7, 8 index the common battle table** = the executable's static
  `/Game/Sound/Battle/...` reference list in order (`TONES` in the exporter; found as UTF-16 strings near
  0x27EF1D8 in `TekkenGame-Win64-Shipping.exe`, interleaved with old voice paths); 15 indexes `BattleSysTones`;
  12 + 0x2000 the fighter's `_item<nn>` lines; 9 floor hits. Category 6 is only WND_MD01/02 on attack startups,
  7 impacts on reaction moves' frame 1. **Category 10 is unidentified** (indices 0/1).
- **Move sound list** (moveset voiceclip block, move offset 0x78, entries until 0xFFFFFFFF, same encoding): an
  attack's contact set (swing 6, impact 7, shout 2:n, 2:999 = any shout), played on connection; guard reactions
  (`sGrd_*`) list 1:26 = GRD_MD01. Moves with list shouts often also shout through 0x84C4 2:n, so the list is not a
  move-start set; play its shout only when the attacker is silent.
- **Music**: `BGM_Container` → switch on state `BGM_Selector` (373 values) → stage track = switch on `FinalRound`
  (Normal/Final) → streamed media. Also layered `SuperCombo` and `FatalitySlow` switches (not wired). stg21/22 have one
  arrangement; stg23/24 normal tracks are in the encrypted patch paks (need `--tekken-key`). Tracks are 5.1, 48 kHz.
- **Subtitles** `decrypted/tkdata/subtitles/<CODE>/us/<CODE>_sub.bin`: line key, key, English text (also
  `xx_ss00/xx_spstart01` special-start keys, not used yet).
- **Special cases**: Leo's name call is `syvo_name_uni` (in `Sound/Voice/leo`); `NAME_CALLS` in the exporter.
  Battle-bank prefetch copies of streamed sounds are skipped by `t7extract audio` (the full loose .wem is used).

## Decisions already made (remaster choices; keep unless the user asks)

- Clean hits whose attack lists no impact fall back to punch/kick by damage tier (limb = nearest striking bone);
  ~7% of clean hits in CPU play.
- The struck fighter's pain/death cry on every clean hit (tiered by damage) — native in the original.
- Power-crush absorb plays `Guard01`; category 9 plays `RES_BANK_DWN_HVY01-03`.
- Music: generated arena / stg17 / stg25 / stg90 → `stg11_arena`; stg08a → stg08; 1.5 s crossfade, volume 0.5, music
  ignores pause and the KO near-freeze (unscaled time). The final-round arrangement starts with the ROUND call.
- Select screen: `SE_Select` cursor, `SE_Decide` confirm (+ the announcer's name call, which the original does),
  `SE_Cancel` back, `SE_VS1` when the VS screen shows.
- Channels: `Play` = effects, `Say` = voices, `Announce` = announcer (round calls, K.O., name calls, Play_Wins); music
  `SoundSettings.Music`. Defaults: master 1, music .5, effects 1, voices .9, announcer 1, stage's own track, final-round
  music on, subtitles on. "Off" also silences the select-screen music.
- Subtitles (switchable in the settings): white text with a shadow at the bottom while the line plays (entrance and victory takes).
- Exporters replace files in place and prune stale ones with their `.meta`: never `rmtree` the Resources folders
  (that regenerates every GUID).

## Adding a fighter

1. Add `code: "BANKSUFFIX"` to `FIGHTERS` in `export_battle_sound.py` (bank `AkBankVoice<SUFFIX>.bnk`); add a
   `NAME_CALLS` entry if the name call isn't `syvo_name_<code>` (check `FighterNameVoices` and `Sound/Voice/<code>`).
2. Unpack that bank and `Sound/Character/<code>/*`, re-run both conversions and the exporter; the moveset must be
   `<code>mot.bin` in the movesets folder; DLC subtitles live under the same `subtitles/<CODE>/us`.
3. Add the code to `SoundCodes` in `CharacterSelect.cs` (select-screen name call) and to the `codes` list of the
   `Sounds` harness. Run the harness (all sound properties should resolve except category 10) and the validator.

## Verify

- `harness/run.sh Sounds <project>` — baseline (2026-10-29, 8 fighters, 68 CPU minutes): 93% of clean hits use the
  attacker's own impact; sound properties with a clip: cat 1 317, 2 903, 3 6, 6 32, 7 27, 8 246, 9 300, 12 699,
  15 18; without: cat 10 121.
- `Temp/validate-battle-sound` — baseline: passed, 429 sound clips, 371 events, 58 tones, 8 fighters, 63 music clips
  (123 min).
- The user judges the mix in Play (levels, timing, music changes); say what wasn't heard.

## Not done yet (ask before building)

Stage ambience and crowds (`AkBankstgNN`, Sound/Stage/StgNN), destructible floor/wall sounds, the SuperCombo /
FatalitySlow music layers, story-mode drama lines (StoryMode banks), the older-game jukebox (other banks), other
voice languages, category 10, Wwise loop points, bus volumes and ducking, the museum and staging_test scenes (no
sound there yet).
