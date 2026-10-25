using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Versus.Remaster
{
    // Unity secondary simulation on the original weighted rig. These are adapted
    // spring/cloth settings, not a reconstruction of Unreal's original solver.
    public sealed class XiaoyuSecondaryDynamics
    {
        sealed class Spring
        {
            public Transform bone,child;
            public Quaternion rest;
            public Vector3 tip,previous;
            public float stiffness,limit,gravity;
        }
        sealed class BodyCapsule
        {
            public Transform first,last;
            public CapsuleCollider collider;
        }
        readonly Transform actor,head,pelvis,body,garmentRoot;
        readonly Quaternion garmentRest;
        readonly List<Spring> springs=new List<Spring>();
        readonly List<Cloth> garments=new List<Cloth>();
        readonly List<BodyCapsule> capsules=new List<BodyCapsule>();
        Vector3 lastRoot;
        float accumulator;
        bool initialized;
        int simulationFrame=-1;
        public int SpringCount=>springs.Count;
        public int GarmentCount=>garments.Count;
        public int FreeClothVertices {get;private set;}

        public XiaoyuSecondaryDynamics(Transform model)
        {
            actor=model;
            var bones=model.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t,StringComparer.OrdinalIgnoreCase);
            head=bones["Head"];
            pelvis=bones["Hip"];body=bones["Base"];
            garmentRoot=new GameObject("Skirt suspension").transform;garmentRoot.SetParent(actor,false);
            garmentRoot.SetPositionAndRotation(pelvis.position,pelvis.rotation);
            garmentRest=Quaternion.Inverse(body.rotation)*pelvis.rotation;
            foreach(var bone in model.GetComponentsInChildren<Transform>())
            {
                string name=bone.name;
                bool hair=name.StartsWith("Hair_",StringComparison.OrdinalIgnoreCase)&&name.Contains("_OPT");
                bool accessory=(name.StartsWith("Feather_",StringComparison.OrdinalIgnoreCase)||name.Contains("_Leg_OPT")||name.StartsWith("Spine2_OPT",StringComparison.OrdinalIgnoreCase));
                if(!hair&&!accessory)continue;
                var child=bone.Cast<Transform>().FirstOrDefault(t=>t.name.Contains("_OPT"));if(!child)continue;
                bool fringe=name.StartsWith("Hair_F_",StringComparison.OrdinalIgnoreCase);
                springs.Add(new Spring {bone=bone,child=child,rest=bone.localRotation,
                    stiffness=fringe?420:hair?180:260,limit=(fringe?25:hair?70:30)*Mathf.Deg2Rad,gravity=hair?.65f:.25f});
            }
            AddCapsule(bones["Hip"],bones["Neck"],.115f);
            foreach(string side in new[]{"L","R"})
            {
                AddCapsule(bones[side+"_UpLeg"],bones[side+"_Leg"],.115f);
                AddCapsule(bones[side+"_Leg"],bones[side+"_Foot"],.055f);
            }
            UpdateCapsules();
            foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())ConfigureCloth(renderer);
            Reset();
        }

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
        void ConfigureCloth(SkinnedMeshRenderer renderer)
        {
            if(!renderer.name.StartsWith("xia_b",StringComparison.OrdinalIgnoreCase))return;
            var mesh=renderer.sharedMesh;var vertices=mesh.vertices;var distances=new float[vertices.Length];
            var suspension=new float[vertices.Length];bool softSkirt=false,formalSkirt=false;var clothSections=new List<int>();
            for(int section=0;section<mesh.subMeshCount;section++)
            {
                string name=renderer.sharedMaterials[section].name.ToLowerInvariant();
                bool coat=name.Contains("1p_coat"),classic=name.Contains("_cloth"),skirt=name.Contains("_skirt"),formal=name.Contains("bdl_formal_a"),shirt=name.Contains("_syatu");
                if(!coat&&!classic&&!skirt&&!formal&&!shirt)continue;
                // Formal uses the same material on a separate waistband panel.
                // Keep that panel fully attached rather than simulating its seam.
                if(formal&&mesh.GetTriangles(section).Min(i=>vertices[i].y)>1f)continue;
                clothSections.Add(section);
                softSkirt|=skirt||formal;
                formalSkirt|=formal;
                float waist=coat?1.04f:classic?1.08f:skirt?1.075f:formal?1.08f:1.01f;
                float hem=coat?.35f:classic?.66f:skirt?.82f:formal?.81f:.93f;
                float reach=coat?.12f:classic?.075f:skirt?.15f:formal?.16f:.018f;
                foreach(int index in mesh.GetTriangles(section))
                {
                    float weight=Mathf.Clamp01((waist-vertices[index].y)/Mathf.Max(.05f,waist-hem));
                    distances[index]=Mathf.Max(distances[index],reach*(skirt?Mathf.Sqrt(weight):formal?weight:weight*weight));
                    if(skirt||formal)suspension[index]=Mathf.Max(suspension[index],Mathf.SmoothStep(0,.95f,weight));
                }
            }
            if(!distances.Any(d=>d>.001f))return;
            var cleanup=renderer.gameObject.AddComponent<XiaoyuGarmentMeshLifetime>();
            if(softSkirt)
            {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    string name=materials[i].name.ToLowerInvariant();if(!name.Contains("_skirt")&&!name.Contains("bdl_formal_a"))continue;
                    var material=new Material(materials[i]);materials[i]=material;cleanup.materials.Add(material);
                    material.SetFloat("_DoubleSidedEnable",1);material.SetFloat("_DoubleSidedNormalMode",0);
                    material.SetVector("_DoubleSidedConstants",new Vector4(-1,-1,-1,0));
                    material.SetFloat("_CullMode",0);material.SetFloat("_CullModeForward",0);material.EnableKeyword("_DOUBLESIDED_ON");
                }
                renderer.sharedMaterials=materials;
            }
            // A skirt hangs from the waist; thigh helper rotations must not
            // pull its hem sideways like a second pair of trousers. Adapt only
            // the live garment vertices, leaving imported meshes/skin intact.
            if(suspension.Any(w=>w>0))
            {
                var live=UnityEngine.Object.Instantiate(mesh);live.name=mesh.name+" suspended garment";
                int attachment=renderer.bones.Length;
                live.bindposes=mesh.bindposes.Concat(new[]{garmentRoot.worldToLocalMatrix*renderer.transform.localToWorldMatrix}).ToArray();
                var weights=mesh.boneWeights;
                for(int i=0;i<weights.Length;i++)
                {
                    float blend=suspension[i];if(blend<=0)continue;var w=weights[i];
                    var influences=new[]{new KeyValuePair<int,float>(w.boneIndex0,w.weight0*(1-blend)),new KeyValuePair<int,float>(w.boneIndex1,w.weight1*(1-blend)),new KeyValuePair<int,float>(w.boneIndex2,w.weight2*(1-blend)),new KeyValuePair<int,float>(w.boneIndex3,w.weight3*(1-blend)),new KeyValuePair<int,float>(attachment,blend)}.OrderByDescending(p=>p.Value).Take(4).ToArray();
                    float sum=influences.Sum(p=>p.Value);
                    weights[i]=new BoneWeight {boneIndex0=influences[0].Key,weight0=influences[0].Value/sum,boneIndex1=influences[1].Key,weight1=influences[1].Value/sum,boneIndex2=influences[2].Key,weight2=influences[2].Value/sum,boneIndex3=influences[3].Key,weight3=influences[3].Value/sum};
                }
                live.boneWeights=weights;renderer.sharedMesh=live;renderer.bones=renderer.bones.Concat(new[]{garmentRoot}).ToArray();
                cleanup.meshes.Add(live);
            }
            renderer=SeparateGarment(renderer,clothSections,cleanup);
            var cloth=renderer.gameObject.AddComponent<Cloth>();var coefficients=cloth.coefficients;
            // Cloth may weld repeated vertices; map its native particles back to
            // original mesh positions instead of assuming identical buffer sizes.
            var points=cloth.vertices;
            var source=new Dictionary<Vector3Int,float>();
            for(int i=0;i<vertices.Length;i++){var key=Vector3Int.RoundToInt(vertices[i]*10000);source.TryGetValue(key,out float old);source[key]=Mathf.Max(old,distances[i]);}
            if(coefficients.Length==vertices.Length)
            {
                for(int i=0;i<coefficients.Length;i++){coefficients[i].maxDistance=distances[i];coefficients[i].collisionSphereDistance=0;if(distances[i]>.001f)FreeClothVertices++;}
            }
            else
            {
                // Quantized bind-space lookup avoids quadratic startup cost.
                for(int i=0;i<coefficients.Length;i++)
                {
                    source.TryGetValue(Vector3Int.RoundToInt(points[i]*10000),out float freedom);
                    coefficients[i].maxDistance=freedom;coefficients[i].collisionSphereDistance=0;if(freedom>.001f)FreeClothVertices++;
                }
            }
            cloth.coefficients=coefficients;cloth.useGravity=true;cloth.useTethers=true;
            cloth.stretchingStiffness=1;cloth.bendingStiffness=formalSkirt?.4f:softSkirt?.35f:.75f;cloth.damping=.5f;
            cloth.worldVelocityScale=.35f;cloth.worldAccelerationScale=.45f;cloth.clothSolverFrequency=90;
            cloth.enableContinuousCollision=true;cloth.friction=.25f;
            cloth.capsuleColliders=capsules.Select(c=>c.collider).ToArray();garments.Add(cloth);
        }
        // Unity Cloth skins every vertex of its renderer itself and does not support the
        // rig's non-uniformly scaled neck/thigh helpers, which balloons skin sharing a
        // mesh with fabric. Move only the simulated sections onto their own renderer.
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
            UpdateSuspension();
            foreach(var spring in springs)spring.bone.localRotation=spring.rest;
            foreach(var spring in springs)spring.tip=spring.previous=spring.child.position;
            foreach(var cloth in garments)cloth.ClearTransformMotion();
            lastRoot=actor.position;accumulator=0;initialized=true;
        }
        public void Tick(float elapsed)
        {
            if(simulationFrame==Time.frameCount)return;
            simulationFrame=Time.frameCount;
            UpdateSuspension();
            UpdateCapsules();
            if(!initialized || Vector3.Distance(actor.position,lastRoot)>.5f || elapsed>.2f)Reset();
            lastRoot=actor.position;
            accumulator+=Mathf.Clamp(elapsed,0,.0667f);
            const float step=1f/60;
            while(accumulator>=step){Simulate(step);accumulator-=step;}
        }
        void UpdateSuspension()=>garmentRoot.SetPositionAndRotation(pelvis.position,body.rotation*garmentRest);
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
                if(spring.bone.name.StartsWith("Hair_",StringComparison.OrdinalIgnoreCase))
                {
                    Vector3 center=head.position+Vector3.up*.11f,away=predicted-center;
                    if(away.magnitude<.10f)predicted=center+away.normalized*.10f;
                    direction=Vector3.RotateTowards(restDirection.normalized,(predicted-root).normalized,spring.limit,0);
                }
                spring.bone.rotation=Quaternion.FromToRotation(restDirection,direction)*restRotation;
                spring.tip=spring.child.position;
            }
        }
    }
    public sealed class XiaoyuGarmentMeshLifetime:MonoBehaviour
    {
        public readonly List<Mesh> meshes=new List<Mesh>();
        public readonly List<Material> materials=new List<Material>();
        void OnDestroy(){foreach(var mesh in meshes)Discard(mesh);foreach(var material in materials)Discard(material);}
        // Editor previews (wardrobe capture) build garments outside Play mode.
        static void Discard(UnityEngine.Object value){if(!value)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
    }
}
