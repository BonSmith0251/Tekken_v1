"""Read-only staging/reference inventory for an extracted Tekken character.

Uses only the Python standard library. Reports source metadata; does not decode
motion, infer bone layouts, modify Unity or export game assets.
"""
import argparse
import hashlib
import json
import re
import struct
import sys
from pathlib import Path

EVENTS = {0x842E: ('left_hand', 1), 0x842F: ('right_hand', 1),
          0x84C2: ('face', 2), 0x8435: ('camera', 4)}
STAGING = re.compile(r'(?:^|_)([sw])\d{2}(?:_|\(|$)', re.I)


def integer(value):
    return int(value, 0) if isinstance(value, str) else int(value)


def track_header(raw, reference, bank):
    if reference & 0xC000 != 0x4000:
        return {'status': 'unresolved_reference_kind', 'reference': reference}
    if raw is None:
        return {'status': 'archive_not_supplied'}

    def read(fmt, pos, end=None):
        end = len(raw) if end is None else end
        size = struct.calcsize(fmt)
        if pos < 0 or pos + size > end:
            raise ValueError('Truncated archive or track header')
        return struct.unpack_from(fmt, raw, pos)

    offset, size = read('<II', 4 + bank * 8)
    end = offset + size
    if size < 20 or end > len(raw) or raw[offset:offset+4] != b'MOTA':
        raise ValueError('Unsupported or invalid MOTA bank table')
    count, = read('>I', offset + 12, end)
    table_end = offset + 20 + count * 4
    if table_end > end:
        raise ValueError('Truncated MOTA index table')
    index = reference & 0x3FFF
    if index >= count:
        raise ValueError(f'Track index {index} exceeds bank count {count}')
    relative, = read('>I', offset + 20 + index * 4, end)
    start = offset + relative
    if start < table_end or start >= end:
        raise ValueError('Track offset outside bank payload')
    fmt, channels = read('>HH', start, end)
    if not 0 < channels < 256:
        raise ValueError('Invalid channel count')
    if fmt == 0xC8:
        frames, = read('>I', start + 4, end)
        if start + 8 + channels * 4 + frames * channels * 12 > end:
            raise ValueError('Truncated C8 payload')
    elif fmt == 0x64:
        frames, = read('>H', start + 4 + channels * 2, end)
    else:
        return {'status': 'unsupported_motion_format', 'format': hex(fmt), 'index': index}
    if not 0 < frames <= 65535:
        raise ValueError('Invalid frame count')
    return {'status': 'header_checked', 'bank': bank, 'index': index,
            'format': hex(fmt), 'channels': channels, 'frames': frames,
            'fullPayloadDecoded': False}


def inspect(moveset, archive=None):
    source = Path(moveset).read_bytes()
    document = json.loads(source)
    moves = document['moves'] if isinstance(document, dict) else document
    if not isinstance(moves, list):
        raise ValueError('Expected moves array')
    raw = Path(archive).read_bytes() if archive else None
    candidates, issues = [], []
    for move in moves:
        if not isinstance(move, dict):
            raise ValueError('Move record must be an object')
        animation = move.get('animation') or ''
        match = STAGING.search(animation)
        properties = move.get('properties') or []
        events = []
        for prop in properties:
            try:
                pid = integer(prop['id'])
                if pid not in EVENTS:
                    continue
                reference = integer(prop['value'])
                kind, bank = EVENTS[pid]
                entry = {'kind': kind, 'propertyId': hex(pid), 'propertyFrame': prop.get('frame'),
                         'reference': reference}
                try:
                    entry.update(track_header(raw, reference, bank))
                except (ValueError, struct.error) as error:
                    entry.update(status='invalid_header', error=str(error))
                events.append(entry)
            except (KeyError, ValueError, TypeError) as error:
                issues.append({'moveId': move.get('id'), 'error': f'Invalid property: {error}'})
        # Camera events discover candidates that do not follow the naming pattern.
        if not match and not any(e['kind'] == 'camera' for e in events):
            continue
        body = move.get('anim') or {}
        body_frames = body.get('frames')
        for event in events:
            if event.get('frames') is not None and body_frames is not None:
                event['matchesBodyFrameCount'] = event['frames'] == body_frames
            if event['status'] != 'header_checked':
                issues.append({'moveId': move.get('id'), 'kind': event['kind'],
                               'status': event['status']})
        candidates.append({'moveId': move.get('id'), 'name': move.get('name'),
                           'animation': animation, 'body': body,
                           'kindHint': ('entrance' if match.group(1).lower() == 's' else 'victory') if match else 'unclassified',
                           'classificationNeedsVerification': True, 'events': events})
    groups = {}
    for entry in candidates:
        signature = json.dumps([entry['animation'], entry['body'], entry['events']], sort_keys=True)
        groups.setdefault(signature, []).append(entry['moveId'])
    return {'moveset': str(Path(moveset).resolve()),
            'movesetSha256': hashlib.sha256(source).hexdigest(),
            'archive': str(Path(archive).resolve()) if archive else None,
            'archiveSha256': hashlib.sha256(raw).hexdigest() if raw is not None else None,
            'moveRecords': len(moves), 'stagingCandidates': len(candidates),
            'candidateAliases': [ids for ids in groups.values() if len(ids) > 1],
            'candidates': candidates, 'issues': issues,
            'limitations': ['Name and event heuristics need semantic verification.',
                            'Header checks do not validate decoding, rig mapping or rendered behavior.',
                            'Property start frames and multiple events require timeline resolution.',
                            'Bank offsets/reference encoding describe this installed archive family.']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--moveset', type=Path, required=True)
    parser.add_argument('--archive', type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.moveset, args.archive), indent=2))
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(2, f'Character inventory failed: {error}\n')


if __name__ == '__main__':
    main()
