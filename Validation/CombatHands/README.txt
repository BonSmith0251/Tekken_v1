FIGHTING HAND POSES

The versus renderer previously curled every finger using the same fixed hinge
angles. It did not use the character's authored fighting hand poses.

FighterActionMotion now loads the original left/right stance tracks before
playback, while the skeleton is still in its bind pose, and applies them on the
same frame as the stance body motion. Each hand has 15 finger joints, including
three separately authored thumb joints. Palm/base bones retain their bind pose.
Native 0x64 rotations are converted through euler_samples before the established
hand coordinate conversion, including mirrored left/right palm bases.
Quaternions are interpolated at runtime. The bake checks every frame for
palm-directed Index/Middle/Ring/Pinky middle-joint bending, preventing the
backward-curl regression. Left-hand mirroring preserves the thumb opposition
axis rather than inverting every rotation axis.

Jin: move 1442, left bank index 253, right index 252, 128 frames.
Kazuya: move 1435, left index 0, right index 1, 88 frames.
Source hashes and references: source.json.

Entrance hand/facial animation still overrides the stance fallback afterward.
Other combat actions currently hold frame zero of the character's native stance
hands; per-attack hand-event playback is not implemented by this fix.

Rebuild: python Tools/SecondaryMotion/bake_combat_hands.py
Close-up playback verification: create Temp/validate-combat-hands with Unity open
and stopped. The editor validator opens LocalVersus2, waits for combat, captures
both hands on both characters, and stops playback.
