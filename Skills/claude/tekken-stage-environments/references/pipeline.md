# Stage pipeline: formats and rules

Everything here was verified against the code on 2026-10-26. Re-check line-level details in
`Tools/import_museum_stages.py` and `Scripts/Editor/MuseumStageImporter.cs` before relying on them.

## Source data

- Maps: `TekkenGame/Content/Maps/stgNN_geom.umap` plus `stgNN_reload.umap` and `stgNN_demo.umap`
  (`SUBLEVELS`). Other sublevels: `_light` (lights, reflection captures, a few cards, read only by
  the stg29 lighting tool), `_lightstatic` (point/spot lights), `_effect` (particles), `_sound`,
  `_mob`, `_debug`, `_edit`. None of those is exported.
- Components read (`COMPONENTS`): StaticMeshComponent, DestructibleComponent, SkeletalMeshComponent,
  SceneComponent, TekkenPlayerStart (GAME_START markers, `_geom` only), and every other
  `*Component` as a transform-only node (capsule, billboard, particle parents). Maps: `_geom` plus
  `SUBLEVELS` `_reload`, `_demo`, `_effect`.
- Node extras: `beam` {width, length, brightness, color, mask} for `SM_GodRay*` components (the
  mesh itself is not exported) and `effect` (ParticleSystemComponent template stem, when
  `bAutoActivate` and not hidden).
- Skipped components: `bHiddenInGame`, `bVisible == False`, `bCastHiddenShadow` (shadow-only
  proxies; stg01 had an 80 m one under its floor), and components whose owning actor has `bHidden`.
- Mesh assets (`MESH_CLASSES`): StaticMesh via `t7extract.mesh.extract_mesh(full=True)`;
  DestructibleMesh and SkeletalMesh via `t7extract.mesh._skeletal(pkg, export, True)` (rest pose,
  disabled sections dropped). Engine primitives (`/Engine/BasicShapes/Cube`, `Cylinder`) are not
  in the paks.
- Node ids: `level * 100000 + export index` (level 0 `_geom`, 1 `_reload`, 2 `_demo`); parents are
  offset the same way.

## Coordinates

- UE cm, left-handed Z-up → Unity m, left-handed Y-up: position `(x, z, -y) * 0.01`, scale
  `(x, z, y)`, rotation `AngleAxis(yaw, up) * AngleAxis(pitch, forward) * AngleAxis(-roll, right)`.
  It is a rotation (det +1), so handedness is kept.
- Winding: STG2 stores Unreal index order. Unity needs the reverse; `LoadMesh` swaps the 2nd and
  3rd index of every triangle. Without it 99.7% of triangles face away from their normals.
- UVs: V flipped (`1 - v`). Normal maps: green flipped at export (DirectX → OpenGL). Tangent w is
  the opposite of UE's TangentZ.w sign (V flip reverses the bitangent).

## STG2 mesh file (`Stages/Source/<sha256(meshpath)[:20]>.bytes`)

```
char[4] "STG2"; int32 vertexCount, sectionCount, uvCount
float3[vc] position (Unity m)      float3[vc] normal      float4[vc] tangent (w = sign)
float2[vc] uv * uvCount (≤2, V flipped)
per section: int32 materialSlot, int32 indexCount, int32[indexCount] indices (Unreal winding)
```

## stages.json

```
stages[]:   name, nodes[], hasStart, start {x,y,z} (Unity m, map space), startYaw (UE degrees)
  nodes[]:  id, parent, name, mesh (key or ""), materials[] (OverrideMaterials keys per slot,
            "" = keep the mesh's), position/rotation/scale (UE values)
meshes[]:   key, path, materials[] (material key per slot)
materials[]: key, path, base, normal, emissive (texture keys = PNG names), blend
            (opaque|masked|translucent|additive|modulate), twoSided, hidden, color[4],
            emissiveColor[4], parameters {param: texture name}, baseSource, note
```

`report.json`: stages, uniqueMeshes, failures[], materials/texturedMaterials (used only),
textures, textureFailures[], untexturedMaterials[{material, stages, reason}].

## Choosing a material's maps (`Materials.describe`, in order)

