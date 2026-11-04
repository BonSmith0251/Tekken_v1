---
name: tekken-costume-wardrobe
description: Import every costume item a Tekken 7 character can wear (upper, lower, hair, head, glasses, with hat-hair variants, lens materials and cloth/hair physics) into the UnityRemaster project's wardrobe scene, and verify it. Use this whenever the user asks to fetch, export, import or wire costumes, outfits, customization items, hats, glasses, hairstyles or clothing for a Tekken character in the Unity remaster, to add another fighter to the wardrobe, to rebuild or fix a character's wardrobe, or to add cloth/physics to worn items, even if they just say "add Lars's costumes" or "make a wardrobe for Asuka".
---

# Tekken costume wardrobe

Build a try-on wardrobe for a Tekken 7 character in the Unity remaster at
`UnityRemaster/TekkenVersusRemaster` (workspace `D:\games\Tekken.7.Ultimate.Edition.v5.10`;
confirm paths, they are hints). Jin, Kazuya and Xiaoyu are done and verified; Alisa (`asa`) and
Katarina (`ltn`) were added 2026-10-25 (see "Alisa" below). A new character is a new config entry in the existing
pipeline, not a new pipeline.

## What already exists

| Piece | Path |
|---|---|
| Exporter (game → JSON/PNG) | `UnityRemaster/export_wardrobe.py --character <code>` (`CHARACTERS` table); `export_jin_wardrobe.py` is a Jin wrapper |
| Texture/material prep | `UnityRemaster/prepare_jin_costume_materials.py --source … --out …` |
| Source data | `Assets/_Project/Characters/<Name>/Source/Wardrobe/` (manifest.json, piece JSON, Textures) |
| Unity importer | `Scripts/Editor/JinCostumeImporter.cs` → `Wardrobes` config + `ImportWardrobe(character)` |
| Built assets | `Assets/_Project/Characters/<Name>/Wardrobe/` (Materials, Meshes, Prefabs, Textures; textures shared by several fighters live in `Assets/_Project/Shared/Textures`) |
| Wardrobe data asset | `Assets/_Project/Wardrobe/<Name>Wardrobe.asset` (`CharacterWardrobe`) |
| Dressing helper | `Scripts/Runtime/MuseumWardrobe.cs` (`Find`, `Dress`; accepts `SK_CH_` renderer names) |
| Physics | `Scripts/Runtime/WardrobeDynamics.cs` |
| Saved items | `Scripts/Runtime/FightWardrobe.cs` (`ForCharacter`, `DressSaved`, `Pick`, `Save`) |
| Scene + viewer | `Scenes/Wardrobe.unity`, `Scripts/Runtime/WardrobeViewer.cs` (character bar, COLORS panel), `Scripts/Editor/WardrobeSceneSetup.cs` |
| Item and hair colours | `UnityRemaster/resolve_material_colors.py` + `run_colors.sh` (resolve parent-chain defaults, then prepare all six material sets); runtime re-tint `Scripts/Runtime/CostumeColors.cs` + `Resources/CostumeTint.shader`; rules in `references/formats.md` ("Colors") |
| Scene lighting | the scene's "Render Settings" object (`StagingRenderSettings`), shared with the other runtime scenes; `WardrobeViewer` takes its lights, shadows and colours from it |
| Capture/validation | `Scripts/Editor/JinWardrobeCapture.cs` → `Validation/Wardrobe/<code>-*.png/json` (applies the runtime default colours) |
| User doc | `Docs/WARDROBE.txt` |

