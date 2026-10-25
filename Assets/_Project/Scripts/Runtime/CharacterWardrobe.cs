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
            // Customizable materials (Color01..04 tinted through a mask), for recoloring.
            public Tint[] tints;
        }
        [System.Serializable] public class Tint
        {
            public string material;
            public Texture2D baseMap,maskMap;
            // Game defaults, linear; slots the mask actually uses.
            public Color[] colors;
            public bool[] slots;
            // Whether the imported albedo already bakes the default colors.
            public bool baked;
            // Hair: no mask, one color (Color01) over the whole texture.
            public bool hair;
        }
    }
}
