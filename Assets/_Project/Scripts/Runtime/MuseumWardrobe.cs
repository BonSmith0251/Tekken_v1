using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Versus.Remaster
{
    // Dresses an instantiated character from wardrobe pieces. Every piece shares the
    // character's bind skeleton, so its renderer is rebound to the model's bones by name.
    // Piece-specific helper bones are copied in at their bind pose under the same parent.
    // Dress a freshly instantiated model before posing it: rest poses are compared at bind.
    public static class MuseumWardrobe
    {
        // Wardrobe renderers are named after their piece; the versus model keeps the source
        // mesh name (SK_CH_jin_bdu_1P), which names the same piece.
        public static string PieceName(string rendererName)=>rendererName.StartsWith("SK_CH_",StringComparison.OrdinalIgnoreCase)?rendererName.Substring(6):rendererName;

        // The worn piece index per slot for a loaded outfit (-1 when the slot is empty).
        public static int[] Find(GameObject model,CharacterWardrobe wardrobe)
        {
            var worn=new HashSet<string>(model.GetComponentsInChildren<SkinnedMeshRenderer>().Select(r=>PieceName(r.name)),StringComparer.OrdinalIgnoreCase);
            return wardrobe.slots.Select(slot=>Array.FindIndex(slot.pieces,p=>worn.Contains(p.piece))).ToArray();
        }

        // Replaces every wardrobe renderer on the model with the selected piece of each slot.
        // A worn head item swaps its dependencyBase hair for the trimmed variant it names.
        public static void Dress(GameObject model,CharacterWardrobe wardrobe,int[] selection)
        {
            if(selection.Length!=wardrobe.slots.Length)throw new ArgumentException("One selection per wardrobe slot is required.");
            var selected=new List<CharacterWardrobe.Piece>();
            for(int s=0;s<wardrobe.slots.Length;s++)
            {
                var slot=wardrobe.slots[s];int index=selection[s];
                if(index<0){if(!slot.optional)throw new InvalidOperationException("Slot "+slot.name+" needs an item.");continue;}
                if(index>=slot.pieces.Length)throw new ArgumentOutOfRangeException(nameof(selection));
                selected.Add(slot.pieces[index]);
            }
            var prefabs=selected.Select(p=>p.prefab).ToList();
            foreach(var head in selected.Where(p=>p.dependency))
            {
                int hair=selected.FindIndex(p=>string.Equals(p.piece,head.dependencyBase,StringComparison.OrdinalIgnoreCase));
                if(hair>=0)prefabs[hair]=head.dependency;
            }
            var names=new HashSet<string>(wardrobe.slots.SelectMany(s=>s.pieces).SelectMany(p=>new[]{p.piece,p.dependency?p.dependency.GetComponentInChildren<SkinnedMeshRenderer>(true).name:p.dependencyRenderer}.Concat(p.replaces??new string[0])).Where(n=>!string.IsNullOrEmpty(n)),StringComparer.OrdinalIgnoreCase);
            // A player build packages only saved pieces. A piece without a prefab that the model
            // already wears (a required slot with nothing saved) stays on the model as it is.
            var worn=new HashSet<string>(model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r=>PieceName(r.name)),StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<prefabs.Count;i++)
            {
                if(prefabs[i])continue;
                if(!worn.Contains(selected[i].piece))throw new InvalidOperationException("Wardrobe piece "+selected[i].name+" was not packaged into this build.");
                names.Remove(selected[i].piece);
                foreach(var replaced in selected[i].replaces??new string[0])names.Remove(replaced);
            }
            foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if(names.Contains(PieceName(renderer.name))){renderer.gameObject.SetActive(false);Discard(renderer.gameObject);}
            var animated=new HashSet<string>(wardrobe.character.boneNames??new string[0],StringComparer.OrdinalIgnoreCase);
            foreach(var prefab in prefabs.Where(p=>p))Attach(model,prefab,animated);
        }

        // Rest poses are per mesh in the source: a helper bone shared by name can rest differently in
        // two items (Xiaoyu's Formal skirt vs her Default 1P coat). Such a helper gets a copy local to
        // this piece, at the piece's own rest; bones the animation drives always stay shared.
        static void Attach(GameObject model,GameObject prefab,ICollection<string> animated)
        {
            if(!prefab)throw new InvalidOperationException("Missing wardrobe prefab.");
            var skeleton=model.transform.Find("Skeleton");
            if(!skeleton)throw new InvalidOperationException("Model has no Skeleton root.");
            var map=new Dictionary<string,Transform>(StringComparer.OrdinalIgnoreCase);
            foreach(var bone in skeleton.GetComponentsInChildren<Transform>(true))map[bone.name]=bone;
            // Instantiate under the model so the piece never enters another open scene.
            var source=UnityEngine.Object.Instantiate(prefab,model.transform);
            try
            {
                foreach(var renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var adopted=new Dictionary<Transform,Transform>();var local=new HashSet<Transform>();
                    Transform Adopt(Transform bone)
                    {
                        if(adopted.TryGetValue(bone,out var known))return known;
                        var parent=bone.parent && bone.parent.name!="Skeleton"?Adopt(bone.parent):skeleton;
                        Transform result;
                        if(!local.Contains(parent) && map.TryGetValue(bone.name,out var existing) && (animated.Contains(bone.name) || SameRest(existing,bone)))result=existing;
                        else if(!local.Contains(parent) && !map.ContainsKey(bone.name)){result=Copy(bone,parent,bone.name);map[bone.name]=result;}
                        else{result=Copy(bone,parent,renderer.name+"_"+bone.name);local.Add(result);}
                        adopted[bone]=result;return result;
                    }
                    renderer.bones=renderer.bones.Select(Adopt).ToArray();
                    renderer.rootBone=Adopt(renderer.rootBone);
                    renderer.transform.SetParent(model.transform,false);
                }
            }
            finally{source.SetActive(false);Discard(source);}
        }

        static bool SameRest(Transform a,Transform b)=>
            (a.localPosition-b.localPosition).sqrMagnitude<1e-8f && Quaternion.Angle(a.localRotation,b.localRotation)<.5f && (a.localScale-b.localScale).sqrMagnitude<1e-6f;

        static Transform Copy(Transform bone,Transform parent,string name)
        {
            var copy=new GameObject(name).transform;copy.SetParent(parent,false);
            copy.localPosition=bone.localPosition;copy.localRotation=bone.localRotation;copy.localScale=bone.localScale;
            return copy;
        }

        static void Discard(GameObject value)
        {
            if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