Codes and folders so far: `jin`/Jin (Characters/Jin/Source/Costumes), `kaz`/Kazuya (Characters/Kazuya/Source/Base), `xia`/Xiaoyu
(Characters/Xiaoyu/Source/Base), `asa`/Alisa (Characters/Alisa/Source/Base), `ltn`/Katarina (Characters/Katarina/Source/Base; face `ltn_face.json`), `nsd`/Lidia (Characters/Lidia/Source/Base; DLC, added 2026-10-29), `bob`/Bob and `leo`/Leo (Characters/<Name>/Source/Base, added 2026-10-29). Editor flags: `import-<code>-wardrobe`, `tints-<code>-wardrobe`,
`capture-<code>-wardrobe`, and `create-jin-wardrobe-scene` (despite the name, it builds the one
scene with every wardrobe). `tints-<code>-wardrobe` loads tint data into the wardrobe asset and
writes `Validation/<code>-wardrobe-colors.json` (`passed`, `recolorablePieces`, `tintMaterials`);
run it before the capture.
Saved-item files use a different code: `FightWardrobe.Code` takes `MuseumCharacter.characterCode`,
falling back to the wardrobe asset name, so the files are `jin.json`, `kazuya.json`, `xia.json`, `asa.json` and `ltn.json`.
Saves live in `Assets/_Project/Resources/FightWardrobe`. After the 2026-10-25 restructure,
`FightWardrobe.ProjectFile()` kept writing to `Assets/Remaster/...` until it was fixed the same day.
The newest saves were moved over and both sets backed up in
`UnityRemaster/Archive/FightWardrobe-backup-2026-10-25`. After moving code, grep for hard-coded
`Application.dataPath` paths.
Use `characterCode` rather than display names for any new lookup ("Ling Xiaoyu" broke a
`StartsWith("Xiaoyu")` test).

Outside the wardrobe, characters wear only the items the user saved there (by piece name, in
`Resources/FightWardrobe/<FightWardrobe.Code>.json`). Versus (both fighters), staging_test and
the museum dress the model with one random saved item per slot and ignore unsaved items and
preset costumes. Keep that for any new character and scene.

How each scene picks:

- **staging_test** uses one seed per session, so the outfit stays the same across takes.
- **Versus** picks once at scene start, not again on rematch. Hit and guard flashes must include
  the swapped-in renderers.
- **Museum** replaces its preset list with a "SAVED ITEMS" list, a **Shuffle saved items** button
  (`ShuffleSavedOutfit`) and the title "Saved items".
- **Empty slots.** An empty head or glasses slot stays empty. An empty upper, lower or hair slot
  keeps the current piece and logs a warning. A character with nothing saved falls back to its
  first preset.
- **Physics.** `WardrobeDynamics` runs in `Wardrobe.unity` and in versus. `VersusGame` builds it
  from the fighter's wardrobe after dressing, falling back to `XiaoyuSecondaryDynamics` only
  when there is no wardrobe. The museum and `staging_test` use the character's own dynamics
  (e.g. `XiaoyuSecondaryDynamics`), always built after dressing. Only Xiaoyu has such a component,
  so Alisa's and Katarina's hair and cloth don't move there. In versus they get `WardrobeDynamics` from their saved items (both have saves now). `env_scene` also dresses its fighters from the saved items. A spring retune for fights
  changes the Wardrobe scene too; see the versus skill.

**Breast flexibility.** `Scripts/Runtime/BustDynamics.cs` springs the bust chains (OPT0>OPT1_bustL,
OPT2>OPT3_bustR, SWG_OPT0/2_bust*__swing) only for female characters. The `IsFemale` gate checks
whether the character's *default outfit prefab* is skinned to the bust bones and caches the answer.
It deliberately ignores worn items: two shared tops are skinned to bust bones for Jin too. One setting, flexibility 0-1 (default 0.5), saved as `breast`/`breastSet` in the
saved-items file (`FightWardrobe.LoadBreast`/`BreastFor`). `WardrobeDynamics` owns it (Wardrobe scene,
versus) and skips bust bones in its generic springs; the museum, staging_test and versus without a
wardrobe create their own after dressing. The Wardrobe scene shows a slider under the physics
toggle. The capture reports `breastChains` per item. The user tried the slider in the Wardrobe scene on 2026-10-25 and approved the feel.

- **Saved values.** The approval came with near-stiff saved values: `breast: 0.0` for Alisa and
  about 0.1 for Xiaoyu and Katarina, far below the 0.5 default. Don't treat 0.5 as the user's preference, and
  never reset these saves when retuning.
- **No-wardrobe fallback.** The museum and staging_test call `FightWardrobe.BreastFor`, which
  returns 0.5. `VersusGame` passes `BustDynamics.DefaultFlexibility` directly.
