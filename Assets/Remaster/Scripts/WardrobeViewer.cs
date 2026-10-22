using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Versus.Remaster
{
    // Try-on room for one character: outfit presets plus every wearable item by slot,
    // shown on the character's own stance motion with an orbiting camera and optional
    // cloth and hair physics.
    public sealed class WardrobeViewer : MonoBehaviour
    {
        public CharacterWardrobe[] wardrobes;
        CharacterWardrobe wardrobe;
        int characterIndex;
        GameObject model;
        Transform[] bones;
        Transform head;
        Vector3[] positions;
        Quaternion[,] rotations;
        int frameCount,preset,slotIndex;
        int[] worn;
        bool custom,playing=true,closeUp,physics=true;
        WardrobeDynamics dynamics;
        // Saved items per character code (by slot name), and the codes with unsaved ticks.
        readonly Dictionary<string,Dictionary<string,HashSet<string>>> allPicks=new Dictionary<string,Dictionary<string,HashSet<string>>>();
        readonly HashSet<string> unsaved=new HashSet<string>();
        Dictionary<string,HashSet<string>> fightPicks;
        bool picksChanged=>unsaved.Count>0;
        string Code=>FightWardrobe.Code(wardrobe);
        float frame,yaw=20,pitch=8,distance=3.4f;
        Camera viewCamera;
        Vector2 presetScroll,itemScroll;
        string error="";

        void Start()
        {
            try
            {
                wardrobes=(wardrobes??new CharacterWardrobe[0]).Where(w=>w && w.character && w.slots!=null).ToArray();
                if(wardrobes.Length==0)throw new InvalidOperationException("Assign at least one character wardrobe.");
                CreateRoom();SelectCharacter(0);
            }
            catch(Exception ex){error=ex.Message;Debug.LogException(ex);}
        }

        void CreateRoom()
        {
            RenderSettings.ambientLight=new Color(.45f,.48f,.55f);
            var surface=FighterView.Surface(Color.white);surface.color=new Color(.08f,.11f,.16f);
            var pedestal=GameObject.CreatePrimitive(PrimitiveType.Cylinder);pedestal.name="Wardrobe pedestal";
            pedestal.transform.SetParent(transform);pedestal.transform.position=new Vector3(0,-.1f,0);pedestal.transform.localScale=new Vector3(2.2f,.1f,2.2f);
            pedestal.GetComponent<Renderer>().material=surface;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Wardrobe floor";floor.transform.SetParent(transform);
            floor.transform.position=Vector3.down*.21f;floor.GetComponent<Renderer>().material=surface;
            foreach(int side in new[]{1,-1})
            {
                var light=new GameObject(side==1?"Key light":"Fill light").AddComponent<Light>();light.transform.SetParent(transform);
                light.type=LightType.Directional;light.intensity=side==1?1.3f:.65f;light.transform.rotation=Quaternion.Euler(40,side==1?145:-110,0);
                // Shadows and contact shadows keep light from reaching the eyes and mouth through lids and lips.
                light.shadows=side==1?LightShadows.Soft:LightShadows.None;
                var data=light.gameObject.AddComponent<HDAdditionalLightData>();
                data.SetIntensity(light.intensity*Mathf.PI,LightUnit.Lux);
                data.useContactShadow.useOverride=true;data.useContactShadow.@override=true;
            }
            var profile=ScriptableObject.CreateInstance<VolumeProfile>();
            var contact=profile.Add<ContactShadows>(true);
            contact.enable.value=true;contact.length.value=.04f;contact.thicknessScale.value=.02f;contact.maxDistance.value=20;contact.fadeDistance.value=2;
            profile.Add<HDShadowSettings>(true).maxShadowDistance.value=12;
            profile.Add<MotionBlur>().intensity.Override(0);
            var volume=gameObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=100;volume.sharedProfile=profile;
            viewCamera=new GameObject("Wardrobe camera").AddComponent<Camera>();viewCamera.transform.SetParent(transform);
            viewCamera.clearFlags=CameraClearFlags.SolidColor;viewCamera.backgroundColor=new Color(.025f,.035f,.055f);
            viewCamera.fieldOfView=30;viewCamera.nearClipPlane=.02f;viewCamera.farClipPlane=100;
            viewCamera.rect=new Rect(340f/1280,0,1-340f/1280,1);
        }

        // The character's stance move, looped; the same JRT1 body data the museum plays.
        void LoadStance()
        {
            var character=wardrobe.character;
            var action=character.actions.FirstOrDefault(a=>a.moveId==character.stanceMoveId) ?? character.actions[0];
            byte[] data=action.motion.bytes;
            if(data.Length<16 || System.Text.Encoding.ASCII.GetString(data,0,4)!="JRT1")throw new InvalidOperationException("Invalid stance motion.");
            int frames=BitConverter.ToInt32(data,4),count=BitConverter.ToInt32(data,8);
            if(count!=character.boneNames.Length || data.Length!=16L+frames*(3L+count*4L)*4)throw new InvalidOperationException("Stance does not match the character rig.");
            positions=new Vector3[frames];rotations=new Quaternion[frames,count];
            for(int f=0;f<frames;f++)
            {
                int offset=16+f*(3+count*4)*4;
                positions[f]=new Vector3(BitConverter.ToSingle(data,offset),BitConverter.ToSingle(data,offset+4),BitConverter.ToSingle(data,offset+8));
                for(int c=0;c<count;c++){int p=offset+12+c*16;rotations[f,c]=new Quaternion(BitConverter.ToSingle(data,p),BitConverter.ToSingle(data,p+4),BitConverter.ToSingle(data,p+8),BitConverter.ToSingle(data,p+12));}
            }
            frameCount=frames;
        }

        // Switches the try-on character; ticks made for other characters are kept until saved.
        public void SelectCharacter(int index)
        {
            characterIndex=index;wardrobe=wardrobes[index];slotIndex=0;itemScroll=presetScroll=Vector2.zero;
            if(!allPicks.TryGetValue(Code,out fightPicks))allPicks[Code]=fightPicks=FightWardrobe.Load(Code);
            LoadStance();WearPreset(0);
        }

        public void WearPreset(int index)
        {
            if(index<0 || index>=wardrobe.character.costumes.Length)throw new ArgumentOutOfRangeException(nameof(index));
            preset=index;custom=false;Rebuild();
        }

        public void Wear(int slot,int index)
        {
            var selection=(int[])worn.Clone();selection[slot]=index;
            worn=selection;custom=true;Rebuild();
        }

        public void SetPhysics(bool enabled){physics=enabled;Rebuild();}

        // Every change dresses a fresh instance of the preset, so cloth garments split off a
        // previous piece never linger, then adds cloth and spring dynamics when enabled.
        void Rebuild()
        {
            if(model){model.SetActive(false);Destroy(model);}
            dynamics=null;
            model=Instantiate(wardrobe.character.costumes[preset].prefab,transform);model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.identity;
            foreach(var behaviour in model.GetComponentsInChildren<MonoBehaviour>())behaviour.enabled=false;
            if(custom)MuseumWardrobe.Dress(model,wardrobe,worn);else worn=MuseumWardrobe.Find(model,wardrobe);
            var map=model.GetComponentsInChildren<Transform>().GroupBy(t=>t.name,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
            bones=wardrobe.character.boneNames.Select(n=>map.TryGetValue(n,out var bone)?bone:throw new InvalidOperationException("Missing bone: "+n)).ToArray();
            head=map.TryGetValue("Head",out var h)?h:bones[0];
            Pose();
            if(physics)dynamics=new WardrobeDynamics(model.transform,wardrobe);
        }

        void Pose()
        {
            int a=(int)frame,b=Mathf.Min(a+1,frameCount-1);float t=frame-a;
            var root=Vector3.Lerp(positions[a],positions[b],t);root.x=0;root.y=0;
            bones[0].localPosition=root;
            for(int i=0;i<bones.Length;i++)bones[i].localRotation=Quaternion.Slerp(rotations[a,i],rotations[b,i],t);
        }

        void Update()
        {
            if(error!="" || !model)return;
            if(Input.GetKeyDown(KeyCode.Space))playing=!playing;
            if(playing){frame=Mathf.Repeat(frame+Time.deltaTime*60,frameCount-1);Pose();}
            if(Input.mousePosition.x>Screen.width*340f/1280)
            {
                if(Input.GetMouseButton(1)){yaw+=Input.GetAxis("Mouse X")*4;pitch=Mathf.Clamp(pitch-Input.GetAxis("Mouse Y")*3,-20,65);}
                distance=Mathf.Clamp(distance-Input.mouseScrollDelta.y*.2f,.4f,6);
            }
        }

        void LateUpdate()
        {
            if(!viewCamera || !model)return;
            // Springs and cloth follow the stance pose applied in Update.
            dynamics?.Tick(Time.deltaTime);
            var target=closeUp?head.position+Vector3.up*.06f:new Vector3(0,.95f,0);
            float range=closeUp?Mathf.Min(distance,1.1f)*.45f:distance;
            // Same orbit convention as the museum: the exhibit faces +Z.
            viewCamera.transform.position=target+Quaternion.Euler(-pitch,yaw,0)*new Vector3(0,0,range);
            viewCamera.transform.LookAt(target);
        }

        HashSet<string> Picks(string slot)
        {
            if(!fightPicks.TryGetValue(slot,out var picks))fightPicks[slot]=picks=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return picks;
        }

        // One item: the tick marks it for the fighting scene, the button tries it on.
        void ItemRow(string slot,string piece,string label,Action wear)
        {
            var picks=Picks(slot);bool ticked=picks.Contains(piece);
            GUILayout.BeginHorizontal();
            bool tick=GUILayout.Toggle(ticked,"",GUILayout.Width(18));
            if(tick!=ticked){if(tick)picks.Add(piece);else picks.Remove(piece);unsaved.Add(Code);}
            if(GUILayout.Button(label))wear();
            GUILayout.EndHorizontal();
        }

        void OnGUI()
        {
            GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1280f,Screen.height/720f,1));
            GUI.Box(new Rect(0,0,340,720),"");
            GUI.Label(new Rect(20,16,300,32),"WARDROBE",new GUIStyle(GUI.skin.label){fontSize=20,fontStyle=FontStyle.Bold});
            if(error!=""){GUI.Label(new Rect(20,60,1200,100),error);return;}
            if(!model)return;
            GUILayout.BeginArea(new Rect(20,56,300,650));
            if(wardrobes.Length>1)
            {
                int chosen=GUILayout.Toolbar(characterIndex,wardrobes.Select(w=>w.character.displayName.Split(' ')[0]+(unsaved.Contains(FightWardrobe.Code(w))?" *":"")).ToArray());
                if(chosen!=characterIndex)SelectCharacter(chosen);
            }
            var costumes=wardrobe.character.costumes;
            GUILayout.Label("OUTFITS  /  "+costumes.Length);
            presetScroll=GUILayout.BeginScrollView(presetScroll,GUILayout.Height(150));
            for(int i=0;i<costumes.Length;i++)if(GUILayout.Button((i==preset && !custom?">  ":"")+costumes[i].name))WearPreset(i);
            GUILayout.EndScrollView();
            GUILayout.Space(6);
            GUILayout.Label("ITEMS  /  common items marked *  /  tick = used in fights");
            slotIndex=GUILayout.SelectionGrid(slotIndex,wardrobe.slots.Select(s=>s.name.ToUpperInvariant()+" ("+Picks(s.name).Count+")").ToArray(),3);
            var slot=wardrobe.slots[slotIndex];int current=worn[slotIndex];
            GUILayout.Label("Wearing: "+(current<0?"None":slot.pieces[current].name)+"   ("+slot.pieces.Length+" items)");
            itemScroll=GUILayout.BeginScrollView(itemScroll);
            if(slot.optional)ItemRow(slot.name,FightWardrobe.None,(current<0?">  ":"")+"None",()=>Wear(slotIndex,-1));
            for(int i=0;i<slot.pieces.Length;i++)
            {
                int index=i;
                ItemRow(slot.name,slot.pieces[i].piece,(i==current?">  ":"")+slot.pieces[i].name+(slot.pieces[i].common?"  *":""),()=>{if(index!=current)Wear(slotIndex,index);});
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Saved items: "+fightPicks.Values.Sum(v=>v.Count)+" ticked"+(unsaved.Contains(Code)?"  (unsaved)":"  (saved)"),GUILayout.Width(180));
            GUI.enabled=picksChanged;
            // Saves every character with unsaved ticks, so switching characters never loses work.
            if(GUILayout.Button("Save items")){foreach(string code in unsaved.ToArray())FightWardrobe.Save(code,allPicks[code]);unsaved.Clear();}
            GUI.enabled=true;
            GUILayout.EndHorizontal();
            bool simulate=GUILayout.Toggle(physics,dynamics!=null?"Cloth & hair physics  ("+dynamics.GarmentCount+" cloth, "+dynamics.SpringCount+" springs)":"Cloth & hair physics");
            if(simulate!=physics)SetPhysics(simulate);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(closeUp?"Full body":"Close-up"))closeUp=!closeUp;
            if(GUILayout.Button(playing?"Pause stance":"Play stance"))playing=!playing;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            string outfit=custom?"Custom outfit":costumes[preset].name;
            GUI.Label(new Rect(360,16,900,32),wardrobe.character.displayName+"  /  "+outfit,new GUIStyle(GUI.skin.label){fontSize=22});
            GUI.Label(new Rect(360,684,900,24),"Right-drag to orbit, wheel to zoom, Space to pause the stance.");
        }
    }
}
