using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    public sealed class OriginalMotionLibrary : EditorWindow
    {
        [Serializable] public class Entry { public string key,name,moveNames,path,format,sha256; public int frames,channels; }
        [Serializable] public class Index { public Entry[] clips; public int moveCount; }
        public const string IndexPath="Assets/_Project/Characters/Jin/Motion/Original/index.json";
        string query="";Vector2 scroll;Index index;
        [MenuItem("Remaster/Original Jin motions")]
        public static void Open()=>GetWindow<OriginalMotionLibrary>("Original Jin motions");
        void OnEnable(){if(File.Exists(IndexPath))index=JsonUtility.FromJson<Index>(File.ReadAllText(IndexPath));}
        void OnGUI()
        {
            if(index==null){EditorGUILayout.HelpBox("Motion source index is missing.",MessageType.Warning);return;}
            EditorGUILayout.LabelField(index.clips.Length+" decoded clips / "+index.moveCount+" Jin move records",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Search original animation or move names. Compatible C8 sources have an experimental Jin 3D preview. Compressed 64 fighting motions still need rotation mapping.",MessageType.Info);
            query=EditorGUILayout.TextField("Search",query);
            var matches=index.clips.Where(c=>(c.key+" "+c.name+" "+c.moveNames).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            EditorGUILayout.LabelField(matches.Length+" matches"+(matches.Length>250?" (first 250 shown; narrow your search)":""));
            scroll=EditorGUILayout.BeginScrollView(scroll);
            foreach(var clip in matches.Take(250))
            {
                EditorGUILayout.BeginHorizontal();
                if(GUILayout.Button("Select",GUILayout.Width(55)))
                {var asset=AssetDatabase.LoadAssetAtPath<OriginalMotionAsset>(clip.path);Selection.activeObject=asset;EditorGUIUtility.PingObject(asset);}
                if(JinRetargetPreview.Available(clip.key) && GUILayout.Button("3D preview",GUILayout.Width(85)))
                    JinRetargetPreview.OpenClip(clip.key);
                EditorGUILayout.LabelField(clip.key+"  "+clip.name+"  ["+clip.frames+"f]",GUILayout.MinWidth(400));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
        [MenuItem("Remaster/Build Jin prototype")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            var index=JsonUtility.FromJson<Index>(File.ReadAllText(IndexPath));
            long samples=0;
            foreach(var clip in index.clips)
            {
                var motion=AssetDatabase.LoadAssetAtPath<OriginalMotionAsset>(clip.path);
                if(!motion || motion.frameCount!=clip.frames || motion.channelCount!=clip.channels)
                    throw new Exception("Motion import validation failed: "+clip.key);
                motion.GetSample(0,0);motion.GetSample(motion.frameCount-1,motion.channelCount-1);
                samples+=(long)motion.frameCount*motion.channelCount;
            }
            Directory.CreateDirectory("Validation");
            File.WriteAllText("Validation/original-motion-import.json","{\"passed\":true,\"clips\":"+index.clips.Length+",\"channelSamples\":"+samples+",\"retargetingVerified\":false}");
            ProjectSetup.BuildWindows();
        }
    }
}
