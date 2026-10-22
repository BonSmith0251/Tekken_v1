using UnityEngine;
namespace Versus.Remaster
{
    // Every item a character can wear, by slot, plus the outfit presets and motions of its museum exhibit.
    [CreateAssetMenu(menuName="Remaster/Character wardrobe")]
    public sealed class CharacterWardrobe : ScriptableObject
    {
        public MuseumCharacter character;
        public Slot[] slots;
        [System.Serializable] public class Slot
        {
            public string name;
            public bool optional;
            public Piece[] pieces;
        }
        // A single wearable item; its renderer is named after the piece in every prefab.
        [System.Serializable] public class Piece
        {
            public string name,piece;
            public bool common;
            public GameObject prefab;
            // Head items can replace one hair (dependencyBase) with a trimmed variant while worn.
            public string dependencyBase;
            public GameObject dependency;
            // Material sections the game simulates as cloth (APEX) on this piece.
            public string[] cloth;
        }
    }
}
