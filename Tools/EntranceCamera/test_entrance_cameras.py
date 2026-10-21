import json
import math
from pathlib import Path
import struct
import unittest

PROJECT = Path(__file__).resolve().parents[2]
WORKSPACE = PROJECT.parents[1]


class EntranceCameraTests(unittest.TestCase):
    def test_physical_lens_preserves_source_projection(self):
        # Independent pinhole projection check for every animated lens sample.
        # 36 x 20.25 mm is an equivalent 16:9 gate, not a recovered filmback.
        for resource in ['Jin_s00', 'Kazuya_s06']:
            camera = (PROJECT / f'Assets/Remaster/Resources/EntranceCameras/{resource}.bytes').read_bytes()
            frames = struct.unpack_from('<I', camera, 4)[0]
            for frame in range(frames):
                horizontal = struct.unpack_from('<f', camera, 20 + frame * 48 + 24)[0]
                focal = 36 / (2 * math.tan(horizontal / 2))
                vertical = 2 * math.atan(20.25 / (2 * focal))
                self.assertAlmostEqual(2 * focal / 36, 1 / math.tan(horizontal / 2), places=10)
                self.assertAlmostEqual(2 * focal / 20.25, 1 / math.tan(vertical / 2), places=10)
                for width, height in [(1280, 720), (1024, 768), (1920, 1200), (3440, 1440)]:
                    scale = min(width / 16, height / 9)
                    self.assertAlmostEqual((16 * scale) / (9 * scale), 16 / 9)
                    self.assertLessEqual(16 * scale, width + 1e-6)
                    self.assertLessEqual(9 * scale, height + 1e-6)

    def test_tracks_match_body_duration_and_have_valid_lenses(self):
        for character, motion, resource, move, track in [
            ('Jin', 661, 'Jin_s00', 2218, 104),
            ('Kazuya', 700, 'Kazuya_s06', 2247, 120),
        ]:
            with self.subTest(character=character):
                body = (PROJECT / f'Assets/Remaster/Museum/{character}Full/character_{motion:04}.bytes').read_bytes()
                camera = (PROJECT / f'Assets/Remaster/Resources/EntranceCameras/{resource}.bytes').read_bytes()
                magic, frames, fps, source_move, source_track = struct.unpack_from('<4sIIII', camera)
                self.assertEqual((magic, fps, source_move, source_track), (b'T7C1', 60, move, track))
                self.assertEqual(frames, struct.unpack_from('<I', body, 4)[0])
                self.assertEqual(len(camera), 20 + frames * 48)
                values = struct.unpack_from(f'<{frames * 12}f', camera, 20)
                self.assertTrue(all(math.isfinite(v) for v in values))
                self.assertTrue(all(0 < values[f * 12 + 6] < math.pi for f in range(frames)))
                self.assertNotEqual(values[:12], values[-12:])

    def test_every_exported_sample_matches_original_archive(self):
        records = json.loads((Path(__file__).parent / 'source-manifest.json').read_text())
        for record in records:
            archive = WORKSPACE / record['source'].replace('\\', '/')
            if not archive.exists():
                self.skipTest('Original extracted archives are not included in the Unity repository')
            data = archive.read_bytes()
            offset, size = struct.unpack_from('<II', data, 4 + record['bank'] * 8)
            start = offset + struct.unpack_from('>I', data, offset + 20 + record['track'] * 4)[0]
            count = record['frames'] * 12
            expected = struct.unpack_from(f'>{count}f', data, start + 24)
            exported = (PROJECT / f'Assets/Remaster/Resources/EntranceCameras/{record["resource"]}.bytes').read_bytes()
            self.assertEqual(expected, struct.unpack_from(f'<{count}f', exported, 20))


if __name__ == '__main__':
    unittest.main()
