# Game data formats and gotchas for the wardrobe pipeline

Contents: CharacterItem assets · skinned pieces · glasses (Head_Socket) · hat-hair
dependencies · materials and textures · cloth and springs · Unity import · checks.

All reads use the workspace `t7extract` package (`Source`, `Package`, `read_properties`,
`decode_static`) and the AST-loaded `decode` from `UnityRemaster/export_jin_rig.py`.

## CharacterItem assets (the inventory)

`TekkenGame/Content/Character/Item/CharacterItem/<CODE>/<FOLDER>/CI_<code>_*.uasset`, one
`CharacterItem` export each. Folder names are upper case (`JIN`, `XIA`, …). Useful properties:

| Property | Meaning |
|---|---|
| `ItemPosition` | Slot: `UPPER`, `LOWER`, `HAIR`, `HEAD`, `GLASSES` (also FULL_BODY, FULL_HEAD, *_ACC, MAKE_UP…) |
| `SourceSkeletalMesh` / `SourceStaticMesh` | Negative import index of the mesh; resolve the object import, then the `Package` import whose path ends in `/<name>` (`/Game/...` → `TekkenGame/Content/....uasset`) |
| `DependencyMesh` | Head items: hair variant worn under them (see below) |
| `ItemOffsets` | Glasses: `Transform` struct (Rotation quat x,y,z,w; Translation; Scale3D), per character |
| `MeshType` | `SkeletalMeshAnim` or `StaticMeshSocket` |
| `bDefaultItem` | The character's default for that slot |

Resolve a mesh import by following its `outer_index` chain to the `Package` import; the
package name can differ from the mesh object name (one Xiaoyu item failed name matching).

`IV_*` and `IOV_*` assets in the same folders are variation and offset overrides (e.g. glasses
moved for a particular hairstyle); they are not selectable items. Common items live under
`Item/Meshes/cmn/<Slot>/<item>/` with one fitted mesh per character (`SK_CH_<code>_…`); the
character's own items under `Item/Meshes/<code>/`.

Table quirks seen so far: developer test items (`CI_kazClothTes_*`, grid-material cloth
sheets) and two CIs pointing at one mesh (`CI_xia_har_1P` / `CI_xia_har_1p_old`).

## Skinned pieces

The decoder outputs Unity-space JSON: positions `(-x, z, y)/100`, bone rotations
`(-qx, qz, qy, qw)`, UV `v → 1-v`, at most four normalized weights; the Unity importer flips
triangle winding. Some source garments store eight influences: Jin's jacket
`SK_CH_jin_bdu_1P` has 170 vertices with more than four, and faces store four. If an item deforms
badly at the shoulders or waist, measure influence counts (`t7extract export --influences 8`
reports them, in glTF basis) before truncating. Pieces already exported for the outfit sets (`Characters/<Name>/Source/Costumes` or `Source/Base`) are
referenced, not duplicated.

Gotchas:
- Common meshes often omit `BASE` and spell bones differently (`Base`, `HEAD` vs `BASE`,
  `Head`). Seed every piece prefab with the character's full face skeleton and match bone
  names case-insensitively (Unreal does). Without this the import throws on `BASE`, or a piece
  binds to an unanimated duplicate chain.
- A section with an empty material slot (`None`) is not drawn in game. Export it as
  `MI_unassigned__<piece>` and skip it at import; showing it with a placeholder produced a
  white hood on Jin's Tekken 4 hair.
- Wrap each prefab build in try/finally and delete leftover `"<Name> wardrobe - "` roots: a
  failed build otherwise leaves objects in the user's open scene.

## Glasses (static mesh on Head_Socket)

`SKT_CH_base_Skeleton` defines `Head_Socket` on bone `HEAD` with RelativeRotation
(Pitch 0, Yaw -90, Roll 0). For each mesh vertex `p` (UE cm):

1. `p1 = rotate(ItemOffsets.Rotation, p * Scale3D) + Translation`
2. `p2 = p1 @ FRotationMatrix(socket rotator) + socket location` (UE row-vector convention)
3. `local = (-x, z, y) / 100`, then `world = HeadBind * local`, where `HeadBind` is the
   composed Unity-space bind matrix of the face skeleton's `Head` bone

