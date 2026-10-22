using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    // Wears every wardrobe piece on the character's first outfit in an isolated preview scene,
    // checks each renderer binding, and writes one contact sheet per slot (<code>-<slot>.png).
    [InitializeOnLoad]
    public static class JinWardrobeCapture
    {
        const int Columns=6,Width=320,Height=480;
        static JinWardrobeCapture() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (var character in JinCostumeImporter.Wardrobes)
            {
                string flag = "Temp/capture-" + character.code + "-wardrobe";
                if (!File.Exists(flag)) continue;
                File.Delete(flag);
                try { Run(character); } catch (Exception e) { Debug.LogException(e); File.WriteAllText(flag + "-error.txt", e.ToString()); }
                return;
            }
        }

        [MenuItem("Remaster/Rendering/Capture wardrobes")]
        static void RunAll(){foreach(var character in JinCostumeImporter.Wardrobes)Run(character);}
        public static void Run(JinCostumeImporter.WardrobeCharacter character)
        {
            var wardrobe=AssetDatabase.LoadAssetAtPath<CharacterWardrobe>(character.asset);
            if(!wardrobe || wardrobe.slots==null || !wardrobe.character)throw new InvalidOperationException(character.code+" has no wardrobe; rebuild it first.");
            var basePrefab=wardrobe.character.costumes[0].prefab;
            Directory.CreateDirectory("Validation/Wardrobe");
            var report=new List<string>();var failures=new List<string>();
            for(int s=0;s<wardrobe.slots.Length;s++)
            {
                var slot=wardrobe.slots[s];
                int rows=(slot.pieces.Length+Columns-1)/Columns;
                var sheet=new Texture2D(Columns*Width,rows*Height,TextureFormat.RGB24,false);
                sheet.SetPixels(Enumerable.Repeat(new Color(.1f,.11f,.13f),sheet.width*sheet.height).ToArray());
                for(int i=0;i<slot.pieces.Length;i++)
                {
                    var piece=slot.pieces[i];
                    var preview=new PreviewRenderUtility();
                    try
                    {
                        var model=preview.InstantiatePrefabInScene(basePrefab);
                        // A connected prefab instance refuses renderer removal; dress an unpacked copy.
                        PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                        var selection=MuseumWardrobe.Find(model,wardrobe);selection[s]=i;
                        MuseumWardrobe.Dress(model,wardrobe,selection);
                        var expected=new HashSet<string>{piece.piece};if(piece.dependency)expected.Add(piece.dependency.GetComponentInChildren<SkinnedMeshRenderer>(true).name);
                        var worn=model.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>expected.Contains(r.name)).ToArray();
                        // Build the scene's cloth and spring dynamics too, so setup errors surface here.
                        var dynamics=new WardrobeDynamics(model.transform,wardrobe);
                        bool clothExpected=piece.cloth!=null && piece.cloth.Length>0;
                        string clothProblem=clothExpected && dynamics.GarmentCount==0?"cloth sections were not simulated":"";
                        int nullBones=worn.Sum(r=>r.bones.Count(b=>!b));
                        var bounds=worn.Length>0?worn[0].bounds:new Bounds();foreach(var r in worn)bounds.Encapsulate(r.bounds);
                        string problem=worn.Length!=expected.Count?"expected "+expected.Count+" renderers, found "+worn.Length:nullBones>0?nullBones+" unbound bones":worn.Any(r=>r.sharedMaterials.Any(m=>!m))?"missing material":
                            bounds.size.y<.01f || bounds.size.y>2.6f?"implausible bounds "+bounds.size:"";
                        if(problem=="")problem=clothProblem;
                        if(problem!="")failures.Add(slot.name+"/"+piece.piece+": "+problem);
                        report.Add("{\"slot\":\""+slot.name+"\",\"piece\":\""+piece.piece+"\",\"name\":\""+piece.name+"\",\"common\":"+(piece.common?"true":"false")+
                            ",\"renderers\":"+worn.Length+",\"bones\":"+worn.Sum(r=>r.bones.Length)+",\"cloth\":"+dynamics.GarmentCount+",\"freeClothVertices\":"+dynamics.FreeClothVertices+",\"springs\":"+dynamics.SpringCount+",\"height\":"+bounds.size.y.ToString("0.###")+",\"problem\":\""+problem+"\"}");
                        var image=Render(preview,model,slot.name);
                        sheet.SetPixels((i%Columns)*Width,(rows-1-i/Columns)*Height,Width,Height,image.GetPixels());
                        UnityEngine.Object.DestroyImmediate(image);
                    }
                    catch(Exception e){failures.Add(slot.name+"/"+piece.piece+": "+e.Message);}
                    finally{preview.Cleanup();}
                }
                sheet.Apply();File.WriteAllBytes("Validation/Wardrobe/"+character.code+"-"+slot.name+".png",sheet.EncodeToPNG());UnityEngine.Object.DestroyImmediate(sheet);
            }
            File.WriteAllText("Validation/Wardrobe/"+character.code+"-wardrobe.json","{\"passed\":"+(failures.Count==0?"true":"false")+",\"failures\":["+
                string.Join(",",failures.Select(f=>"\""+f.Replace("\"","'")+"\""))+"],\"pieces\":[\n"+string.Join(",\n",report)+"]}");
            Debug.Log(character.code+" wardrobe capture: "+report.Count+" pieces, "+failures.Count+" failures");
        }

        static Texture2D Render(PreviewRenderUtility preview,GameObject model,string slot)
        {
            var renderers=model.GetComponentsInChildren<SkinnedMeshRenderer>();
            var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            // The face renderer sits forward of the head bone, which gives the facing side.
            var head=model.GetComponentsInChildren<Transform>().First(t=>string.Equals(t.name,"Head",StringComparison.OrdinalIgnoreCase));
            var face=renderers.First(r=>r.sharedMaterials.Any(m=>m && m.name.Contains("_Eyes"))).bounds.center;
            var forward=model.transform.forward;if(Vector3.Dot(face-head.position,forward)<0)forward=-forward;
            bool closeUp=slot=="hair" || slot=="head" || slot=="glasses";
            var target=closeUp?head.position+Vector3.up*.08f:bounds.center;
            float distance=slot=="glasses"?.55f:closeUp?.9f:bounds.size.y*1.75f+.4f;
            var cam=preview.camera;
            cam.transform.position=target+Quaternion.AngleAxis(25,Vector3.up)*forward*distance+Vector3.up*(closeUp?.05f:.15f);
            cam.transform.LookAt(target);cam.fieldOfView=30;cam.nearClipPlane=.02f;cam.farClipPlane=20;
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.18f,.2f,.24f);
            preview.lights[0].intensity=1.2f;preview.lights[0].transform.rotation=Quaternion.LookRotation(-forward+Vector3.down*.6f+Vector3.right*.4f);
            preview.lights[1].intensity=.6f;preview.ambientColor=new Color(.35f,.35f,.35f);
            preview.BeginStaticPreview(new Rect(0,0,Width,Height));preview.Render(true);
            var image=preview.EndStaticPreview();
            if(image.width==Width && image.height==Height)return image;
            // Static previews are scaled by the display's pixels-per-point; resample to the sheet cell.
            var resampled=RenderTexture.GetTemporary(Width,Height,0);var previous=RenderTexture.active;
            Graphics.Blit(image,resampled);RenderTexture.active=resampled;
            var cell=new Texture2D(Width,Height,TextureFormat.RGB24,false);cell.ReadPixels(new Rect(0,0,Width,Height),0,0);cell.Apply();
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(resampled);UnityEngine.Object.DestroyImmediate(image);
            return cell;
        }
    }
}
