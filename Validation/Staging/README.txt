STAGING PLAYBACK

Startup: randomly select one native entrance for each character. The previous
choice is stored separately per character and action type, avoiding immediate
repeats across Unity Play sessions/application launches. Five Jin and seven
Kazuya distinct entrances are available. The duplicate kz_s06 body alias is
represented once so it does not receive twice the selection probability.

Wins: at match victory select one of four Jin or three Kazuya victory actions.
The winner's body, hand and available face/camera tracks play together.
Automatic rematches wait for the action to finish, then hold three seconds.
Manual rematch clears all secondary transforms and presentation overrides.
The two paired _y victory variants have no independent native camera/face
properties; they use an explicit full-body camera and retain neutral face.

Assets: Resources/StagingCatalog.json links each original move ID to its body
catalog entry, secondary track and camera track. Tools/SecondaryMotion/
bake_staging_tracks.py regenerates the catalog and 19 secondary/17 camera
resources from the installed original archives. Kazuya s03 alone uses the
repaired landing overlap map. Entrances retain their own frame lengths.

Camera continuity:
- Native within-action edit discontinuities blend over 6 frames before / 9 after.
- Composed viewing angle, distance and FOV ease over a 9-frame time constant.
- Subject translation follows immediately; delaying it caused fast actions to
  leave the view. Final bounds fitting keeps the subject visible.
- Angular change is limited to 2.2 degrees/frame before floor safety correction;
  vertical FOV change is limited to .8 degrees/frame.
- Kazuya landing's close/wide focus switch now eases over 20 source frames.
- The deliberate black cut between players and the round-start cut remain.

Validation: playback.json scans every frame of all 19 distinct actions for
finite camera values and records head visibility and maximum per-frame camera
changes. PNGs show the final rendered frame of each action. flow.json verifies
random startup entrances, both winner sequences completing, and rematch cleanup.
Those checks do not claim pixel-identical recreation of the original game.

ACTION-SPECIFIC CAMERA CORRECTION
The renderer now uses each track's actual camera position relative to its
animated subject, including lateral movement and height. Previously it reduced
that position to a distance and substituted the same 12-degree frontal angle
for every action. Authored roll is now retained up to 12 degrees. The existing
camera-edit/FOV smoothing remains enabled.

To preserve the requested front view, only the rearward depth component is
reflected into the front hemisphere; lateral travel remains signed and the
profile angle is limited to 75 degrees. Bounds fitting and floor clearance
remain safety adjustments. This is a front-facing adaptation of each native
track, not an unmodified original-game camera transform.

camera-sources.json records the 17 distinct native camera sample hashes and
move bindings. The two paired victory variants still have no standalone source
camera and continue to use the documented full-body fallback.

Shot scale is also adapted per sample from the native distance and FOV. The
shared full-body safety bounds no longer turn every close-up into a wide shot;
head bounds remain included so close-up framing does not lose the subject.