Export as a rigid piece with every vertex weighted 1.0 to `Head`, so it reuses the skinned
pipeline and swap code. Sanity check: Jin's Evil Eyes landed exactly on the eyeballs
(y 1.75–1.79, z 0.08–0.11) and round glasses sat at eye height with temples running back.
UVs come from the static vertex buffer after the tangent block (8 or 16 bytes), half or full
precision per the mesh flags.

Lens materials (`*gla_*_lens`, `*gla_*_trans`): texture alpha is not opacity (it varies 0–255
with no pattern; the parent material holds opacity). Drop the base map and use a transparent
tint — clear about 0.2 alpha, `sun` names about 0.75.

Use the character's own skeleton asset when it has one: Kazuya's `SKT_CH_kaz` defines
`Head_Socket` with no rotation (the base skeleton's has yaw -90), and his ItemOffsets assume
it. With the base socket his glasses came out turned 90°. (`export_wardrobe.py` config key
`skeleton`.)

## Hat-hair dependencies

`DependencyMesh` names a trimmed version of the character's default hair (Jin: `_A` to `_I`
variants of `jin_har_1p`). Export these as hidden "variant" pieces. When dressing, if the head
item has a dependency and the worn hair is its base (`dependency` minus the trailing `_X`),
wear the variant instead. Other hairs stay as chosen.

## Materials and textures

- Resolve every material and texture from the piece's own item folder first, then the
  character's folder, then anywhere. Common items reuse names (e.g.
  `MI_CH_cmn_har_hair_taba001_1` in many hair folders) with different contents: suffix
  colliding names with the item folder.
- Some common items keep materials in a folder spelled `Matelial`. Match `/Mat`, not
  `/Material/`; otherwise those materials silently fall back to having no diffuse.
- The character's skin/face/outfit materials already exist from the outfit sets; reuse them
  unless the piece's own folder has a material of the same name.
- Common items' colors are customization tints (mask channels × Color01–04 params); the
  defaults are mostly neutral greys. That is expected, not a bug. The same palette rule (mask RGB
  plus remainder, × Color01–04, in linear space) applies to character outfits too; Xiaoyu's own
  outfit was first imported without it.
- Eyes are not tinted that way. `T_CH_eye_S` separates the iris from the sclera, with Color01
  tinting the iris and Color02 the sclera. One Color01 over the whole eyeball turned Xiaoyu's eyes
  red. Eye materials belong to the character import, not the wardrobe.
- Re-running a prepare script does not rebuild an existing `*_MaskMap.png`; it is built only in
  `ConvertMaterial`. Reconvert the material after regenerating its surface texture.

## Cloth and springs

