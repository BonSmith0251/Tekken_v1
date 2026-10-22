using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Versus.Remaster
{
    // Dresses an instantiated character from wardrobe pieces. Every piece shares the
    // character's bind skeleton, so its renderer is rebound to the model's bones by name.
    // Piece-specific helper bones are copied in at their bind pose under the same parent.
    public static class MuseumWardrobe
    {
        // Wardrobe renderers are named after their piece; the versus model keeps the source
        // mesh name (SK_CH_jin_bdu_1P), which names the same piece.
        static string PieceName(string rendererName)=>rendererName.StartsWith("SK_CH_",StringComparison.OrdinalIgnoreCase)?rendererName.Substring(6):rendererName;

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
            var names=new HashSet<string>(wardrobe.slots.SelectMany(s=>s.pieces).SelectMany(p=>new[]{p.piece,p.dependency?p.dependency.GetComponentInChildren<SkinnedMeshRenderer>(true).name:null}).Where(n=>n!=null),StringComparer.OrdinalIgnoreCase);
            foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if(names.Contains(PieceName(renderer.name))){renderer.gameObject.SetActive(false);Discard(renderer.gameObject);}
            foreach(var prefab in prefabs)Attach(model,prefab);
        }

        static void Attach(GameObject model,GameObject prefab)
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
                    renderer.bones=renderer.bones.Select(b=>Adopt(b,map,skeleton)).ToArray();
                    renderer.rootBone=Adopt(renderer.rootBone,map,skeleton);
                    renderer.transform.SetParent(model.transform,false);
                }
            }
            finally{source.SetActive(false);Discard(source);}
        }

        static Transform Adopt(Transform bone,Dictionary<string,Transform> map,Transform skeleton)
        {
            if(map.TryGetValue(bone.name,out var existing))return existing;
            var parent=bone.parent && bone.parent.name!="Skeleton"?Adopt(bone.parent,map,skeleton):skeleton;
            var copy=new GameObject(bone.name).transform;copy.SetParent(parent,false);
            copy.localPosition=bone.localPosition;copy.localRotation=bone.localRotation;copy.localScale=bone.localScale;
            map[bone.name]=copy;return copy;
        }

        static void Discard(GameObject value)
        {
            if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
