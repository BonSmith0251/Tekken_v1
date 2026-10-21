import copy
import tempfile
import struct
import unittest
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation
import combat_pose_converter as converter
from t7extract.motion import Motion

class ConverterTests(unittest.TestCase):
    def profile(self):
        return dict(schema='jin-combat-pose/1',fidelity='experimental-calibration',
            bones=[dict(name=n,channel=c,order='xyz',axes=np.eye(3).tolist(),
                        pre=[0,0,0,1],post=[0,0,0,1]) for n,c in zip(converter.BONES,converter.CHANNELS)],
            root=dict(sourceReference=[0,1000,0],targetReference=[0,0,-1],metersPerUnit=.001))
    def motion(self):
        frame=np.zeros((23,3));frame[1]=[0,1000,0]
        return Motion('0xC8',[11 if c in [0,1,6] else 7 for c in range(23)],[frame.tolist()])
    def test_calibration_recovers_explicit_target_pose(self):
        with tempfile.TemporaryDirectory() as temp:
            source=Path(temp)/'source.anim';target=Path(temp)/'target.bytes'
            m=self.motion();m.samples[0][10]=[.4,-.6,.2];source.write_bytes(m.to_c8())
            expected=np.r_[[.1,.2,-1.03],np.tile(Rotation.from_euler('xyz',[.2,.3,.4]).as_quat(),19)][None,:]
            converter.write_clip(target,expected,{})
            p=converter.calibrate(source,target)
            actual=converter.convert(m,p)
            np.testing.assert_allclose(actual,expected,atol=1e-7)
    def test_noncommuting_pre_post_and_axis_reflection(self):
        p=self.profile();m=self.motion();m.samples[0][10]=[.3,.4,.5]
        b=p['bones'][6];b['axes']=converter.AXES.tolist()
        pre=Rotation.from_euler('x',.4);post=Rotation.from_euler('y',-.2)
        b['pre']=pre.as_quat().tolist();b['post']=post.as_quat().tolist()
        result=converter.convert(m,p)[0,3:].reshape(19,4)[6]
        expected=pre.as_matrix()@converter.AXES@Rotation.from_euler('xyz',[.3,.4,.5]).as_matrix()@converter.AXES.T@post.as_matrix()
        np.testing.assert_allclose(Rotation.from_quat(result).as_matrix(),expected,atol=1e-7)
    def test_quaternion_continuity_across_pi(self):
        m=self.motion();m.samples*=2;m.samples=copy.deepcopy(m.samples)
        m.samples[0][3]=[0,0,3.13];m.samples[1][3]=[0,0,-3.13]
        q=converter.convert(m,self.profile())[:,3:7]
        self.assertGreater(float(q[0]@q[1]),.999)
    def test_root_scale_and_in_place(self):
        m=self.motion();m.samples[0][1]=[100,1100,200]
        np.testing.assert_allclose(converter.convert(m,self.profile(),'source')[0,:3],[-.1,.2,-1.1])
        np.testing.assert_allclose(converter.convert(m,self.profile(),'in-place')[0,:3],[0,0,-1.1])
    def test_reject_bad_profile_and_unsupported_skeleton(self):
        p=self.profile();p['bones'][0]['axes'][0][0]=2
        with self.assertRaises(ValueError):converter.convert(self.motion(),p)
        p=self.profile();p['bones'][1]['channel']=3
        with self.assertRaises(ValueError):converter.validate_profile(p)
        with self.assertRaises(ValueError):converter.convert(Motion('0xC8',[7],[[[0,0,0]]]),self.profile())
    def test_endian_and_output_protection(self):
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp)/'clip.anim';m=self.motion();path.write_bytes(m.to_c8())
            decoded,_=converter.read_motion(path);np.testing.assert_allclose(decoded.samples,m.samples)
            path.write_bytes(struct.pack('>HHI23I69f',200,23,1,*m.descriptors,*np.asarray(m.samples).reshape(-1)))
            decoded,_=converter.read_motion(path);np.testing.assert_allclose(decoded.samples,m.samples)
            target=Path(temp)/'clip.bytes';v=converter.convert(m,self.profile())
            converter.write_clip(target,v,{})
            with self.assertRaises(FileExistsError):converter.write_clip(target,v,{})
            target.write_bytes(b'JRT1')
            with self.assertRaises(ValueError):converter.read_jrt(target)

if __name__=='__main__':unittest.main()