- The skeletal decoder skips each section's APEX cloth mapping array (`array_skip(64, …,
  "cloth")`). Patch the AST-loaded decoder to keep that count; sections with a non-zero count
  are the ones the game simulates as cloth. Record them per piece by material name.
- Jin has 7 cloth garments (Long Coat, School Uniform jacket, Emperor jacket cloth, Tattered
  hood/cloth/trousers, Tattered off). Many more pieces have `_OPT` helper bone chains (hair
  strands, hachimaki/Santa tails, belts, armor flaps) which get springs.
- `WardrobeDynamics` splits cloth sections onto their own renderer (Unity Cloth cannot skin the
  rig's non-uniformly scaled helpers), pins from the waist or the section top, and collides
  with torso, leg and upper-arm capsules.
- Unity creates cloth particles lazily (not in edit-mode previews); apply max distances once
  `cloth.coefficients` is non-empty, never assume it is ready at `AddComponent`.
- Any mesh/material cleanup component must use `DestroyImmediate` outside Play mode, or editor
  captures log errors.
- Cloth modelled flared at bind pose (Xiaoyu's Formal skirt) needs a max distance of at least its
  outward offset from the body, or it cannot drop onto the legs (`WardrobeDynamics.cs`, around
  line 108). The user confirmed the drape in Play; static captures show a flat disc.
- Not yet in `WardrobeDynamics`; the museum's `XiaoyuSecondaryDynamics` has these:
  - **Skirt suspension.** Hem vertices weighted to thigh and hip helpers flare sideways even
    with cloth off. Blend those weights toward a skirt-suspension bone that follows the pelvis
    position and `Base` rotation.
  - **Waistband pinning.** Panels sharing the skirt material are pinned when their minimum
    y is above about 1 m.
  - **Seek reset.** Reset on timeline seek, not on loop wrap.

  Port them if wardrobe skirts misbehave.
- A copied project (Codex's `UnityRemaster/CategoryBuild`) needs `com.unity.modules.cloth` in its
  package manifest.

## Unity import and checks

- Rebuild an existing mesh asset in place (`Clear()` then assign). `EditorUtility.CopySerialized`
  from a new mesh updated the bounds but kept the old vertex buffer, so re-exported geometry
  silently never reached Unity. To audit, decode `_typelessdata` positions from the `.asset` and
  compare with the piece JSON.
- Rest poses are per mesh: a helper bone shared by name can rest differently in two items.
  `MuseumWardrobe` gives such helpers a piece-local copy (animated bones stay shared), so dress a
  model before posing it.
- `PreviewRenderUtility.InstantiatePrefabInScene` returns a connected prefab instance; unpack it
  before removing renderers.

- `HdrpProjectSetup.ConvertMaterial` converts Standard to HDRP/Lit. Afterwards, any overlay whose
  diffuse alpha matters needs a transparent surface: face overlays (`*_eye_shadow`, `*_eye_corner`,
  via `MakeEyeShadowTransparent`; `IsEyeShadow` matches both) and lenses (`MakeTransparent`).
  An opaque `*_eye_corner` showed as pink-red patches.
- `ConvertMaterial` also guards two silent defects; re-check after big imports:
  - **Black masks.** HDRP's GPU mask combiner writes all-black `_MaskMap` once HDRP is active, so
    the mask is now built on the CPU.
  - **Stale `_MaterialID`.** A leftover SpecularColor `_MaterialID` (4) on a reused material with
    no specular map renders as a white mirror, so it is now reset to 1.
- The outfit-set importers (`JinImporter`, `KazuyaImporter`, `XiaoyuImporter`) still use the
  `CopySerialized` mesh update described above. Only `JinCostumeImporter` rebuilds in place.
- A method with an optional parameter no longer converts to `Func<>` as a method group; use a
  lambda.
- The capture renders each item on the default outfit in a `PreviewRenderUtility` scene:
  `Dress`, build `WardrobeDynamics`, then check renderer count (piece plus hat-hair variant),
  unbound bones, missing materials, bounds, and that expected cloth was created.

## Colors (customization tints)

- Customizable materials tint the diffuse by Color01..04 through the item's mask (RGB pick 01..03,
  remainder 04, alpha blends): `albedo = diffuse * (1 - m.a + m.a * Σ w_i C_i)` in linear space.
  The material-function expressions are stripped from the cooked build; this formula matches the
  bakes that were checked against the game.
- Instances set only some colors; the rest come from the parent chain (`Parent` → instances →
  base material's `MaterialExpressionVectorParameter` defaults, e.g. M_CH_Base_machieru Color02 is
  light green). `resolve_material_colors.py` fills them; without it most common items stayed grey.
  Body/face materials (eye, skin, teeth, mouth, tear, brow, lash) are excluded.
- Hair has no mask: the greyscale hair texture times Color01 (Xiaoyu's own hair carries a generic
  `T_CH_MIBase_M` mask and is `_hair_`-named). The user wants hair black by default, including the
  hair color of masked hair items. In hair-named masked materials (`MI_CH_cmn_har_*`, e.g.
  two_block2, slick_dreads) only Color01 is forced to black. The second colour keeps the game
  default: a white streak on the emo bob, purple on the two-block. `tint_info` needs the material
  name to detect these. Forced to black: Jin's wardrobe 2, Kazuya's 2, Xiaoyu's 12.
- Mask (`_M`) textures must be imported linear: `JinCostumeImporter.TintTexture` sets
  `sRGBTexture=false`.
- Kazuya's and Xiaoyu's outfit sets come from their own prepare scripts (Kazuya: no tint; Xiaoyu:
  missing colors as white, special eyes); never re-prepare them with Jin's script. Use
  `--tint-only` (records `unity.tint` without touching images; `baked` says whether the bake already
  includes the default tint) and let the runtime tint unbaked materials.
- Runtime: `CostumeColors.Apply` re-tints into a per-model material instance named "<name> (colors)";
  anything matching materials by name (cloth sections) must use `CostumeColors.SourceName`. Hat-hair
  variants alias their base hair's tints and colors. JsonUtility cannot read nested arrays, so tint
  colors are stored flat (12 floats).
