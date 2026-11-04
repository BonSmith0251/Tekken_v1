# Original camera evidence

Read original `TekkenGame/Content/Binary/tkdata.bin`, entries `mothead/bin/<code>mot.bin` and `mothead/mask/<code>rotsm.arc`. The project tool uses `t7extract.moveset` and `TkDataFile`; it checks Unity command-graph source hashes only as a consumer consistency check. Original move records, not the graph, provide the events. Tools/CharacterEffects/dispatch_native.py supplies bounded PE disassembly; Python capstone is required.

Current extraction (2026-10-27): 1,494 selected event records across five inspected fighters; 12 supported look-at tracks across four fighters (Jin 2, Kazuya 6, Xiaoyu 2, Katarina 2). Alisa's four/six-channel position/Euler layouts are not supported by this look-at demo. These counts include shared/item/mode-specific moves and are not counts of reachable versus actions. Other camera layouts remain metadata with `supported=false`. Original export and investigation are in workspace `Artifacts/CameraEffects/OriginalExport` and adjacent native disassembly JSONs.

Bank 4 in the ARC directory points to a MOTA container. The observed tracks are big-endian C8: type u16, channel count u16, frame count u32, then channel descriptors and frames × channels × 3 float samples. Bound each track using the next greater offset, not merely the archive's end. Require finite samples and supported descriptors. The look-at family is 11/11/15/3/21. T7CF resources retain all floats in little endian with magic, frame count, channel count and descriptors. Source paths and hashes belong in the manifest even for excluded layouts.

Properties inventoried as camera candidates: 0x8435–0x843A, 0x8445, 0x8448 and 0x8449, with bank flag `(value & 0xC000) == 0x4000`, track `value & 0x3FFF`. Existing Unity staging handles four-channel 11/7/15/21 tracks separately; do not feed them into FightCameraTrack. The camera-property family and side interpretation are inherited format knowledge requiring further native/visual corroboration; the exporter proves stored references and track bytes, not all call semantics.

Native shake evidence for executable SHA-256 `f46f9ff526fe5d00a0d1ee52126f61628cdc13c402264e2764e4010d3b9c1892`:

- Interpreter VA 0x140717740; nonvisual switch table VA 0x14071FC98, indexed by property minus 0x8001.
- 0x8001/2/3/4 and 0x8006/7/8/9 pass IDs 3/4/5/6 to 0x1406FF050.
- That function resets/stores preset state in three camera objects, including helpers 0x1406E7AF0 and 0x1406E7AD0. This does not recover the later waveform evaluation.
- The separate visual switch at 0x14071FA10 is not the shake table. Blindly indexing it outside its reachable range can produce misleading disassembly. 0x8439/0x843A returning in one switch does not prove the game has no camera handling elsewhere.

The tool pins the executable hash before using addresses and checks each actual preset call. On a changed executable, rediscover dispatch before updating the pin. Do not infer SHA authenticity beyond local identity.

`Tools/EntranceCamera/gameplay-camera-capture.json` contains an earlier original-process observation of 65-degree horizontal gameplay FOV and 16:9 aspect. Its context says the user confirmed a normal fight, but frames were not independently phase-labeled. It is not proof of impact zoom, shake or KO timing.

Recovered since: the shake presets (export_shake_presets.py) and the 0x84CC follow-rate requests (export_follow_rates.py; not a zoom: base rate 0.1/frame scaled x3/x2/x1.5/x0.5/x0.25 by requests 11/12/13/14/15). Preserve unanswered questions: exact remaining camera trigger call paths; source-time flags; actor versus opponent anchors; native blending and roll; interruption and fallback. (The KO camera is answered: see the KO section below.) Each needs original code or controlled original execution, not a fit to the Unity result.

Versus check (2026-10-27, Claude harness `RageCamera`): Jin, Kazuya, Xiaoyu and Katarina Rage Arts start their look-at track in LocalVersus2 on the side matching the actor's facing. Alisa's `Aa_RageArts01` raises 0x8439=0 and 0x843A=1 with no 0x4000 bank flag; bank 0 of `asarotsm.arc` and the moveset's embedded MOTA banks are body animation (type 0x64), `mask/common.arc` is another container format, and 0x842E/0x842F (0x41xx) select body animations. Her Rage Art camera source is unresolved.

Shake recovered (2026-10-27): camB update 0x1406EDCE0 (vtable 0x1428BBFA0 slot 1) calls evaluator 0x140701FD0(state, preset, frame) per 60 Hz tick; ten per-frame tables built on its stack from .rdata (jump table 0x1407024C0) add values[f]*2π/65536 rad to the eye→target pitch only. `Tools/CameraEffects/export_shake_presets.py` reproduces them into catalog.json (shakePresets). Interpreter case 0x14071999B: properties 0x8005/0x800A use min(value,9) as the preset. Native preset requests at 0x1406DEF4B (6), 0x140711298 (4), 0x140714B78 and 0x1407911C2 (3) are unidentified events.

## KO (2026-10-28)

Fight state 0x1406DE720 on a KO: slow table entry 2 = {speed 1%, 90 frames} (0x140740A30; 150 when game +0x260,
set when the winner's fighter +0xC02 / Rage Drive property 0x801B is active); fighters advance x speed/100
(0x140758790); shake preset 6 (0x1406DEF4B); camera mode 7 (0x140700B50, update 0x1406EF580): copy of the current
view, FOV 35 (horizontal degrees, matching a live capture's 35 samples), weight frame/5, 1 to frame 95, out by 115,
one step per camera frame (real 60 Hz). Mode 8 (0x1407008A0 -> 0x140700580, update 0x1406EF2C0 -> 0x1406F3BF0) is the Rage Drive KO shot: variant 0/1 at random; centre = P1 or match winner (game +0x268), variant 1 its opponent; s = sin(80deg t)/sin(80deg); FOV 20->35; angle -30->-60 / -60->-45 times side sign (C +0x14D4); position = C + R_y(angle)(dir to opponent)(|d| + 4000 mm), 100 mm up; target = live view 1 target; roll sign*15deg; 150 frames, full weight, mm y-up.
Settings: static initializer 0x140701A3D, 0x1434DF5FC..0x1434DF614. Reference video: freeze 146.0-147.47 s in
TEKKEN 7 2026-10-23 08-04-08.mp4.

## Trade slow-down (2026-10-28)

Hit resolution 0x140713DD0: both fighters struck on the same frame (+0x7CC > 0 with +0x754 & 0x20, or both +0x754 &
0x220; cancelled by a throw, +0x314 < 0) and either damage >= 17 (0x1434DF5D8): slow entry 2 = {10%, 24 frames},
shake preset 5 (2 + 0x1434DF5DC), camera mode 7 at FOV 35 with 4 / 20 / 30 frames (0x1434DF5E0..EC). Catalog
`tradeCamera` (export_ko_camera.py). A KO in the same frame overrides it.
