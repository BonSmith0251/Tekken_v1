using UnityEngine;

namespace Versus.Remaster
{
    /// <summary>Articulated stand-in rig. Replace this view with an Animator-driven character.</summary>
    public class FighterView : IFighterPresentation
    {
        public readonly Transform root;
        readonly Transform torso, head, leftArm, rightArm, leftLeg, rightLeg;
        readonly Material armor, dark, glow;
        readonly Color color;
        public FighterView(string name, Color color, Transform parent)
        {
            this.color = color;
            root = new GameObject(name).transform; root.SetParent(parent);
            armor = Surface(color, .45f); dark = Surface(new Color(.035f, .045f, .065f), .25f);
            glow = Surface(color, .1f); glow.EnableKeyword("_EMISSION"); glow.SetColor("_EmissionColor", color * 1.6f);
            torso = Piece("Chest", PrimitiveType.Cube, root, new Vector3(0, 1.3f, 0), new Vector3(.56f, .62f, .3f), armor);
            Piece("Waist", PrimitiveType.Cube, root, new Vector3(0, .92f, 0), new Vector3(.43f, .22f, .3f), dark);
            Piece("Chest stripe", PrimitiveType.Cube, torso, new Vector3(0, .15f, -.52f), new Vector3(.8f, .12f, .08f), glow);
            head = Piece("Helmet", PrimitiveType.Sphere, root, new Vector3(0, 1.9f, 0), new Vector3(.38f, .43f, .36f), dark);
            Piece("Visor", PrimitiveType.Cube, head, new Vector3(0, .04f, .44f), new Vector3(.8f, .16f, .22f), glow);
            leftArm = Limb("Left arm", new Vector3(-.4f, 1.55f, 0), .63f, .17f);
            rightArm = Limb("Right arm", new Vector3(.4f, 1.55f, 0), .63f, .17f);
            leftLeg = Limb("Left leg", new Vector3(-.2f, .94f, 0), .85f, .23f);
            rightLeg = Limb("Right leg", new Vector3(.2f, .94f, 0), .85f, .23f);
        }
        public static Material Surface(Color c, float metallic = 0)
        {
            // Resource material keeps the shader in standalone builds that create the arena at runtime.
            var template = Resources.Load<Material>("PrototypeSurface");
            var m = template ? new Material(template) : new Material(Shader.Find("Standard")); m.color = c;
            m.SetFloat("_Metallic", metallic); m.SetFloat("_Glossiness", .48f); return m;
        }
        public static Transform Piece(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale;
            var collider = go.GetComponent<Collider>(); if (collider) Object.Destroy(collider);
            go.GetComponent<Renderer>().sharedMaterial = material; return go.transform;
        }
        Transform Limb(string name, Vector3 position, float length, float width)
        {
            var pivot = new GameObject(name).transform; pivot.SetParent(root, false); pivot.localPosition = position;
            Piece("Armor", PrimitiveType.Capsule, pivot, Vector3.down * length * .47f, new Vector3(width, length * .5f, width), armor);
            Piece("Joint", PrimitiveType.Sphere, pivot, Vector3.zero, Vector3.one * width * 1.25f, dark);
            Piece("Glove / boot", PrimitiveType.Cube, pivot, Vector3.down * length, new Vector3(width * 1.3f, .2f, width * 1.7f), dark);
            return pivot;
        }
        public void Render(FighterState f, MatchRules rules, float clock, bool moving)
        {
            root.position = new Vector3(f.x, 0, f.z);
            root.rotation = Quaternion.Euler(0, f.facing == 1 ? 90 : -90, 0);
            float bounce = Mathf.Sin(clock * 5) * .018f;
            torso.localPosition = new Vector3(0, (f.crouch ? 1.05f : 1.3f) + bounce, 0);
            head.localPosition = new Vector3(0, (f.crouch ? 1.62f : 1.9f) + bounce, 0);
            leftArm.localRotation = Quaternion.Euler(-44, 0, -15);
            rightArm.localRotation = Quaternion.Euler(-48, 0, 18);
            float walk = moving && f.attack < 0 && f.stun == 0 ? Mathf.Sin(clock * 14) * 24 : 7;
            leftLeg.localRotation = Quaternion.Euler(walk, 0, -5);
            rightLeg.localRotation = Quaternion.Euler(-walk, 0, 5);
            if (f.guard)
            { leftArm.localRotation = Quaternion.Euler(-140, -20, -12); rightArm.localRotation = Quaternion.Euler(-140, 20, 12); }
            if (f.attack >= 0)
            {
                var move = rules.moves[f.attack];
                float wind = Mathf.Clamp01((float)f.attackFrame / move.startup);
                float recover = Mathf.Clamp01((float)(f.attackFrame - move.startup - move.active) / move.recovery);
                float action = Mathf.Sin(wind * Mathf.PI * .5f) * (1 - recover);
                if (f.attack == 0) leftArm.localRotation = Quaternion.Euler(-44 - action * 56, 0, -15);
                if (f.attack == 1) rightArm.localRotation = Quaternion.Euler(-48 - action * 57, 0, 18);
                if (f.attack == 2) leftLeg.localRotation = Quaternion.Euler(-action * 65, 0, -action * 20);
                if (f.attack == 3) rightLeg.localRotation = Quaternion.Euler(-action * 105, 0, 5);
                torso.localRotation = Quaternion.Euler(0, (f.attack % 2 == 0 ? -1 : 1) * action * 12, 0);
            }
            else torso.localRotation = Quaternion.Euler(f.stun > 0 ? -15 : 0, 0, 0);
            if (f.health == 0) root.rotation *= Quaternion.Euler(-80, 0, 0);
            armor.color = f.flash > 0 ? Color.white : color;
        }
    }
}