- **Not yet re-checked.** Alisa's `breastChains` capture (`Validation/Wardrobe/asa-wardrobe.json`)
  predates the `IsFemale` gate, so re-capture her before relying on it. Breast motion outside the
  Wardrobe scene hasn't been checked by the user.
- **Mapping (our tuning, not game data).** Stiffness `Lerp(900, 110)`, retention
  `Lerp(0.78, 0.92)`, limit `Lerp(3°, 24°)`, 120 Hz split stepping with carry 0.85.

**One versus scene.** On 2026-10-27 the user had `LocalVersus.unity` retired; LocalVersus2 is the
only versus scene and keeps dressing fighters from saved items. Ask before removing anything else. The analysis found almost nothing versus-only by GUID
(only `Settings/LocalVersus.asset`); `JinRig` and the scripts shared with the museum must stay.

**Colours.** The COLORS panel shows the worn item: one row per used colour slot (or "Hair
color"), a 24-colour palette, RGB sliders and a "Game default" reset. Items with no mask that
aren't hair say "This item has no color options". Save items stores the colours in the same file,
as a `colors` array of `{piece, slot 0-3, sRGB hex}`. `FightWardrobe.DressSaved` applies them,
plus the game's default tints that the textures don't bake, so the museum, staging_test and
versus show them. The user confirmed this end to end on 2026-10-23 (picked colours appear in the
other scenes). How accurate the colours are against the game is still unconfirmed.

The saved files are user data: the user ticks items, picks colours and presses **Save items**. Never regenerate
or overwrite them during a re-import, and commit them only along with the user's other changes.
Player builds package only saved items (added 2026-10-28; a LocalVersus2 build that day dressed
Alisa and Katarina from saved items with no errors). Build from the open editor with
`Temp/build-localversus2` (file content = output folder; empty = the default
`Builds/LocalVersus2` on D:, where the user wants the one build to live). A build is about 7.2 GB:
check free space first (D: once ran out mid-build; the temporary C: copy was deleted at the user's
request). A failed build skips OnPostprocessBuild: the flag handler removes the copies in `finally`,
but a failed build from the menu item leaves `Resources/Wardrobe` until the next editor load.
- **Verify a build** with `LocalVersus2.exe --smoke-test -screen-width 1280 -screen-height 720
  -screen-fullscreen 0 -logFile <log>` and grep the log for `"<Name> fight outfit:"` and "not packaged"
  errors. Don't rely on the Editor.log Build Report: an incremental build can omit its "Used Assets"
  list. Outfits are random picks from the saved items, so they differ between runs.
- Unverified: in one capture Katarina's trousers looked white (her saved colours, or the jersey covering them).
`Scripts/Editor/WardrobeBuildPackager.cs` (IPreprocessBuildWithReport) copies each wardrobe into
`FightWardrobe.PackagedFolder` (`Resources/Wardrobe`), nulling prefab, dependency and tints of
unsaved pieces but keeping names (`Piece.dependencyRenderer` keeps the hat-hair renderer name). It
deletes the copies after the build, or on the next editor load. The editor's `ForCharacter` skips
that folder. `MuseumWardrobe.Dress` keeps a selected piece without a prefab if the model already
wears it, and throws otherwise. Report: `Temp/package-wardrobe` →
`Validation/Wardrobe/build-package.json` (sizes beyond the MuseumCharacter's own dependencies;
first run: Jin 194→47 MB, Kazuya 349→198, Xiaoyu 411→122, Alisa 473→136, Katarina 452→113).
The MuseumCharacter assets still pull every preset outfit into builds (stripping them was offered; the user didn't answer). Builds save their own
selections under `persistentDataPath/FightWardrobe`, limited to the packaged items.

The wardrobe is deliberately a separate scene; the user had it taken out of `CharacterMuseum`.
Do not wire it back in. Character import, animation, staging and face/eye material fixes belong
to the `tekken-character-remaster` skill (`~/.codex/skills/tekken-character-remaster`, read
manually).

Read `references/formats.md` before changing the exporter or importer: it has the game data
layouts (CharacterItem assets, Head_Socket math, cloth flags) and the gotchas found so far.

## Adding a character

1. **Prerequisite: the character's outfit sets.** The wardrobe reuses the museum import's
   `Characters/<Name>/Source/Base` (Jin: `Source/Costumes`) (face skeleton `<code>_fac.json`, `manifest.json` with costumes, pieces and
   materials) and its built materials folder. If the character has no museum import yet, that
   comes first; say so rather than inventing a face skeleton. The outfit-set importers
   (`JinImporter`, `KazuyaImporter`, `XiaoyuImporter`) still update existing meshes with
   `CopySerialized`, which keeps stale vertices (see formats.md). Rebuild in place before relying
   on re-exported outfit pieces.

2. **Inventory from the game's own customization table** —
   `Character/Item/CharacterItem/<CODE>/*/CI_*.uasset`, one asset per selectable item. Slot comes
   from `ItemPosition` (not the folder). Do not scan `Item/Meshes` for meshes instead. The
   exporter already skips and reports: CIs without a mesh, developer test meshes (`*test*`, e.g.
   Kazuya's cloth-test sheets), and a second CI pointing at a mesh already listed. DLC characters'
   items are among the AES-encrypted entries in the `_P` patch paks. Read through `PakSet` (the
   `_P` paks override 7,781 base files), and use t7extract's `--tekken-key` or the copies in
   `decrypted/assets`. Never search for keys; the user supplies them.

3. **Export.** Add an entry to `CHARACTERS` in `export_wardrobe.py`: customization folder code,
   costume source, output folder, face JSON, mesh renames, and `names` for own items whose
   derived name is wrong (default hair is named after the first outfit that wears it; variants
   get CI-derived names like "1p A"). Run it, then prepare. Check the summary: counts per slot,
   skipped list, materials with no diffuse, unassigned material slots, duplicate display names.
   Re-running Jin must leave his manifest unchanged.

4. **Import.** Add a `WardrobeCharacter` to `Wardrobes` in the importer (and a menu item). Match
   the character's own costume importer: e.g. Xiaoyu needs `weldTriangleSoup`,
   `localAnonymousBones` and extra double-sided tokens. The user keeps Unity 2023.2 open: drive
   jobs through `Temp/` flags; before touching one, confirm `Assembly-CSharp-Editor.dll` is newer
   than your last script edit and that no `error CS` follows the last reload in Editor.log. If the
   DLL doesn't update, create `Temp/refresh-assets`: `Scripts/Editor/AssetRefreshHook.cs` makes the open
   editor refresh and recompile. It waits while Play is on, so then ask the user to stop Play.
   Claude can't focus Unity itself, because computer-use resolves "Unity" to a separate 6000.6
   install. A tints run reimports the `_M` mask textures as linear (`sRGBTexture=false`) and
   rewrites their `.meta` files under `Characters/*/Source/*/Textures`, so check `git status`
   again afterwards. `run_colors.sh` loops over Jin, Kazuya, Xiaoyu and Alisa. Write `.sh` files
   with `newline='
'`: Python's `write_text` on Windows turned it CRLF and broke it. Editor.log is
   `%LOCALAPPDATA%\Unity\Editor\Editor.log`. A failed flag writes `Temp/<flag>-error.txt`, so poll
   for that and for the result JSON. Imports of ~1,000 textures take several minutes; run the wait
   in the background.

5. **Scene.** Recreate the scene with the create flag so the viewer lists every wardrobe.

6. **Other scenes.** `FightWardrobe.ForCharacter` finds any wardrobe by its museum character, so
   museum/staging/versus pick it up automatically. Check any character-specific dynamics are
   built after dressing. The museum dresses inside `LoadModel`, before Xiaoyu's dynamics.
   `staging_test` creates the dynamics per take after dressing, calls `Reset()` after the first
   `Present()`, ticks with unscaled time and freezes while paused.

7. **Verify, then report honestly.**
   - Run the capture flag; read `Validation/Wardrobe/<code>-wardrobe.json`.
   - Look at every contact sheet yourself. Binding errors show as exploded or offset meshes;
     also look for stray geometry, wrong hat hair, opaque lenses, items on the wrong body part.
   - Audit the new materials and meshes, because HDRP conversion defects stay invisible until
     later:
     - no all-zero `*_MaskMap.png`;
     - no SpecularColor material (`_MaterialID` 4) without a `_SpecularColorMap`;
     - alpha overlays transparent;
     - vertices per triangle not exactly 3.00 (triangle soup), except hair and feather cards.
   - Re-capture an existing character after shared-code changes to catch regressions.
   - After any material or colour change, check that cloth still runs. The "(colors)" instance
     suffix once silently switched off cloth on 5 garments (Jin's longcoat, the Tattered set);
     the capture caught it with "cloth sections were not simulated".
   - Physics only runs in Play mode; ask the user to play the scene for that. Captures with cloth
     off hide skin ballooning, so check renderers for `*_skin_*` sections next to cloth. Flared
     skirts look like flat discs in static captures; judge them in Play.
   - Update `Docs/WARDROBE.txt`. Tell the user what was verified, what is approximate, and that
     characters with nothing saved fall back to their first preset (with console warnings).
   - Report the real git status (`git log origin/main..` for what's unpushed). The repo is owned
     by another Windows user, so use `git -c safe.directory='*' …`; plain `git` failing doesn't mean
     nothing can be committed. Wardrobe work has been committed and pushed to `origin`
     (`BonSmith0251/Tekken_v1`) only when the user asked; commit or push only on request.
     TortoiseGit's `TGitCache` intermittently blocks `git add` ("unable to write new index file").
     Loop the retry: staging has needed up to 20 attempts and committing up to 9. Each fighter adds about 0.4–0.8 GB, 90% PNGs (Katarina 789 MB). The repo is on Git LFS
     since 2026-10-26, so PNGs go to LFS automatically; push with the proxy unset and
     `-c pack.useSparse=false` (character skill's git notes). The global `~/.gitconfig` sets
     `http.postbuffer=524288000`; override it per command, never edit the global config.
     Commit saved-item JSON as its own commit; the user's ticks and un-ticks are their data.

## Alisa

- Her forearms and hands are separate `EXTRA` CharacterItems (`exp_*_parmL/R`), paired with upper
  items by name only, often abbreviated (`coat_mitt` for `coat_mitten`). `export_wardrobe.py`
  (`forearms` config key) merges each upper's pair into the upper piece, sharing bones by name;
  an upper with no pair gets her bare forearms `exp_ude`. `CharacterWardrobe.Piece.replaces` lists
  her outfit-set forearm renderers so `MuseumWardrobe.Dress` hides them.
- Her `ATH_*_Btop` detach-point bones must follow her neck, head, upper arms and hips
  (`AlisaImporter.AttachDetachPoints`; config `detachPoints`). Item prefabs get the same re-hang,
  or `Attach` sees a different rest and gives every item its own copy, which splits the seams again.
- Glasses need her own skeleton asset, which lives in her mesh folder
  (`Item/Meshes/asa/Base/asa_base/Meshes/SKT_CH_asa.uasset`; the `skeleton` key now takes a full
  pak path). With the base skeleton's Head_Socket they sat beside her head, turned 90°, like Kazuya's.
- The game files her robot bases (`CI_asa_base`, `CI_asa_1p_base_old` → `exp_1P*`) under HAIR;
  the export skips `exp_*` meshes outside EXTRA. Wearing one showed her bald.
- Her Default 1P headpiece (`asa_hed_1P`) swaps her hair for `asa_har_1p_H`, so her default outfit
  already wears a hat-hair variant. `JinWardrobeCapture` now expects the variant in that case.
- Some items point at a base `Material` instead of an instance (robot parts: `danmen`, `hane`,
  `saw`); the exporter now reads those with no instance parameters.
- After the 2026-10-25 restructure, re-running an export recreates textures that now live in
  `Shared/Textures` (Jin: 605). Delete the byte-identical untracked copies afterwards.
  `prepare_jin_costume_materials.py` now also looks in `Shared/Textures` and follows
  `Validation/restructure-map.json` for merged duplicates. Re-running the raw export overwrites
  the prepared manifest (unity/tint fields); re-run the prepare steps or restore it.

## Katarina

- A straightforward config entry: base skeleton (her glasses land on her eyes), no forearm or
  detach-point handling, `weldTriangleSoup` for her Loose shorts.
- Her default glasses `CI_ltn_gla_1p` reference an `ItemBlueprint` instead of a mesh. The exporter
  now resolves the Blueprint package, takes its `StaticMeshComponent.StaticMesh`
  (`SM_CH_ltn_gla_1p_A`) and stops if the component carries a Relative* transform it doesn't
  apply. The Blueprint's `BP_KnockedOff_*` sibling (glasses flying off in a fight) is not used.
- `WardrobeViewer.LoadStance` now resets `frame`; switching from a character with a longer stance
  (Alisa's 550 frames) threw IndexOutOfRangeException in `Pose`.

## Lidia

- Plain config entry (base skeleton; her glasses land on her eyes). Her items read from the
  encrypted `_P` paks through t7extract's built-in key like any other.
- `CI_nsd_hed_1p` is a head item with no mesh, only a `DependencyMesh` hair; the export skips it.
- Six of her head items (tiaras, crowns, oni mask) depend on `nsd_har_1p_off`, her headband-off hair.
  The exporter's base-hair rule now strips `_off` as well as `_A`..`_Z`; with only the letter rule the
  swap never happened and the capture failed them ("expected 2 renderers, found 1").
- On a machine short of commit memory, run the prepare scripts with `OPENBLAS_NUM_THREADS=1`.

## Bob and Leo

- Plain config entries (base skeleton, `<code>_fac.json`). Bob: 110 items (16/15/17/26/32 plus 4 hat-hair
  variants); Leo: 122 (18/18/19/32/32 plus 3). Each skips only `CI_<code>_bdu_t_yamasa_y` (no mesh).
- Bob's drink hat comes as three separate head items (`CI_bob_hed_drinkhat_IA`, `_IA_a`, `_IA_b`),
  each its own skeletal mesh; only `_IA` swaps his hair (to `bob_har_1p_D`).
- Bob's stubble (`MI_CH_bob_fah_1p`, `_old`) is a soft alpha overlay; opaque it covered his jaw in a grey slab.
  `HdrpProjectSetup.IsEyeShadow` now matches `_fah_`, so the importers and `Temp/fix-eye-shadow` make it transparent.
- Colour masks: the game layers them (Color02 under everything, then R Color01, G Color03, B Color04).
  Evidence: Jin's black-masked trousers set only Color02 (black); Kazuya's sash Color02 purple; Lidia's
  yellow-masked belt Color03 black; Bob's jacket yellow roses Color03 red, magenta leaves Color04 green;
  Bob's yellow gloves Color03 charcoal, as in his select art. The older weighted mix (black = Color04,
  overlaps averaged) still serves the other fighters. Bob alone is opted in: `prepare_bob_materials.py`,
  `prepare_jin_costume_materials.py --layered-tint`, `Tint.layered` -> `CostumeTint.shader _Layered`.
- Bob's Default 1P gloves and wrist cords share his shirt's cloth section. With physics on they were
  simulated and skinned by Unity Cloth (no non-uniformly scaled helpers), so the hand stretched into
  spikes. `WardrobeDynamics.ConfigureCloth` now pins vertices on forearm/hand/finger bones and moves
  only triangles with a free vertex to the cloth renderer; the pinned rest keeps the rig's skinning.
- Both were built in the same pass as their import, per the standing rule that every new fighter gets
  a wardrobe.

## Approximations to disclose

Lens opacity (fixed clear/sunglass tint), colour accuracy against the game (the formula matches
the bakes that were checked), cloth freedom
estimated from garment height (APEX assets not decoded), spring settings retuned for fights,
unconfirmed by the user:
- a per-frame split update instead of fixed 1/60 steps;
- strands follow 80% of the root translation;
- stiffness: fringe 320, hair 130, other 190;
- limits: 28°, 60°, 40°;
- retention: 0.86, 0.90, 0.88;
- hair detected by `_OPT`, `Head_` and piece-prefixed bone names.

The first version was called "too glitchy". Other approximations: glasses offsets that depend on hair (`IOV_*`) not applied, full-body /
full-head / accessory items not included unless asked, player builds ship only saved items,
and no skirt waist suspension or seek reset in `WardrobeDynamics` (the museum's Xiaoyu dynamics
has both).
