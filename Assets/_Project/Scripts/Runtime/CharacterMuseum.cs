using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Versus.Remaster
{
    public sealed class CharacterMuseum : MonoBehaviour
    {
        public MuseumCharacter[] characters;
        public int CharacterIndex {get;private set;}
        public int CostumeIndex {get;private set;}
        public int ActionIndex {get;private set;}
        public int FrameCount {get;private set;}
        public float Frame {get;private set;}
        public bool Playing {get;private set;}=true;
        public void PausePreview()=>Playing=false;
        public void SetOriginalCamera(bool enabled){useOriginalCamera=enabled;SetFrame(Frame);}
        public void RefreshPreviewCamera()=>LateUpdate();
        public XiaoyuSecondaryDynamics FlexibleMotion=>flexibleMotion;
        GameObject model;
        Transform[] bones;
        Vector3[] positions;
        Quaternion[,] rotations;
        Camera cameraRig;
        float speed=1,yaw=25,pitch=12,distance=6.2f;
        bool loop=true,showDetails,sourceTravel;
        int page;
        Vector2 detailScroll;
        Vector2 characterScroll,costumeScroll,actionScroll;
        // Characters with a wardrobe wear their saved items instead of the preset costumes.
        string[] savedOutfit;
        readonly System.Random outfitRandom=new System.Random();
        string search="",error="";
        static readonly string[] CategoryOrder={"All","Staging","Entrance","Ready","Idle","Movement","Fighting","Throws","Reactions","Transitions","Victory","Other"};
        string[] categoryOptions=CategoryOrder;
        int categoryIndex;
        SecondaryMotionTrack secondaryMotion;
        XiaoyuSecondaryDynamics flexibleMotion;
        // Female fighters' breasts, at the flexibility saved in the Wardrobe scene.
        BustDynamics breasts;
        EntranceCameraTrack actionCamera;
        EntranceCameraLens actionLens;
        bool useOriginalCamera;
        Vector3 headFaceAxis=Vector3.forward;
        GameObject galleryFloor,galleryPedestal;
        StagingRenderSettings renderSettings;
        void Start()
        {
            try { CreateGallery();SelectCharacter(0); }
            catch(Exception ex){error=ex.Message;Debug.LogException(ex);}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--museum-smoke")>=0)
            {Application.runInBackground=true;Invoke(nameof(Smoke),2);}
        }
        void CreateGallery()
        {
            // Lights, volume and colors come from the scene's Render Settings object (the staging_test look).
            renderSettings=StagingRenderSettings.FindOrCreate(transform);
            var mat=FighterView.Surface(renderSettings.floor);
            var platform=GameObject.CreatePrimitive(PrimitiveType.Cylinder);platform.name="Exhibit pedestal";
            galleryPedestal=platform;
            platform.transform.SetParent(transform);platform.transform.position=new Vector3(0,-.1f,0);platform.transform.localScale=new Vector3(2.6f,.1f,2.6f);
            platform.GetComponent<Renderer>().material=mat;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.transform.SetParent(transform);floor.transform.position=Vector3.down*.21f;floor.GetComponent<Renderer>().material=mat;
            galleryFloor=floor;
            var clarity=new GameObject("Museum preview clarity").AddComponent<UnityEngine.Rendering.Volume>();clarity.transform.SetParent(transform);
            clarity.isGlobal=true;clarity.priority=100;clarity.sharedProfile=ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            clarity.sharedProfile.Add<UnityEngine.Rendering.HighDefinition.MotionBlur>().intensity.Override(0);
            cameraRig=new GameObject("Museum camera").AddComponent<Camera>();cameraRig.transform.SetParent(transform);
            cameraRig.clearFlags=CameraClearFlags.SolidColor;renderSettings.Apply(cameraRig,galleryFloor.GetComponent<Renderer>());
            cameraRig.fieldOfView=35;cameraRig.nearClipPlane=.05f;cameraRig.farClipPlane=100;
            cameraRig.rect=new Rect(.25f,.19f,.75f,.81f);
            actionLens=new EntranceCameraLens(cameraRig);
        }
        public void SelectCharacter(int index)
        {
            if(characters==null || index<0 || index>=characters.Length)throw new InvalidOperationException("No museum character available.");
            var entry=characters[index];
            if(!entry || (entry.prefab==null && (entry.costumes==null || entry.costumes.Length==0)) || entry.actions==null || entry.actions.Length==0)throw new InvalidOperationException("Character needs a model and its own actions.");
            CharacterIndex=index;CostumeIndex=0;LoadModel(entry.costumes!=null && entry.costumes.Length>0?entry.costumes[0].prefab:entry.prefab);
            search="";page=0;categoryIndex=0;
            categoryOptions=CategoryOrder;
            int stance=Array.FindIndex(entry.actions,a=>a.moveId==entry.stanceMoveId);
            SelectAction(stance>=0?stance:0);
            headFaceAxis=bones[4].InverseTransformDirection(model.transform.forward);
        }
        void LoadModel(GameObject prefab)
        {
            if(!prefab)throw new InvalidOperationException("Costume has no model prefab.");
            secondaryMotion?.Restore();secondaryMotion=null;actionCamera=null;
            if(model){model.SetActive(false);Destroy(model);}
            flexibleMotion=null;breasts=null;
            model=Instantiate(prefab,transform);
            // Give each character a stable exhibit slot. This also keeps models
            // separated during the frame in which Unity retires a previous instance.
            float slot=characters.Length>1?(CharacterIndex-(characters.Length-1)*.5f)*2.7f:0;
            model.transform.localPosition=new Vector3(slot,0,0);model.transform.localRotation=Quaternion.identity;
            if(galleryPedestal)galleryPedestal.transform.localPosition=new Vector3(slot,-.1f,0);
            foreach(var behaviour in model.GetComponentsInChildren<MonoBehaviour>())behaviour.enabled=false;
            var map=new Dictionary<string,Transform>(StringComparer.OrdinalIgnoreCase);foreach(var bone in model.GetComponentsInChildren<Transform>())map[bone.name]=bone;
            var names=characters[CharacterIndex].boneNames;bones=new Transform[names.Length];
            for(int i=0;i<bones.Length;i++)if(!map.TryGetValue(names[i],out bones[i]))throw new InvalidOperationException("Missing bone: "+names[i]);
            // Saved wardrobe items replace the preset's pieces before any cloth/spring setup reads them.
            savedOutfit=FightWardrobe.DressSaved(model,characters[CharacterIndex],outfitRandom);
            if(Application.isPlaying && characters[CharacterIndex].characterCode=="xia")flexibleMotion=new XiaoyuSecondaryDynamics(model.transform);
            if(Application.isPlaying)breasts=BustDynamics.Create(model.transform,characters[CharacterIndex],FightWardrobe.BreastFor(characters[CharacterIndex]));
        }
        public void SelectCostume(int index)
        {
            var entry=characters[CharacterIndex];
            if(entry.costumes==null || index<0 || index>=entry.costumes.Length)throw new InvalidOperationException("Invalid costume selection.");
            float frame=Frame;int action=ActionIndex;LoadModel(entry.costumes[index].prefab);CostumeIndex=index;SelectAction(action);SetFrame(frame);
        }
        // Re-dresses a wardrobe character with another random combination of its saved items.
        public void ShuffleSavedOutfit()
        {
            var entry=characters[CharacterIndex];
            float frame=Frame;int action=ActionIndex;LoadModel(entry.costumes!=null && entry.costumes.Length>0?entry.costumes[0].prefab:entry.prefab);
            SelectAction(action);SetFrame(frame);
        }
        public void SelectAction(int index)
        {
            secondaryMotion?.Restore();secondaryMotion=null;actionCamera=null;
            var entry=characters[CharacterIndex];var action=entry.actions[index];
            if(!action.motion)throw new InvalidDataException("Missing action motion: "+action.name);
            byte[] data=action.motion.bytes;
            if(data.Length<16 || System.Text.Encoding.ASCII.GetString(data,0,4)!="JRT1")throw new InvalidDataException("Invalid action header");
            int frames=BitConverter.ToInt32(data,4),count=BitConverter.ToInt32(data,8),fps=BitConverter.ToInt32(data,12);
            if(frames<1 || count!=bones.Length || fps!=60 || data.Length!=16L+frames*(3L+count*4L)*4)throw new InvalidDataException("Action does not match this character's rig");
            positions=new Vector3[frames];rotations=new Quaternion[frames,count];
            for(int f=0;f<frames;f++)
            {
                int offset=16+f*(3+count*4)*4;
                positions[f]=new Vector3(BitConverter.ToSingle(data,offset),BitConverter.ToSingle(data,offset+4),BitConverter.ToSingle(data,offset+8));
                for(int c=0;c<count;c++)
                {int p=offset+12+c*16;rotations[f,c]=new Quaternion(BitConverter.ToSingle(data,p),BitConverter.ToSingle(data,p+4),BitConverter.ToSingle(data,p+8),BitConverter.ToSingle(data,p+12));}
            }
            ActionIndex=index;FrameCount=frames;detailScroll=Vector2.zero;
            if(!string.IsNullOrEmpty(action.secondaryResource))secondaryMotion=new SecondaryMotionTrack(model.transform,action.secondaryResource,frames);
            if(!string.IsNullOrEmpty(action.cameraResource))actionCamera=new EntranceCameraTrack(action.cameraResource,action.moveId,frames);
            SetFrame(0);
            flexibleMotion?.Reset();breasts?.Reset();
        }
        public void SetFrame(float frame)
        {
            bool wrap=Playing&&loop&&frame<3&&Frame>FrameCount-4;
            bool seek=!wrap&&(frame<Frame || Mathf.Abs(frame-Frame)>3);
            Frame=Mathf.Clamp(frame,0,FrameCount-1);int a=(int)Frame,b=Mathf.Min(a+1,FrameCount-1);float t=Frame-a;
            Vector3 root=Vector3.Lerp(positions[a],positions[b],t);
            if(!sourceTravel && !(useOriginalCamera && actionCamera!=null)){root.x=0;root.y=0;}
            bones[0].localPosition=root;
            for(int i=0;i<bones.Length;i++)bones[i].localRotation=Quaternion.Slerp(rotations[a,i],rotations[b,i],t);
            secondaryMotion?.Apply(Frame);
            if(seek){flexibleMotion?.Reset();breasts?.Reset();}
        }
        void Update()
        {
            if(error!="" || bones==null)return;
            if(Input.GetKeyDown(KeyCode.Space))Playing=!Playing;
            if(Input.GetKeyDown(KeyCode.LeftArrow)){Playing=false;SetFrame(Mathf.Floor(Frame)-1);}
            if(Input.GetKeyDown(KeyCode.RightArrow)){Playing=false;SetFrame(Mathf.Floor(Frame)+1);}
            if(Playing)
            {
                float next=Frame+Time.deltaTime*60*speed;
                if(next>FrameCount-1){if(loop)next=Mathf.Repeat(next,FrameCount);else {next=FrameCount-1;Playing=false;}}
                SetFrame(next);
            }
            if(Input.mousePosition.x>Screen.width*.25f && Input.mousePosition.y>Screen.height*.19f)
            {
                if(Input.GetMouseButton(1)){yaw+=Input.GetAxis("Mouse X")*4;pitch=Mathf.Clamp(pitch-Input.GetAxis("Mouse Y")*3,-20,65);}
                distance=Mathf.Clamp(distance-Input.mouseScrollDelta.y*.25f,2,8);
            }
        }
        void LateUpdate()
        {
            flexibleMotion?.Tick(Time.deltaTime);breasts?.Tick(Time.deltaTime);
            if(!cameraRig)return;
            // Re-applied so color edits on the Render Settings object show live during Play.
            if(renderSettings)renderSettings.Apply(cameraRig,galleryFloor?galleryFloor.GetComponent<Renderer>():null);
            if(useOriginalCamera && actionCamera!=null && model)
            {
                var focus=new Bounds(bones[4].position+Vector3.up*.10f,Vector3.one*.44f);
                foreach(int i in new[]{8,12,15,18})focus.Encapsulate(new Bounds(bones[i].position,Vector3.one*.20f));
                actionCamera.Apply(cameraRig,model.transform,Frame,actionLens,focus,bones[4].TransformDirection(headFaceAxis),null,bones[4].position);
                return;
            }
            cameraRig.fieldOfView=35;
            Vector3 target=new Vector3(0,1,0);
            float framingDistance=distance;
            if(model)
            {
                var renderers=model.GetComponentsInChildren<Renderer>();
                if(renderers.Length>0)
                {
                    var bounds=renderers[0].bounds;for(int i=1;i<renderers.Length;i++)bounds.Encapsulate(renderers[i].bounds);
                    if(bones!=null)foreach(var bone in bones)if(bone)bounds.Encapsulate(bone.position);
                    target=bounds.center;framingDistance=Mathf.Max(distance,bounds.extents.magnitude*2.15f);
                }
            }
            if(!sourceTravel){target.x=model?model.transform.position.x:0;target.z=model?model.transform.position.z:0;}
            cameraRig.transform.position=target+Quaternion.Euler(-pitch,yaw,0)*new Vector3(0,0,framingDistance);
            cameraRig.transform.LookAt(target);
        }
        void OnGUI()
        {
            GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1280f,Screen.height/720f,1));
            GUI.Box(new Rect(0,0,320,720),"");GUI.Box(new Rect(320,582,960,138),"");
            GUI.Label(new Rect(20,18,290,35),"CHARACTER MUSEUM",new GUIStyle(GUI.skin.label){fontSize=22,fontStyle=FontStyle.Bold});
            if(error!=""){GUI.Label(new Rect(20,70,1100,100),error);return;}
            if(bones==null)return;
            GUILayout.BeginArea(new Rect(20,65,280,495));
            GUILayout.Label("CHARACTERS");characterScroll=GUILayout.BeginScrollView(characterScroll,GUILayout.Height(60));
            for(int i=0;i<characters.Length;i++)if(GUILayout.Button((i==CharacterIndex?">  ":"")+characters[i].displayName))SelectCharacter(i);
            GUILayout.EndScrollView();
            var costumes=characters[CharacterIndex].costumes;
            if(savedOutfit!=null)
            {
                GUILayout.Label("SAVED ITEMS  /  from the Wardrobe scene");costumeScroll=GUILayout.BeginScrollView(costumeScroll,GUILayout.Height(95));
                foreach(string item in savedOutfit)GUILayout.Label(item);
                GUILayout.EndScrollView();
                if(GUILayout.Button("Shuffle saved items"))ShuffleSavedOutfit();
            }
            else if(costumes!=null && costumes.Length>0)
            {
                GUILayout.Label("COSTUMES  /  "+costumes.Length);costumeScroll=GUILayout.BeginScrollView(costumeScroll,GUILayout.Height(125));
                for(int i=0;i<costumes.Length;i++)if(GUILayout.Button((i==CostumeIndex?">  ":"")+costumes[i].name))SelectCostume(i);
                GUILayout.EndScrollView();
            }
            GUILayout.Space(8);
            GUILayout.Label("MOVE RECORDS  /  "+characters[CharacterIndex].actions.Length);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("<",GUILayout.Width(32))){categoryIndex=(categoryIndex+categoryOptions.Length-1)%categoryOptions.Length;page=0;actionScroll=Vector2.zero;}
            if(GUILayout.Button("CATEGORY: "+categoryOptions[categoryIndex])){categoryIndex=(categoryIndex+1)%categoryOptions.Length;page=0;actionScroll=Vector2.zero;}
            if(GUILayout.Button(">",GUILayout.Width(32))){categoryIndex=(categoryIndex+1)%categoryOptions.Length;page=0;actionScroll=Vector2.zero;}
            GUILayout.EndHorizontal();
            string query=GUILayout.TextField(search);if(query!=search){search=query;page=0;actionScroll=Vector2.zero;}
            var actions=characters[CharacterIndex].actions;
            string selectedCategory=categoryOptions[categoryIndex];
            var matches=Enumerable.Range(0,actions.Length).Where(i=>(selectedCategory=="All"||actions[i].category==selectedCategory)&&(actions[i].name+" "+actions[i].animation).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            int pages=Mathf.Max(1,(matches.Length+99)/100);page=Mathf.Clamp(page,0,pages-1);
            GUILayout.BeginHorizontal();if(GUILayout.Button("<") && page>0){page--;actionScroll=Vector2.zero;}
            GUILayout.Label((page+1)+" / "+pages+" ("+matches.Length+")");if(GUILayout.Button(">") && page<pages-1){page++;actionScroll=Vector2.zero;}GUILayout.EndHorizontal();
            actionScroll=GUILayout.BeginScrollView(actionScroll);
            foreach(int i in matches.Skip(page*100).Take(100))if(GUILayout.Button((i==ActionIndex?">  ":"")+actions[i].name,GUILayout.Height(29)))SelectAction(i);
            GUILayout.EndScrollView();GUILayout.EndArea();
            string costume=savedOutfit!=null?"Saved items":costumes!=null && costumes.Length>0?costumes[CostumeIndex].name:"Default";
            GUI.Label(new Rect(345,18,850,35),characters[CharacterIndex].displayName+"  /  "+costume+"  /  "+actions[ActionIndex].name,new GUIStyle(GUI.skin.label){fontSize=24});
            if(GUI.Button(new Rect(20,575,280,30),showDetails?"Hide transitions / record":"Transitions / original record"))showDetails=!showDetails;
            bool travel=GUI.Toggle(new Rect(20,615,280,25),sourceTravel,"Source travel (camera follows)");
            if(travel!=sourceTravel){sourceTravel=travel;SetFrame(Frame);}
            GUI.enabled=actionCamera!=null;
            bool nativeCamera=GUI.Toggle(new Rect(20,650,280,25),useOriginalCamera,"Original action camera");
            if(nativeCamera!=useOriginalCamera){useOriginalCamera=nativeCamera;SetFrame(Frame);}
            GUI.enabled=true;
            if(showDetails)
            {
                GUI.Box(new Rect(870,60,395,480),"");GUILayout.BeginArea(new Rect(880,70,375,460));
                detailScroll=GUILayout.BeginScrollView(detailScroll);
                var action=actions[ActionIndex];GUILayout.Label("Category: "+action.category);GUILayout.Label("Animation: "+action.animation);
                if(GUILayout.Button("Default transition -> "+action.transition))JumpTo(action.transition);
                GUILayout.Label("Cancel branches (manual preview)");
                if(action.branches!=null)foreach(var branch in action.branches)
                {bool valid=Array.Exists(actions,a=>a.moveId==branch.target);GUI.enabled=valid;if(GUILayout.Button(branch.label))JumpTo(branch.target);GUI.enabled=true;}
                GUILayout.Label("Original record / conditions");GUILayout.TextArea(action.details??"");
                GUILayout.EndScrollView();GUILayout.EndArea();
            }
            GUI.Label(new Rect(345,553,850,25),"Right-drag to orbit  •  Scroll to zoom");
            if(GUI.Button(new Rect(345,600,100,30),Playing?"Pause":"Play"))Playing=!Playing;
            if(GUI.Button(new Rect(455,600,75,30),"Restart"))SetFrame(0);
            if(GUI.Button(new Rect(540,600,45,30),"<")){Playing=false;SetFrame(Mathf.Floor(Frame)-1);}
            if(GUI.Button(new Rect(595,600,45,30),">")){Playing=false;SetFrame(Mathf.Floor(Frame)+1);}
            loop=GUI.Toggle(new Rect(665,606,70,25),loop,"Loop");
            GUI.Label(new Rect(750,604,95,25),"Speed "+speed.ToString("0.00")+"x");speed=GUI.HorizontalSlider(new Rect(850,612,170,20),speed,.1f,2);
            if(GUI.Button(new Rect(1050,600,190,30),"Reset camera")){yaw=25;pitch=12;distance=4.3f;}
            float selected=GUI.HorizontalSlider(new Rect(345,652,895,25),Frame,0,FrameCount-1);
            if(Mathf.Abs(selected-Frame)>.001f){Playing=false;SetFrame(selected);}
            GUI.Label(new Rect(345,680,890,25),"Frame "+((int)Frame+1)+" / "+FrameCount+"   •   Space play/pause   •   Arrow keys step");
        }
        void JumpTo(int id)
        {int index=Array.FindIndex(characters[CharacterIndex].actions,a=>a.moveId==id);if(index>=0)SelectAction(index);}
        public void Capture(string path,int width,int height)
        {
            var rt=new RenderTexture(width,height,24);cameraRig.targetTexture=rt;
            Rect viewport=cameraRig.rect;cameraRig.rect=new Rect(0,0,1,1);cameraRig.Render();RenderTexture.active=rt;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            File.WriteAllBytes(path,image.EncodeToPNG());cameraRig.targetTexture=null;cameraRig.rect=viewport;RenderTexture.active=null;Destroy(rt);Destroy(image);
        }
        void Smoke()
        {
            try
            {
                int actions=0,frames=0,costumes=0;
                for(int c=0;c<characters.Length;c++)
                {SelectCharacter(c);if(characters[c].costumes!=null)for(int k=0;k<characters[c].costumes.Length;k++){SelectCostume(k);costumes++;SetFrame(0);LateUpdate();Capture(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath,"..")),"character-"+c+"-costume-"+k.ToString("00")+".png"),700,700);SetFrame(FrameCount-1);}for(int a=0;a<characters[c].actions.Length;a++){if(string.IsNullOrEmpty(characters[c].actions[a].category))throw new InvalidDataException("Uncategorized action");SelectAction(a);actions++;for(int f=0;f<FrameCount;f++){SetFrame(f);frames++;}}}
                SelectCharacter(0);JumpTo(1555);SetFrame(10);Playing=false;LateUpdate();
                string dir=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
                File.WriteAllText(Path.Combine(dir,"museum-smoke.json"),"{\"passed\":true,\"characters\":"+characters.Length+",\"costumes\":"+costumes+",\"actions\":"+actions+",\"frames\":"+frames+"}");
                Capture(Path.Combine(dir,"museum-preview.png"),1000,800);Invoke(nameof(Exit),2);
            }
            catch(Exception ex){Debug.LogException(ex);Application.Quit(1);}
        }
        void Exit()=>Application.Quit(0);
    }
}

