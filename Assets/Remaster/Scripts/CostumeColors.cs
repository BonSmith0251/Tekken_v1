using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Versus.Remaster
{
    // Recolors worn wardrobe items the way the game's customization does (CostumeTint.shader):
    // each tintable material gets an instance whose base color map is re-tinted on the GPU from
    // its untinted map and color mask. Colors are per piece and per color slot (Color01..04);
    // a slot without an override keeps the game's default. Materials whose import already bakes
    // the defaults are only instanced when the piece has an override.
    public static class CostumeColors
    {
        public const int Slots=4;
        public const string InstanceSuffix=" (colors)";
        static Material blit;
        static Texture2D fullMask;

        // The imported material name behind a recolored instance.
        public static string SourceName(Material material)=>material.name.EndsWith(InstanceSuffix)?material.name.Substring(0,material.name.Length-InstanceSuffix.Length):material.name;

        // overrides: piece name -> four optional sRGB colors.
        public static void Apply(GameObject model,CharacterWardrobe wardrobe,IDictionary<string,Color?[]> overrides)
        {
            var tints=new Dictionary<string,CharacterWardrobe.Piece>(StringComparer.OrdinalIgnoreCase);
            foreach(var piece in wardrobe.slots.SelectMany(s=>s.pieces))if(piece.tints!=null && piece.tints.Length>0)tints[piece.piece]=piece;
            // A hat's trimmed hair variant shares its base hair's materials and colors.
            var alias=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var head in wardrobe.slots.SelectMany(s=>s.pieces).Where(p=>p.dependency && tints.ContainsKey(p.dependencyBase??"")))
            {
                var variant=head.dependency.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if(variant && !tints.ContainsKey(variant.name)){tints[variant.name]=tints[head.dependencyBase];alias[variant.name]=head.dependencyBase;}
            }
            var holder=model.GetComponent<CostumeTintHolder>()??model.AddComponent<CostumeTintHolder>();
            foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                // Cloth splits a garment onto "<piece> garment"; it shares the piece's material instances.
                string name=MuseumWardrobe.PieceName(renderer.name.EndsWith(" garment")?renderer.name.Substring(0,renderer.name.Length-8):renderer.name);
                if(!tints.TryGetValue(name,out var piece))continue;
                overrides.TryGetValue(alias.TryGetValue(name,out var baseHair)?baseHair:name,out var colors);
                bool overridden=colors!=null && colors.Any(c=>c.HasValue);
                var materials=renderer.sharedMaterials;bool changed=false;
                for(int i=0;i<materials.Length;i++)
                {
                    var original=holder.OriginalOf(materials[i]);if(!original)continue;
                    var tint=piece.tints.FirstOrDefault(t=>Key(t.material)==Key(original.name));
                    if(tint==null || !tint.baseMap)continue;
                    if(tint.baked && !overridden)
                    {
                        if(materials[i]!=original){materials[i]=original;changed=true;}
                        continue;
                    }
                    var instance=holder.InstanceFor(original,tint.baseMap);
                    Render(holder.TargetOf(instance),tint,colors);
                    if(materials[i]!=instance){materials[i]=instance;changed=true;}
                }
                if(changed)renderer.sharedMaterials=materials;
            }
        }

        // The color a slot shows: the override, or the default of the first material using the slot.
        public static Color Current(CharacterWardrobe.Piece piece,Color?[] colors,int slot)
        {
            if(colors!=null && colors[slot].HasValue)return colors[slot].Value;
            var tint=piece.tints.FirstOrDefault(t=>t.slots[slot])??piece.tints[0];
            return tint.colors[slot].gamma;
        }

        public static bool[] UsedSlots(CharacterWardrobe.Piece piece)=>
            Enumerable.Range(0,Slots).Select(s=>piece.tints!=null && piece.tints.Any(t=>t.slots!=null && t.slots[s])).ToArray();

        static void Render(RenderTexture target,CharacterWardrobe.Tint tint,Color?[] colors)
        {
            if(!blit)blit=new Material(Resources.Load<Shader>("CostumeTint")){hideFlags=HideFlags.HideAndDontSave};
            // Hair has no mask: Color01 covers the whole texture.
            if(!fullMask){fullMask=new Texture2D(1,1,TextureFormat.RGBA32,false,true){hideFlags=HideFlags.HideAndDontSave};fullMask.SetPixel(0,0,new Color(1,0,0,1));fullMask.Apply();}
            blit.SetTexture("_MaskTex",tint.maskMap?tint.maskMap:fullMask);
            for(int s=0;s<Slots;s++)
            {
                // Saved colors are what the user picked (sRGB); the shader mixes in linear space.
                var color=colors!=null && colors[s].HasValue?colors[s].Value.linear:tint.colors[s];
                blit.SetColor("_Color"+(s+1),color);
            }
            Graphics.Blit(tint.baseMap,target,blit);
            target.GenerateMips();
        }

        static string Key(string name)=>new string(name.Select(c=>char.IsLetterOrDigit(c)?c:'_').ToArray()).ToLowerInvariant();
    }

    // Owns the material instances and tinted textures of one dressed model.
    public sealed class CostumeTintHolder:MonoBehaviour
    {
        readonly Dictionary<Material,Material> instances=new Dictionary<Material,Material>();
        readonly Dictionary<Material,Material> originals=new Dictionary<Material,Material>();
        readonly Dictionary<Material,RenderTexture> targets=new Dictionary<Material,RenderTexture>();

        public Material OriginalOf(Material material)=>material && originals.TryGetValue(material,out var original)?original:material;
        public RenderTexture TargetOf(Material instance)=>targets[instance];

        public Material InstanceFor(Material original,Texture source)
        {
            if(instances.TryGetValue(original,out var instance))return instance;
            var target=new RenderTexture(source.width,source.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB)
                {useMipMap=true,autoGenerateMips=false,wrapMode=TextureWrapMode.Repeat,anisoLevel=4,name=original.name+" colors"};
            target.Create();
            instance=new Material(original){name=original.name+CostumeColors.InstanceSuffix};
            instance.SetTexture("_BaseColorMap",target);
            instances[original]=instance;originals[instance]=original;targets[instance]=target;
            return instance;
        }

        void OnDestroy()
        {
            foreach(var target in targets.Values)if(target){target.Release();Discard(target);}
            foreach(var instance in instances.Values)Discard(instance);
        }
        static void Discard(UnityEngine.Object value){if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
    }
}
