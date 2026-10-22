# Xiaoyu source inventory

The installed character code is `xia`; her staging animation prefix is `fu_`.
`xia/moveset.json` was extracted from the installed `xiamot.bin` with
`python -m t7extract moves decrypted/tkdata xia`. The source archive is
`decrypted/tkdata/mothead/mask/xiarotsm.arc`.

The [source validation](staging-source-validation.json) records all nine
`fu_s`/`fu_w` actions, their body references, and every camera, face, and
hand event. Five records are entrances and four are victories. Eight have
distinct camera events. `fu_w01_y` (move 2435) has its own hands but lacks
standalone camera and face events. All 34 referenced tracks decode, contain
finite samples, and match their body frame counts. Event property frame
`32769` still needs timeline interpretation before playback.

Original first-costume mesh packages in the installed game:

- `Character/Item/Meshes/xia/Face/xia_fac/Meshes/SK_CH_xia_fac.uasset`
- `Character/Item/Meshes/xia/Lower/xia_bdl_1p/Meshes/SK_CH_xia_bdl_1p.uasset`
- `Character/Item/Meshes/xia/Upper/xia_bdu_1p/Meshes/SK_CH_xia_bdu_1p.uasset`
- `Character/Item/Meshes/xia/Hair/xia_har_1p/Meshes/SK_CH_xia_har_1p.uasset`

The Unity project currently has no Xiaoyu rig, costume prefab, body motion
catalog, or scene actor. No Xiaoyu action is connected to runtime playback
yet. The source validation does not establish skinned pose, camera framing,
or visual fidelity.