1. Walk the MaterialInstanceConstant chain to the base Material (`chain`), collecting
   TextureParameterValues, VectorParameterValues, BasePropertyOverrides (blend, two-sided) and,
   for the base Material, its Texture2D imports (fixed samplers).
2. Parameters sorted by `layer_rank`: primary names first (`BaseColorMap`, `BaseColor_A`,
   `Diffuse`, `Mat1`, `BaseA`, `bottom diffuse`), overlays last (`moss`, `_B`, `Mat2`, `top`,
   `detail`, `decal`, `noise`, `flow`, ...). `role()` maps each parameter to base/normal/emissive
   by name, then by the texture's suffix (`suffix_role`).
3. `suffix_role`: strips a SpeedTree `_Tex` / `_Tex_2`; anything containing `normal` is a normal
   map; `spec`, `gloss`, `gradation`, `defaultnormal` are none; `_N/_NRM` normal; `_E/_EM` emissive;
   `_D/_BC/_C/_Diffuse/_Base/_Albedo/_Col` base.
4. No base yet → base-material samplers with a base suffix, excluding `msk/mask/nois/noiz` names
   and packed masks (`packed()`: empty red channel, data in G/B).
5. Still none → an `_a` texture (albedo naming in the ppeel pack), not packed.
6. Still none → the first texture with no role suffix and no mask/spec/normal/rough/... name,
   not packed.
7. Still none but an emissive map → the emissive map is the colour (self-lit sign or sky).
8. Sky domes (`skydome|skysphere|skybox|_sky`) with a base → also emissive (self-lit).
9. Translucent/additive with a base and no real alpha → an `_op` copy whose alpha is brightness ×
   the `*mask*` parameter texture.
10. Colour/emissive vector parameters fill `color` / `emissiveColor`. With no base, `note` says why.

Helper materials (`HELPERS`: `M_Collision`, `M_NoPixelDraw`, `M_lighting`, `BOUNCEMAT*`) get
`hidden: true` and are not described.

## Unity build (`MuseumStageImporter`)

- `ConfigureTextures` fixes import settings of PNGs imported before the postprocessor existed
  (max 2048, mips, compressed; `_nrm.png` NormalMap linear; others sRGB with alpha transparency).
- One HDRP/Lit material per entry (`Stages/Materials/<key>.mat`, reused GUIDs): base map + colour
  (grey 0.55 when shader-constant), normal map, metallic 0, smoothness 0.25, emissive map/colour
  (self-lit: base colour black, emissive ×1.2), surface type transparent for translucent/additive/
  modulate, additive blend mode, alpha clip 0.5 for masked, double-sided. Fog/mist/smoke/cloud/
  unkai/Water_Cliff transparent materials get half alpha. Hidden → `Hidden.mat` (transparent,
  alpha 0); a renderer whose slots are all hidden is not created.
- Skipped meshes: paths containing `/FogSheet/` and `/LightBeam/Meshes/SM_GodRay` (blueprint
  effect planes that get their material at runtime). God rays come back as `AddBeam` quads
  (child `GodRay`, X = length, Y/Z = width, `Remaster/StageLightShaft`: additive, mask × colour ×
  brightness × 0.4, faded within `fadedistance` of the camera and when a quad is seen edge-on).
- `effect` nodes get a `StageEffectMarker` (system name).
- Mesh assets are refilled in place (same GUIDs); prefabs are rebuilt with a `StageInfo`
  (hasMatchStart, matchStart, matchYaw).

## Spawning (`StageEnvironment.Spawn`)

1. Instantiate the prefab, rotate by `AngleAxis(-(matchYaw + 90), up)`, shift so the turned
   start marker's XZ is at the parent's origin.
2. Raycast down from 2 m above the marker over 8 m (no marker: from 20 m over 40 m) against
   temporary MeshColliders on renderers that cover the origin and are not all `Hidden`; the first
   up-facing hit (normal.y > 0.4) becomes the floor. No hit → the marker height.
3. `LastAlignment` records floor, top, yaw and "(no hit)" for captures.

## Captures (`EnvironmentSceneSetup.Capture`)

