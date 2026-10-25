using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Versus.Remaster
{
    // Breast motion for female fighters, with one user setting: flexibility 0 (held still) to 1
    // (soft, bouncy). The game's skeleton has two bust chains under Spine2 that items skin to:
    // OPT0_bustL > OPT1_bustL and OPT2_bustR > OPT3_bustR, and the SWG_*__swing pair. Each chain
    // root swings toward its child like the wardrobe's strand springs; both kinds move alike so
    // items mixing them stay together. Only chains the worn meshes are skinned to are simulated,
    // and only for female fighters: those whose default outfit is skinned to the bust chains (a few
    // common tops fitted to male fighters are skinned to them too, and must not bounce).
    public sealed class BustDynamics
    {
        public const float DefaultFlexibility=.5f;
        static readonly (string root,string child)[] Chains={
            ("OPT0_bustL","OPT1_bustL"),("OPT2_bustR","OPT3_bustR"),
            ("SWG_OPT0_bustL__swing","SWG_OPT1_bustL__swing"),("SWG_OPT2_bustR__swing","SWG_OPT3_bustR__swing")};

        sealed class Spring
        {
            public Transform bone,child;
            public Quaternion rest;
            public Vector3 tip,previous,origin;
        }
        readonly Transform actor;
        readonly List<Spring> springs=new List<Spring>();
        Vector3 lastRoot;
        float lastStep=1f/120,flexibility,stiffness,retention,limit;
        bool initialized;
        int simulationFrame=-1;
        public int ChainCount=>springs.Count;

        // Bust bones of every chain, including copies a piece carries under its own prefix.
        public static bool IsBustBone(string name)=>name.IndexOf("bust",StringComparison.OrdinalIgnoreCase)>=0;

        static readonly Dictionary<MuseumCharacter,bool> female=new Dictionary<MuseumCharacter,bool>();

        // Whether a fighter has breasts: their default outfit is skinned to a bust chain.
        public static bool IsFemale(MuseumCharacter character)
        {
            if(!character)return false;
            if(female.TryGetValue(character,out bool known))return known;
            var prefab=character.costumes!=null && character.costumes.Length>0?character.costumes[0].prefab:character.prefab;
            bool result=prefab && Weighted(prefab.transform).Any(b=>b && IsBustBone(b.name));
            female[character]=result;return result;
        }

        // Null for male fighters and when nothing the model wears is skinned to a bust chain.
        public static BustDynamics Create(Transform model,MuseumCharacter character,float flexibility)
        {
            if(!IsFemale(character))return null;
            var dynamics=new BustDynamics(model,flexibility);
            return dynamics.springs.Count>0?dynamics:null;
        }

        static HashSet<Transform> Weighted(Transform model)
        {
            var weighted=new HashSet<Transform>();
            foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh=renderer.sharedMesh;if(!mesh)continue;var bones=renderer.bones;
                foreach(var w in mesh.boneWeights)
                {
                    if(w.weight0>.01f)weighted.Add(bones[w.boneIndex0]);if(w.weight1>.01f)weighted.Add(bones[w.boneIndex1]);
                    if(w.weight2>.01f)weighted.Add(bones[w.boneIndex2]);if(w.weight3>.01f)weighted.Add(bones[w.boneIndex3]);
                }
            }
            return weighted;
        }

        BustDynamics(Transform model,float value)
        {
            actor=model;
            var weighted=Weighted(model);
            foreach(var bone in model.GetComponentsInChildren<Transform>())
            foreach(var (root,childName) in Chains)
            {
                if(!bone.name.EndsWith(root,StringComparison.OrdinalIgnoreCase))continue;
                var child=bone.Cast<Transform>().FirstOrDefault(t=>t.name.EndsWith(childName,StringComparison.OrdinalIgnoreCase));
                // The chain moves skin only if the root or anything below it carries weights.
                if(child && bone.GetComponentsInChildren<Transform>().Any(weighted.Contains))
                    springs.Add(new Spring {bone=bone,child=child,rest=bone.localRotation});
            }
            Flexibility=value;
            Reset();
        }

        public float Flexibility
        {
            get=>flexibility;
            set
            {
                flexibility=Mathf.Clamp01(value);
                // Stiff and damped at the low end, soft with a few visible bounces at the top.
                stiffness=Mathf.Lerp(900,110,flexibility);
                retention=Mathf.Lerp(.78f,.92f,flexibility);
                limit=Mathf.Lerp(3,24,flexibility)*Mathf.Deg2Rad;
            }
        }

        public void Reset()
        {
            foreach(var spring in springs)spring.bone.localRotation=spring.rest;
            foreach(var spring in springs){spring.tip=spring.previous=spring.child.position;spring.origin=spring.bone.position;}
            lastRoot=actor.position;lastStep=1f/120;initialized=true;
        }

        public void Tick(float elapsed)
        {
            if(simulationFrame==Time.frameCount)return;
            simulationFrame=Time.frameCount;
            if(!initialized || Vector3.Distance(actor.position,lastRoot)>.5f || elapsed>.2f)Reset();
            lastRoot=actor.position;
            foreach(var spring in springs)spring.bone.localRotation=spring.rest;
            if(flexibility<=0){foreach(var spring in springs){spring.tip=spring.previous=spring.child.position;spring.origin=spring.bone.position;}return;}
            float time=Mathf.Clamp(elapsed,0,.05f);if(time<=0)return;
            int count=Mathf.CeilToInt(time*120);
            // Follow most of the chest's translation so knockdowns and snaps don't fling the chains;
            // the bounce comes from the body's acceleration and turning.
            const float carry=.85f;
            foreach(var spring in springs)
            {
                Vector3 moved=(spring.bone.position-spring.origin)*carry;
                spring.tip+=moved;spring.previous+=moved;spring.origin=spring.bone.position;
            }
            for(int i=0;i<count;i++)Simulate(time/count);
        }

        void Simulate(float step)
        {
            foreach(var spring in springs)spring.bone.localRotation=spring.rest;
            foreach(var spring in springs)
            {
                Vector3 root=spring.bone.position,restTip=spring.child.position,restDirection=restTip-root;
                float length=restDirection.magnitude;if(length<.0005f)continue;
                Quaternion restRotation=spring.bone.rotation;
                Vector3 predicted=spring.tip+(spring.tip-spring.previous)*(step/lastStep)*Mathf.Pow(retention,step*60)+
                    (restTip-spring.tip)*stiffness*(step*step);
                spring.previous=spring.tip;
                Vector3 direction=Vector3.RotateTowards(restDirection.normalized,(predicted-root).normalized,limit,0);
                spring.bone.rotation=Quaternion.FromToRotation(restDirection,direction)*restRotation;
                spring.tip=spring.child.position;
            }
            lastStep=step;
        }
    }
}
