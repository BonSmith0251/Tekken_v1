using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Versus.Remaster
{
    // The wardrobe items saved for use outside the wardrobe, by piece name per slot. Every scene
    // that shows the character (versus, museum, staging) dresses it with a random saved item per
    // slot; unsaved items and the preset costumes are ignored. A required slot with nothing saved
    // keeps the model's item and logs a warning; an optional slot with nothing saved stays empty.
    // Picked item colors are saved alongside (CostumeColors).
    // In the editor the selection is saved into Resources so builds ship it; a player build
    // saves its own changes under persistentDataPath.
    public static class FightWardrobe
    {
        public const string None="none";
        [Serializable] class SlotData { public string slot; public string[] pieces; }
        // A picked item color: piece, color slot (0-3 for Color01..04) and sRGB hex.
        [Serializable] class ColorData { public string piece; public int slot; public string color; }
        [Serializable] class Data { public SlotData[] slots; public ColorData[] colors; }

        static string ResourcePath(string code)=>"FightWardrobe/"+code;
        static string ProjectFile(string code)=>Path.Combine(Application.dataPath,"Remaster/Resources/FightWardrobe",code+".json");
        static string PlayerFile(string code)=>Path.Combine(Application.persistentDataPath,"FightWardrobe",code+".json");

        public static string Code(CharacterWardrobe wardrobe)=>!string.IsNullOrEmpty(wardrobe.character.characterCode)?wardrobe.character.characterCode:wardrobe.name.Replace("Wardrobe","").ToLowerInvariant();

        // The wardrobe whose saved items dress this character, or null when it has none.
        public static CharacterWardrobe ForCharacter(MuseumCharacter character)
        {
            if(!character)return null;
#if UNITY_EDITOR
            foreach(string guid in UnityEditor.AssetDatabase.FindAssets("t:CharacterWardrobe"))
            {
                var wardrobe=UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterWardrobe>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if(wardrobe && wardrobe.character==character)return wardrobe;
            }
            return null;
#else
            return Resources.LoadAll<CharacterWardrobe>("Wardrobe").FirstOrDefault(w=>w.character==character);
#endif
        }

        static Data Read(string code)
        {
            string json=null;
            if(Application.isEditor){if(File.Exists(ProjectFile(code)))json=File.ReadAllText(ProjectFile(code));}
            else if(File.Exists(PlayerFile(code)))json=File.ReadAllText(PlayerFile(code));
            else json=Resources.Load<TextAsset>(ResourcePath(code))?.text;
            return string.IsNullOrEmpty(json)?null:JsonUtility.FromJson<Data>(json);
        }

        // Saved colors: piece name -> four optional sRGB colors (null keeps the game default).
        public static Dictionary<string,Color?[]> LoadColors(string code)
        {
            var result=new Dictionary<string,Color?[]>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in Read(code)?.colors??new ColorData[0])
            {
                if(entry.slot<0 || entry.slot>=CostumeColors.Slots || !ColorUtility.TryParseHtmlString("#"+entry.color,out var color))continue;
                if(!result.TryGetValue(entry.piece,out var colors))result[entry.piece]=colors=new Color?[CostumeColors.Slots];
                colors[entry.slot]=color;
            }
            return result;
        }

        public static Dictionary<string,HashSet<string>> Load(string code)
        {
            var result=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var data=Read(code);
            if(data?.slots!=null)foreach(var slot in data.slots)result[slot.slot]=new HashSet<string>(slot.pieces??new string[0],StringComparer.OrdinalIgnoreCase);
            return result;
        }

        public static void Save(string code,Dictionary<string,HashSet<string>> selection,Dictionary<string,Color?[]> colors)
        {
            var data=new Data {slots=selection.OrderBy(p=>p.Key).Select(p=>new SlotData {slot=p.Key,pieces=p.Value.OrderBy(x=>x).ToArray()}).ToArray(),
                colors=colors.OrderBy(p=>p.Key).SelectMany(p=>p.Value.Select((c,s)=>c.HasValue?new ColorData {piece=p.Key,slot=s,color=ColorUtility.ToHtmlStringRGB(c.Value)}:null)).Where(c=>c!=null).ToArray()};
            string file=Application.isEditor?ProjectFile(code):PlayerFile(code);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file,JsonUtility.ToJson(data,true));
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.ImportAsset("Assets/_Project/Resources/FightWardrobe/"+code+".json");
#endif
        }

        // One index per slot: a random saved piece (or None, when saved on an optional slot).
        // An optional slot with nothing saved is left empty; a required one keeps the current piece.
        public static int[] Pick(CharacterWardrobe wardrobe,Dictionary<string,HashSet<string>> selection,int[] current,System.Random random)
        {
            var result=(int[])current.Clone();
            for(int s=0;s<wardrobe.slots.Length;s++)
            {
                var slot=wardrobe.slots[s];
                selection.TryGetValue(slot.name,out var ticked);
                var choices=ticked==null?new List<int>():Enumerable.Range(0,slot.pieces.Length).Where(i=>ticked.Contains(slot.pieces[i].piece)).ToList();
                if(slot.optional && (ticked==null || ticked.Count==0 || ticked.Contains(None)))choices.Add(-1);
                if(choices.Count>0)result[s]=choices[random.Next(choices.Count)];
                else Debug.LogWarning("No saved "+slot.name+" items for "+wardrobe.character.displayName+"; keeping "+(current[s]<0?"none":slot.pieces[current[s]].name)+". Save some in the Wardrobe scene.");
            }
            return result;
        }

        // Dresses a model from its character's saved items; returns "slot: item" lines, or null
        // when the character has no wardrobe.
        public static string[] DressSaved(GameObject model,MuseumCharacter character,System.Random random)
        {
            var wardrobe=ForCharacter(character);
            return wardrobe?Apply(model,wardrobe,Code(wardrobe),random):null;
        }

        // Dresses a model from the saved selection; returns the chosen "slot: item" lines.
        public static string[] Apply(GameObject fighter,CharacterWardrobe wardrobe,string code,System.Random random)
        {
            var selection=Load(code);
            var current=MuseumWardrobe.Find(fighter,wardrobe);
            if(current.Select((index,s)=>index<0 && !wardrobe.slots[s].optional).Any(missing=>missing))
                throw new InvalidOperationException("The fighter model does not wear a known item in every required slot.");
            var chosen=Pick(wardrobe,selection,current,random);
            if(!chosen.SequenceEqual(current))MuseumWardrobe.Dress(fighter,wardrobe,chosen);
            // Saved colors, and game-default tints that the imported textures do not bake.
            CostumeColors.Apply(fighter,wardrobe,LoadColors(code));
            return chosen.Select((index,s)=>wardrobe.slots[s].name+": "+(index<0?"None":wardrobe.slots[s].pieces[index].name)).ToArray();
        }
    }
}
