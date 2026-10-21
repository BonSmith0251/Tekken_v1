Original Jin motion sources

Open Remaster > Original Jin motions in Unity. Select a clip and scrub frames in the Inspector.

These .t7motion files contain decoded, little-endian 0xC8 samples. They are NOT Unity .anim clips. The 0x64 compression has been decoded; the skeletal retargeting has not been verified. The playable build retains procedural motion.

The library contains every clip in the Jin character and embedded banks, plus shared clips referenced by his moveset. Unreferenced character-bank clips can include cinematic motions. Move references include nonattack states, aliases and shared reactions; the move count is not a count of distinct attacks.

Original frame samples are preserved, with no invented keys. File header: uint16 0xC8, uint16 channels, uint32 frames, uint32[channels] descriptors; then frame-major float32[frames][channels][3]. Body samples use native coordinates and units.

Decoder format reference: https://github.com/Kiloutre/TKMovesets/blob/master/TKMovesets2/Utils/Animations_T_x64.cpp
Bone mapping research: https://github.com/Kiloutre/BlenderTekkenAnimationImporter

The decoder uses N-1 residual samples after the base pose; the public reference has a frame-index ambiguity at block boundaries. Synthetic acceleration/block tests and installed Jin loop continuity checks validate the chosen indexing.
