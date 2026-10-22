using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Versus.Remaster
{
    // Secondary motion for whatever a dressed character wears, driven by the source data:
    // Unity Cloth on the sections the game simulates as APEX cloth, and springs on the
    // _OPT helper bone chains (hair strands, ties, tails, armor flaps). The solver settings
    // follow the tested Xiaoyu setup; they are adaptations, not Unreal's original values.
    public sealed class WardrobeDynamics
    {
        sealed class Spring
        {
            public Transform bone,child;
            public Quaternion rest;
            public Vector3 tip,previous;
            public float stiffness,limit,gravity;
            public bool hair;
        }
        sealed class BodyCapsule
        {
            public Transform first,last;
            public CapsuleCollider collider;
        }
        readonly Transform actor,head;
        readonly List<Spring> springs=new List<Spring>();
        readonly List<Cloth> garments=new List<Cloth>();
        readonly List<BodyCapsule> capsules=new List<BodyCapsule>();
        // Per-garment vertex freedom, applied once Unity has built the cloth particles.
        readonly List<(Cloth cloth,Vector3[] vertices,float[] distances)> pending=new List<(Cloth,Vector3[],float[])>();
        Vector3 lastRoot;
        float accumulator;
        bool initialized;
        int simulationFrame=-1;
        public int SpringCount=>springs.Count;
        public int GarmentCount=>garments.Count;
        public int FreeClothVertices {get;private set;}

        public WardrobeDynamics(Transform model,CharacterWardrobe wardrobe)
        {
            actor=model;
            var bones=new Dictionary<string,Transform>(StringComparer.OrdinalIgnoreCase);
            foreach(var t in model.GetComponentsInChildren<Transform>())if(!bones.ContainsKey(t.name))bones[t.name]=t;
            head=bones["Head"];
            foreach(var bone in bones.Values)
            {
                if(bone.name.IndexOf("_OPT",StringComparison.OrdinalIgnoreCase)<0)continue;
                var child=bone.Cast<Transform>().FirstOrDefault(t=>t.name.IndexOf("_OPT",StringComparison.OrdinalIgnoreCase)>=0);if(!child)continue;
                bool hair=bone.name.StartsWith("hair",StringComparison.OrdinalIgnoreCase)||bone.name.StartsWith("Head_",StringComparison.OrdinalIgnoreCase);
                bool fringe=hair&&bone.name.IndexOf("_F_",StringComparison.OrdinalIgnoreCase)>=0;
                springs.Add(new Spring {bone=bone,child=child,rest=bone.localRotation,hair=hair,
                    stiffness=fringe?420:hair?180:260,limit=(fringe?25:hair?70:30)*Mathf.Deg2Rad,gravity=hair?.65f:.25f});
            }
            AddCapsule(bones["Hip"],bones["Neck"],.13f);
            foreach(string side in new[]{"L","R"})
            {
                AddCapsule(bones[side+"_UpLeg"],bones[side+"_Leg"],.12f);
                AddCapsule(bones[side+"_Leg"],bones[side+"_Foot"],.06f);
                AddCapsule(bones[side+"_Arm"],bones[side+"_ForeArm"],.06f);
            }
            UpdateCapsules();
            var cloth=wardrobe.slots.SelectMany(s=>s.pieces).Where(p=>p.cloth!=null && p.cloth.Length>0)
                .GroupBy(p=>p.piece,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>new HashSet<string>(g.First().cloth.Select(Key)),StringComparer.OrdinalIgnoreCase);
            float waist=bones["Hip"].position.y;
            foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                if(cloth.TryGetValue(renderer.name,out var sections))ConfigureCloth(renderer,sections,waist);
            Reset();
        }

        // Material asset names replace punctuation with underscores; compare on that form.
        static string Key(string name)=>new string(name.Select(c=>char.IsLetterOrDigit(c)?c:'_').ToArray()).ToLowerInvariant();

        void AddCapsule(Transform first,Transform last,float radius)
        {
            var node=new GameObject("Garment collision").transform;node.SetParent(actor,false);
            var collider=node.gameObject.AddComponent<CapsuleCollider>();collider.direction=1;collider.radius=radius;collider.isTrigger=true;
            capsules.Add(new BodyCapsule {first=first,last=last,collider=collider});
        }
        void UpdateCapsules()
        {
            foreach(var capsule in capsules)
            {
                Vector3 delta=capsule.last.position-capsule.first.position;
                capsule.collider.transform.SetPositionAndRotation((capsule.first.position+capsule.last.position)*.5f,Quaternion.FromToRotation(Vector3.up,delta.normalized));
                capsule.collider.height=delta.magnitude+capsule.collider.radius*2;
            }
        }

        void ConfigureCloth(SkinnedMeshRenderer renderer,HashSet<string> clothMaterials,float waist)
        {
            var mesh=renderer.sharedMesh;var vertices=mesh.vertices;var distances=new float[vertices.Length];var clothSections=new List<int>();
            for(int section=0;section<mesh.subMeshCount;section++)
            {
                var material=renderer.sharedMaterials[section];
                if(!material || !clothMaterials.Contains(Key(material.name)))continue;
                var indices=mesh.GetTriangles(section);if(indices.Length==0)continue;
                clothSections.Add(section);
                float top=indices.Max(i=>vertices[i].y),bottom=indices.Min(i=>vertices[i].y);
                // Garments hanging past the waist stay attached above it and swing free toward
                // the hem; garments above the waist (hoods, short panels) only drape slightly.
                bool hanging=bottom<waist-.1f;
                float anchor=hanging?Mathf.Min(top,waist+.05f):top,reach=hanging?Mathf.Clamp((anchor-bottom)*.2f,.04f,.15f):.03f;
                foreach(int index in indices)
                {
                    float weight=Mathf.Clamp01((anchor-vertices[index].y)/Mathf.Max(.05f,anchor-bottom));
                    // Skirts modelled flared out at bind (Xiaoyu's Formal) need room to fall back in to the legs.
                    float spread=hanging?Mathf.Max(0,new Vector2(vertices[index].x,vertices[index].z).magnitude-.18f):0;
                    distances[index]=Mathf.Max(distances[index],(reach+spread)*weight*weight);
                }
            }
            if(!distances.Any(d=>d>.001f))return;
            var cleanup=renderer.gameObject.AddComponent<XiaoyuGarmentMeshLifetime>();
            renderer=SeparateGarment(renderer,clothSections,cleanup);
            var cloth=renderer.gameObject.AddComponent<Cloth>();
            pending.Add((cloth,vertices,distances));ApplyFreedom();
            cloth.useGravity=true;cloth.useTethers=true;
            cloth.stretchingStiffness=1;cloth.bendingStiffness=.6f;cloth.damping=.5f;
            cloth.worldVelocityScale=.35f;cloth.worldAccelerationScale=.45f;cloth.clothSolverFrequency=90;
            cloth.enableContinuousCollision=true;cloth.friction=.25f;
            cloth.capsuleColliders=capsules.Select(c=>c.collider).ToArray();garments.Add(cloth);
        }

        // Unity creates cloth particles lazily (not yet in edit-mode previews); until then the
        // limits wait here, and Tick retries so no garment starts with the default unlimited freedom.
        void ApplyFreedom()
        {
            for(int g=pending.Count-1;g>=0;g--)
            {
                var (cloth,vertices,distances)=pending[g];
                if(!cloth){pending.RemoveAt(g);continue;}
                var coefficients=cloth.coefficients;if(coefficients.Length==0)continue;
                // Cloth may weld repeated vertices; map its particles back to original mesh positions.
                var points=cloth.vertices;
                var source=new Dictionary<Vector3Int,float>();
                for(int i=0;i<vertices.Length;i++){var key=Vector3Int.RoundToInt(vertices[i]*10000);source.TryGetValue(key,out float old);source[key]=Mathf.Max(old,distances[i]);}
                for(int i=0;i<coefficients.Length;i++)
                {
                    float freedom=coefficients.Length==vertices.Length?distances[i]:source.TryGetValue(Vector3Int.RoundToInt(points[i]*10000),out float found)?found:0;
                    coefficients[i].maxDistance=freedom;coefficients[i].collisionSphereDistance=0;if(freedom>.001f)FreeClothVertices++;
                }
                cloth.coefficients=coefficients;pending.RemoveAt(g);
            }
        }
        public int PendingGarments=>pending.Count;

        // Unity Cloth skins every vertex of its renderer itself and does not support the rig's
        // non-uniformly scaled helper bones, so only the simulated sections move to their own renderer.
        static SkinnedMeshRenderer SeparateGarment(SkinnedMeshRenderer renderer,List<int> clothSections,XiaoyuGarmentMeshLifetime cleanup)
        {
            var source=renderer.sharedMesh;var materials=renderer.sharedMaterials;
            var kept=Enumerable.Range(0,source.subMeshCount).Where(i=>!clothSections.Contains(i)).ToList();
            if(kept.Count==0)return renderer;
            Mesh Subset(List<int> sections,string suffix)
            {
                var subset=UnityEngine.Object.Instantiate(source);subset.name=source.name+suffix;
                var triangles=sections.Select(source.GetTriangles).ToArray();
                subset.subMeshCount=sections.Count;
                for(int i=0;i<sections.Count;i++)subset.SetTriangles(triangles[i],i);
                cleanup.meshes.Add(subset);return subset;
            }
            var node=new GameObject(renderer.name+" garment");node.transform.SetParent(renderer.transform.parent,false);
            node.transform.SetLocalPositionAndRotation(renderer.transform.localPosition,renderer.transform.localRotation);node.transform.localScale=renderer.transform.localScale;
            var garment=node.AddComponent<SkinnedMeshRenderer>();
            garment.sharedMesh=Subset(clothSections," cloth");garment.bones=renderer.bones;garment.rootBone=renderer.rootBone;
            garment.sharedMaterials=clothSections.Select(i=>materials[i]).ToArray();
            garment.updateWhenOffscreen=renderer.updateWhenOffscreen;garment.quality=renderer.quality;
            renderer.sharedMesh=Subset(kept," body");renderer.sharedMaterials=kept.Select(i=>materials[i]).ToArray();
            return garment;
        }

        public void Reset()
        {
            UpdateCapsules();
            foreach(var spring in springs)spring.bone.localRotation=spring.rest;
            foreach(var spring in springs)spring.tip=spring.previous=spring.child.position;
            foreach(var cloth in garments)cloth.ClearTransformMotion();
            lastRoot=actor.position;accumulator=0;initialized=true;
        }

        public void Tick(float elapsed)
        {
            if(simulationFrame==Time.frameCount)return;
            simulationFrame=Time.frameCount;
            if(pending.Count>0)ApplyFreedom();
            UpdateCapsules();
            if(!initialized || Vector3.Distance(actor.position,lastRoot)>.5f || elapsed>.2f)Reset();
            lastRoot=actor.position;
            accumulator+=Mathf.Clamp(elapsed,0,.0667f);
            const float step=1f/60;
            while(accumulator>=step){Simulate(step);accumulator-=step;}
        }

        void Simulate(float step)
        {
            foreach(var spring in springs)spring.bone.localRotation=spring.rest;
            foreach(var spring in springs)
            {
                Vector3 root=spring.bone.position,restTip=spring.child.position,restDirection=restTip-root;
                float length=restDirection.magnitude;if(length<.001f)continue;
                Quaternion restRotation=spring.bone.rotation;
                Vector3 predicted=spring.tip+(spring.tip-spring.previous)*.84f+
                    (Physics.gravity*spring.gravity+(restTip-spring.tip)*spring.stiffness)*(step*step);
                spring.previous=spring.tip;
                Vector3 direction=Vector3.RotateTowards(restDirection.normalized,(predicted-root).normalized,spring.limit,0);
                predicted=root+direction*length;
                if(spring.hair)
                {
                    // Keep strands outside the skull.
                    Vector3 center=head.position+Vector3.up*.11f,away=predicted-center;
                    if(away.magnitude<.10f)predicted=center+away.normalized*.10f;
                    direction=Vector3.RotateTowards(restDirection.normalized,(predicted-root).normalized,spring.limit,0);
                }
                spring.bone.rotation=Quaternion.FromToRotation(restDirection,direction)*restRotation;
                spring.tip=spring.child.position;
            }
        }
    }
}
