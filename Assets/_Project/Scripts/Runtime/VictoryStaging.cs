using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Versus.Remaster
{
    public sealed class VictoryStaging
    {
        readonly Transform actor, head;
        readonly Transform[] landmarks;
        readonly Vector3 faceAxis;
        readonly FighterActionMotion motion;
        readonly MuseumCharacter character;
        readonly string key;
        readonly Dictionary<int,SecondaryMotionTrack> secondary=new Dictionary<int,SecondaryMotionTrack>();
        readonly Dictionary<int,EntranceCameraTrack> cameras=new Dictionary<int,EntranceCameraTrack>();
        StagingAction selected;
        float frame;
        public bool Active => selected!=null;
        public bool Complete => Active && frame>=selected.frames-1;
        public VictoryStaging(Transform actor,FighterActionMotion motion,MuseumCharacter character,string key)
        {
            this.actor=actor;this.motion=motion;this.character=character;this.key=key;
            head=actor.GetComponentsInChildren<Transform>().First(t=>string.Equals(t.name,"Head",StringComparison.OrdinalIgnoreCase));
            faceAxis=head.InverseTransformDirection(actor.forward);
            landmarks=actor.GetComponentsInChildren<Transform>().Where(t=>new[]{"L_Foot","R_Foot","L_Hand","R_Hand"}.Contains(t.name,StringComparer.OrdinalIgnoreCase)).ToArray();
            // Cache against the untouched bind pose, never an animated hand/face.
            foreach(var entry in StagingCatalog.Load().entries.Where(e=>e.character==key && e.kind=="victory"))
            {
                secondary.Add(entry.moveId,new SecondaryMotionTrack(actor,entry.secondary,entry.frames));
                if(!string.IsNullOrEmpty(entry.camera))cameras.Add(entry.moveId,new EntranceCameraTrack(entry.camera,entry.moveId,entry.frames));
            }
        }
        public void Begin(Func<StagingAction,bool> allowed=null,int preferred=-1){selected=StagingCatalog.Select(key,character,"victory",allowed,preferred);frame=0;}
        public void Advance(float delta){if(Active)frame=Mathf.Min(frame+Mathf.Clamp(delta,0,1f/15f)*60,selected.frames-1);}
        public void Prepare()
        {
            int index=Array.FindIndex(character.actions,a=>a.moveId==selected.moveId);
            motion.SetPresentation(index,frame,false,true);
        }
        public void Present(Camera camera,EntranceCameraLens lens)
        {
            secondary[selected.moveId].Apply(frame);
            var bounds=new Bounds(head.position+Vector3.up*.1f,Vector3.one*.44f);
            foreach(var bone in landmarks)bounds.Encapsulate(new Bounds(bone.position,Vector3.one*.2f));
            if(cameras.TryGetValue(selected.moveId,out var track))
                track.Apply(camera,actor,frame,lens,bounds,head.TransformDirection(faceAxis), null, head.position);
            else
            {
                // Paired victory variants have no standalone source camera.
                // Use an explicit full-body frontal view rather than another take's track.
                lens.SetHorizontalFov(65);
                var front=Vector3.ProjectOnPlane(head.TransformDirection(faceAxis),Vector3.up).normalized;
                if(front.sqrMagnitude<.01f)front=actor.forward;
                float distance=Mathf.Max(3.5f,bounds.extents.magnitude*3);
                camera.transform.position=bounds.center+front*distance+Vector3.up*.2f;
                camera.transform.LookAt(bounds.center);
            }
        }
        public void Clear()
        {
            if(!Active)return;
            secondary[selected.moveId].Restore();motion.SetPresentation(-1,0,false);selected=null;
        }
    }
}
