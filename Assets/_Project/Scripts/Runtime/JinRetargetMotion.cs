using System;
using System.Collections.Generic;
using UnityEngine;

namespace Versus.Remaster
{
    // v3 clips already face model +Z. Battle facing belongs to the fighter root.
    public sealed class JinRetargetMotion : MonoBehaviour
    {
        static readonly string[] Names={"BASE","Spine1","Hip","Neck","Head","R_Shoulder","R_Arm","R_ForeArm","R_Hand","L_Shoulder","L_Arm","L_ForeArm","L_Hand","R_UpLeg","R_Leg","R_Foot","L_UpLeg","L_Leg","L_Foot"};
        static readonly string[] Keys={"idle","walk_forward","walk_back","crouch","left_punch","right_punch","left_kick","right_kick"};
        static readonly int[] Strike={10,10,14,13};
        sealed class Clip { public Vector3[] p; public Quaternion[,] q; }
        readonly Dictionary<string,Clip> clips=new Dictionary<string,Clip>();
        Transform[] bones;
        Quaternion[] previous,transition;
        Vector3 previousPosition,transitionPosition;
        float lastClock=-1,stateStart,transitionStart,lastX;
        int lastAttackFrame=-1;
        bool hasPose;
        public string CurrentClip {get;private set;}="";
        public float SampleFrame {get;private set;}
        public float Weight {get;private set;}
        public bool Ready=>clips.Count==Keys.Length;
        public void Initialize(TextAsset idle)
        {
            var map=new Dictionary<string,Transform>();
            foreach(var bone in GetComponentsInChildren<Transform>())map[bone.name]=bone;
            bones=new Transform[Names.Length];previous=new Quaternion[Names.Length];transition=new Quaternion[Names.Length];
            for(int i=0;i<Names.Length;i++)bones[i]=map[Names[i]];
            clips.Clear();
            foreach(string key in Keys)
            {
                var asset=key=="idle" && idle?idle:Resources.Load<TextAsset>("JinRetargeted/Combat/"+key);
                if(!asset)throw new InvalidOperationException("Missing Jin combat clip: "+key);
                byte[] data=asset.bytes;
                if(data.Length<16 || System.Text.Encoding.ASCII.GetString(data,0,4)!="JRT1")throw new Exception("Invalid Jin clip: "+key);
                int frames=BitConverter.ToInt32(data,4),count=BitConverter.ToInt32(data,8);
                if(frames<2 || count!=Names.Length || BitConverter.ToInt32(data,12)!=60 || data.Length!=16+frames*316)throw new Exception("Invalid Jin clip size: "+key);
                var clip=new Clip {p=new Vector3[frames],q=new Quaternion[frames,count]};
                for(int f=0;f<frames;f++)
                {
                    int offset=16+f*316;
                    clip.p[f]=new Vector3(BitConverter.ToSingle(data,offset),BitConverter.ToSingle(data,offset+4),BitConverter.ToSingle(data,offset+8));
                    for(int c=0;c<count;c++)
                    { int p=offset+12+c*16;clip.q[f,c]=new Quaternion(BitConverter.ToSingle(data,p),BitConverter.ToSingle(data,p+4),BitConverter.ToSingle(data,p+8),BitConverter.ToSingle(data,p+12)); }
                }
                clips.Add(key,clip);
            }
            hasPose=false;lastClock=-1;CurrentClip="";
        }
        public void Apply(FighterState f,MatchRules rules,float clock,bool moving)
        {
            if(!Ready)return;
            float dx=f.x-lastX;lastX=f.x;
            string key="idle";
            if(f.health<=0 || f.stun>0 || (f.guard && f.attack<0))key="procedural";
            else if(f.attack>=0)key=Keys[4+f.attack];
            else if(f.crouch)key="crouch";
            else if(moving)key=Mathf.Abs(dx)<.00001f && CurrentClip.StartsWith("walk_")?CurrentClip:dx*f.facing<0?"walk_back":"walk_forward";
            bool restart=key!=CurrentClip || (f.attack>=0 && f.attackFrame<lastAttackFrame) || clock<lastClock;
            if(restart)
            {
                CurrentClip=key;stateStart=transitionStart=clock;
                Array.Copy(previous,transition,previous.Length);transitionPosition=previousPosition;
            }
            lastClock=clock;lastAttackFrame=f.attack>=0?f.attackFrame:-1;
            Weight=key=="procedural"?0:1;
            if(Weight>0)
            {
                var clip=clips[key];int end=clip.p.Length-1;
                if(f.attack>=0)
                {
                    var move=rules.moves[f.attack];int strike=Strike[f.attack];
                    SampleFrame=f.attackFrame<=move.startup?Mathf.Lerp(0,strike,(float)f.attackFrame/move.startup):
                        Mathf.Lerp(strike,end,(float)(f.attackFrame-move.startup)/Mathf.Max(1,move.active+move.recovery-1));
                }
                else SampleFrame=Mathf.Repeat((clock-stateStart)*60,clip.p.Length);
                int a=Mathf.Clamp((int)SampleFrame,0,end),b=f.attack>=0?Mathf.Min(a+1,end):(a+1)%clip.p.Length;
                float t=SampleFrame-a;
                float blend=hasPose?Mathf.Clamp01((clock-transitionStart)/.08f):1;
                // Complete the transition before an attack reaches its impact frame.
                if(f.attack>=0 && f.attackFrame>=rules.moves[f.attack].startup)blend=1;
                bones[0].localPosition=Vector3.Lerp(transitionPosition,Vector3.Lerp(clip.p[a],clip.p[b],t),blend);
                for(int i=0;i<bones.Length;i++)bones[i].localRotation=Quaternion.Slerp(transition[i],Quaternion.Slerp(clip.q[a,i],clip.q[b,i],t),blend);
            }
            previousPosition=bones[0].localPosition;
            for(int i=0;i<bones.Length;i++)previous[i]=bones[i].localRotation;
            hasPose=true;
        }
    }
}
