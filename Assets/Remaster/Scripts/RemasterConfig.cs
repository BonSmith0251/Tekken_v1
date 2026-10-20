using UnityEngine;
namespace Versus.Remaster
{
    [CreateAssetMenu(menuName = "Versus/Match configuration")]
    public class RemasterConfig : ScriptableObject
    {
        public MatchRules rules = new MatchRules();
        public string playerOne = "CRIMSON", playerTwo = "COBALT";
        public Color colorOne = new Color(.95f, .15f, .22f), colorTwo = new Color(.1f, .6f, 1f);
        [TextArea] public string notes = "Original prototype rules. Replace with independently rebuilt move data and retargeted animation.";
    }
}