One `PreviewRenderUtility` per shot, a global volume with `Resources/RemasterEnvironment`
(fixed exposure, as `HdrpSceneLighting` installs at runtime), a `StagingRenderSettings` rig yawed
`VersusYaw`, an `EnvironmentScene` built in edit mode (camera disabled), then
`UnloadUnusedAssetsImmediate` + `GC.Collect`. Views: `SetView(0, 8, 5.2)` with fighters,
`SetView(25, 14, 14)` without. 1280×720.

## Stage effects (`Tools/StageEffects/export_stage_effects.py`)

- Systems = the `effect` names in stages.json (101 on 2026-10-26, 539 placements). The battle
  decoder (`export_hit_effects.py`: `load`, `emitter`, `export_texture`, `export_mesh`, `flatten`)
  runs on pak entries through a `CaseIndex` of `PakFile` wrappers; Effect folders win name clashes.
- Stage additions (`stage_material_info`): emitters with a disabled Required module are dropped;
  an instance with no colour texture inherits its parent's fixed samplers (a noise + `*Mask*`
  pair is baked as `<noise>__x__<mask>`, mask rotated 90° tip-up); `add_color`/`color` vectors
  become `tint` + `intensity`; soft particles (`M_EF__DEPTH_*` parent, DepthFade/PixelDepth
  parameters) get alpha × `SOFT_FADE` 0.3. `subImages` is passed through but the runtime ignores it.
- Runtime: `StageEnvironment.Spawn` → `StartEffects` (Play only): one `CascadeEffects` "Stage
  effects" under the stage, each marker whose system loops (`Loops`: an emitter with EmitterLoops 0) played at its transform × `Euler(0, 90, 0)` (the effect's
  UE X is the marker's +X) with the marker's mean lossy scale. Captures call `StartEffects` and
  `SimulateStep(1/30)` × 90.

## Original lighting and wind

- Exporter: every component's properties are `{**archetype, **instance}`; `archetype()` loads the
  blueprint template (`<Name>_GEN_VARIABLE` under the BlueprintGeneratedClass) so blueprint lights
  (PULSELIGHT*, BP_FlickerPointLight*) keep their real intensity instead of UE's 5000 default.
- Node `light`: type, intensity (UE units; default 10 directional / 5000 others), color (sRGB 0..1),
  radius (m), inner/outer (half-angles), inverseSquared, shadows, channel (`LC_CHARA_BOTH` = fighters
  only, `LC_STAGE` = stage only), mobility, enabled (static directional = baked only → disabled).
- Node `sky`: intensity, color, lower, cubemap (`Stages/Lighting/<name>.t7sky`, 128² RGBAHalf, same
  layout as stg29's sky) when SourceType is SLS_SpecifiedCubemap. Node `fog`: ExponentialHeightFog.
  Node `wind`: WindDirectionalSource strength/speed/gusts. Stage `post`: highest-priority enabled
  unbound PostProcessVolume's overridden keys (drama volumes are disabled in the maps).
- `StageLightingBuilder`: `AddLight` (UE +X → Unity +Z child rotation, light layers 1 stage / 2
  fighters), `TrimBakedLights` (nearest 96 static), `BuildProfile` (Exposure fixed 0, ACES,
  HDRI/Gradient sky, IndirectLightingController, Fog meanFreePath = 1/(density·0.1) m, height
  10/falloff m, Bloom, ColorAdjustments saturation/filter, Vignette, SSAO), `Finish` (StageLighting,
  StageWind over foliage materials, 120k-vertex budget nearest the start).
- `StageLighting.Activate`: disables this scene's StagingRenderSettings lights and volumes, adds both
  light layers to fighters, a priority-50 global volume with a profile copy (fog base height and HDRI
  rotation follow the spawned stage). `StageEnvironment.Spawn` activates; `Despawn` restores.
- HDRP casts cascade shadows from one directional light only ("Cascade Shadow atlasing has failed"
  every frame otherwise, seen 2026-10-26 in Play). `StageLighting.KeepOneShadowSun` keeps the stage's
  brightest shadowed sun and switches shadows off on every other directional light (all loaded scenes
  in Play, the stage's own scene in edit-mode captures), on Activate and every 0.5 s; Deactivate restores.
  User confirmed in Play on 2026-10-26 that the errors are gone.
