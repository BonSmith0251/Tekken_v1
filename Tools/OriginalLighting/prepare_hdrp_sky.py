"""Convert the extracted UE Z-up HDR cubemap to Unity cube-face storage."""
import gzip, hashlib, json, struct
from pathlib import Path
import numpy as np

PROJECT = Path(__file__).resolve().parents[2]
WORKSPACE = PROJECT.parents[1]
source = WORKSPACE / 'decrypted/lighting/T7_T_ENV_s80skysphere_capturedCube.dds'
raw = source.read_bytes()
expected = '840188827ee3e36c3d0bd4ceb64602abcfebe45dcdfa379bb4e6edb9a29f8ce2'
assert hashlib.sha256(raw).hexdigest() == expected
n = 1024
src = np.frombuffer(raw, dtype='<f2', offset=148).reshape(6,n,n,4)
assert np.isfinite(src[:,:,:,:3]).all()
u,v = np.meshgrid(2*(np.arange(n)+.5)/n-1, 1-2*(np.arange(n)+.5)/n)
one = np.ones_like(u)
directions = [(one,-v,-u),(-one,-v,u),(u,one,v),(u,-one,-v),(u,-v,one),(-u,-v,-one)]
faces = []
for ux,uy,uz in directions:
    x,y,z = ux,-uz,uy  # inverse of UE(x,y,z) -> Unity(x,z,-y)
    xyz = np.stack([x,y,z])
    major = np.argmax(np.abs(xyz),axis=0)
    axis = np.take_along_axis(xyz,major[None],axis=0)[0]
    face = major*2+(axis<0)
    den = np.abs(axis)
    sc = np.choose(face,[-z,z,x,x,x,-x])/den
    tc = np.choose(face,[-y,-y,z,-z,-y,-y])/den
    ix = np.clip(((sc+1)*.5*n).astype(int),0,n-1)
    iy = np.clip(((tc+1)*.5*n).astype(int),0,n-1)
    pixels = src[face,iy,ix].copy()
    pixels[:,:,3] = 1  # original auxiliary alpha is not sky opacity
    # Unity cube uploads use the opposite row order from the direction grid.
    faces.append(pixels[::-1].tobytes())
output = PROJECT/'Assets/Remaster/Rendering/OriginalStage/Stg29Sky.t7sky'
output.parent.mkdir(parents=True,exist_ok=True)
output.write_bytes(gzip.compress(b'T7SK'+struct.pack('<I',n)+b''.join(faces),mtime=0))
report = dict(sourceDdsSha256=expected,resolution=n,faces=6,format='RGBAHalf',sourceToUnityBasis='(x,z,-y)',resampling='exact texel reorientation with Unity cube row order; no HDR clipping',auxiliaryAlpha='set to 1',compressedBytes=output.stat().st_size)
(PROJECT/'Validation/OriginalLighting/sky-conversion.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report))
