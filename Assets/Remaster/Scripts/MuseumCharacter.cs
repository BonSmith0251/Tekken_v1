using UnityEngine;
namespace Versus.Remaster
{
    [CreateAssetMenu(menuName="Remaster/Museum character")]
    public sealed class MuseumCharacter : ScriptableObject
    {
        public string displayName;
        public GameObject prefab;
        public Costume[] costumes;
        public string[] boneNames;
        public Action[] actions;
        [System.Serializable] public class Costume
        {
            public string name;
            public GameObject prefab;
        }
        [System.Serializable] public class Action
        {
            public string name;
            public TextAsset motion;
            public int moveId,transition;
            public string animation,details;
            public Branch[] branches;
        }
        [System.Serializable] public class Branch
        {
            public int target;
            public string label;
        }
    }
}
