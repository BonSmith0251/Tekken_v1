"""Reconvert the installed eight-clip pose lab using its editable calibration."""
import json
from pathlib import Path
from combat_pose_converter import BONES,convert,read_motion,write_clip,digest,validate_profile

tools=Path(__file__).resolve().parent
project=tools.parent.parent
out=project/'Assets/_Project/Characters/Jin/Motion/CombatPoseLab'
profile_bytes=(out/'calibration-v3.json').read_bytes();profile=json.loads(profile_bytes)
validate_profile(profile)
manifest=json.loads((tools/'sources/index.json').read_text())
entries=[]
for entry in manifest['clips']:
    source=(tools/'sources'/entry['file']).resolve()
    if not source.is_relative_to((tools/'sources').resolve()):raise ValueError('Source escapes source folder')
    key=entry['key']
    if not key.replace('_','').isalnum():raise ValueError('Invalid clip key')
    motion,raw=read_motion(source)
    output=out/(key+'.bytes')
    report=write_clip(output,convert(motion,profile),dict(source=entry['file'],
        sourceSha256=digest(raw),moveId=entry['moveId'],moveName=entry['name'],
        profileSha256=digest(profile_bytes),rootMotion='in-place'),force=True)
    entries.append(dict(key=key,name=entry['name'],path=output.relative_to(project).as_posix(),
        frames=len(motion.samples),maxFrameRotationDegrees=report['maxFrameRotationDegrees']))
    print(key,report['frames'],'frames; largest step',round(report['maxFrameRotationDegrees'],1),'degrees')
(out/'index.json').write_text(json.dumps(dict(bones=BONES,clips=entries,mappingVersion=3,
    fidelity='experimental-calibration',originalGameVerified=False,rotationEncoding='spherical-axis-angle'),indent=2))
print('Updated experimental preview data. These clips are not wired into gameplay.')
