"""Regression check for repeated source motion at Kazuya's two entrance cuts."""
import json
import re
import struct
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

class LandingStitchTests(unittest.TestCase):
    def test_descent_never_rewinds_at_either_join(self):
        code = (ROOT / 'Assets/Remaster/Scripts/LocalVersusEntrance.cs').read_text()
        values = {name: int(re.search(r'\b' + name + r' = (\d+)', code)[1])
                  for name in ('FirstCut', 'FirstOverlap', 'SecondCut', 'SecondOverlap')}
        def source(frame):
            return (frame + (values['FirstOverlap'] if frame >= values['FirstCut'] else 0)
                    + (values['SecondOverlap'] if frame >= values['SecondCut'] else 0))
        data = (ROOT / 'Assets/Remaster/Museum/KazuyaFull/character_0714.bytes').read_bytes()
        frames, bones, rate = struct.unpack_from('<3i', data, 4)
        roots = [struct.unpack_from('<3f', data, 16 + f * (3 + bones * 4) * 4)
                 for f in range(frames)]
        duration = frames - values['FirstOverlap'] - values['SecondOverlap']
        self.assertEqual(source(duration - 1), frames - 1)
        joins = []
        for frame in (values['FirstCut'], values['SecondCut']):
            a, b = source(frame - 1), source(frame)
            before, after = -roots[a][2], -roots[b][2]
            self.assertGreater(before, after)
            self.assertLess(before - after, .13)
            joins.append(dict(outputFrame=frame, sourceBefore=a, sourceAfter=b,
                              heightBefore=before, heightAfter=after))
        heights = []
        for quarter in range(79 * 4):
            frame = quarter / 4
            a, t = int(frame), frame % 1
            heights.append(-(roots[source(a)][2] * (1-t) + roots[source(a+1)][2] * t))
        self.assertTrue(all(b <= a + .00001 for a, b in zip(heights, heights[1:])))
        report = dict(sourceFrames=frames, presentationFrames=duration, joins=joins,
                      monotonicQuarterFrameDescent=True,
                      livePlaybackVerified=False)
        (ROOT / 'Validation/EntranceFraming/landing-stitch-check.json').write_text(
            json.dumps(report, indent=2) + '\n')

if __name__ == '__main__':
    unittest.main()
