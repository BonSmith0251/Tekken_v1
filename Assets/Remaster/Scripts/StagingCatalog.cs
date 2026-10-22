using System;
using System.Linq;
using UnityEngine;
namespace Versus.Remaster
{
    [Serializable]
    public sealed class StagingAction
    {
        public string character, animation, kind, camera, secondary;
        public int moveId, frames;
        public bool landingStitch;
    }
    [Serializable]
    public sealed class StagingCatalog
    {
        public StagingAction[] entries;
        static StagingCatalog cached;
        public static StagingCatalog Load()
        {
            if(cached!=null)return cached;
            var asset=Resources.Load<TextAsset>("StagingCatalog");
            if(!asset)throw new InvalidOperationException("Missing staging catalog.");
            return cached=JsonUtility.FromJson<StagingCatalog>(asset.text);
        }
        public static StagingAction Select(string character, MuseumCharacter actor, string kind = "entrance")
        {
            var pool=Load().entries.Where(e=>e.character==character && e.kind==kind &&
                Array.Exists(actor.actions,a=>a.moveId==e.moveId && a.motion)).ToArray();
            if(pool.Length==0)throw new InvalidOperationException("No staging actions for "+character);
            string key="Remaster.LastStaging."+character+"."+kind;
            int previous=PlayerPrefs.GetInt(key,-1);
            var choices=pool.Where(e=>pool.Length==1 || e.moveId!=previous).ToArray();
            var chosen=choices[new System.Random(Guid.NewGuid().GetHashCode()).Next(choices.Length)];
            PlayerPrefs.SetInt(key,chosen.moveId);PlayerPrefs.Save();
            Debug.Log("Staging selected: "+character+" "+chosen.animation+" ("+chosen.moveId+")");
            return chosen;
        }
    }
}
