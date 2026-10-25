"""Validate baked native secondary tracks and restored skin weights."""
import json,struct,unittest
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[2]

class SecondaryMotionTests(unittest.TestCase):
 def test_tracks(self):
  for ch,move,frames in [('jin',2220,301),('kaz',2326,217)]:
   data=(ROOT/f'Assets/_Project/Resources/SecondaryMotion/{ch}_{move}.bytes').read_bytes()
   magic,n,count,rate=struct.unpack_from('<4sIII',data);self.assertEqual((magic,n,count,rate),(b'SMT1',frames,81,60))
   p=16;names=[]
   for _ in range(count):
    size,=struct.unpack_from('<I',data,p);p+=4;names.append(data[p:p+size].decode());p+=size
   self.assertEqual(len(set(names)),81)
   a=np.frombuffer(data[p:],dtype='<f4').reshape(n,count,10)
   self.assertTrue(np.isfinite(a).all());self.assertLess(abs(a[:,:,:3]).max(),.02)
   self.assertTrue(np.allclose(np.linalg.norm(a[:,:,3:7],axis=2),1,atol=1e-5))
   self.assertGreater(np.ptp(a[:,names.index('R_Index1'),3:7],axis=0).max(),.2)
   self.assertGreater(np.ptp(a[:,names.index('Jaw_Joint'),3:7],axis=0).max(),.001)
   if ch=='jin':self.assertTrue(np.array_equal(a[:,names.index('L_Index1')],np.repeat(a[:1,names.index('L_Index1')],n,axis=0)))
 def test_kazuya_skin_weights(self):
  for piece,part in [('kaz_fac','Lip'),('kaz_bdu_dougi_org','Index')]:
   data=json.loads((ROOT/f'Assets/_Project/Characters/Kazuya/Source/Base/{piece}.json').read_text())
   ids={i for i,b in enumerate(data['bones']) if part.lower() in b['name'].lower()}
   influenced=0
   for w in data['weights']:
    self.assertAlmostEqual(sum(w[f'weight{i}'] for i in range(4)),1,places=5)
    influenced+=any(w[f'boneIndex{i}'] in ids and w[f'weight{i}']>0 for i in range(4))
   self.assertGreater(influenced,50)

if __name__=='__main__':unittest.main()
