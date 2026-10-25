using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    [ScriptedImporter(1,"t7motion")]
    public sealed class OriginalMotionImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            byte[] data=File.ReadAllBytes(context.assetPath);
            if(data.Length<12 || BitConverter.ToUInt16(data,0)!=0xc8)throw new InvalidDataException("Expected decoded little-endian 0xC8 motion");
            int channels=BitConverter.ToUInt16(data,2), frames=BitConverter.ToInt32(data,4);
            if(channels<1 || channels>255 || frames<1 || frames>65535 || data.LongLength!=8L+4L*channels+12L*channels*frames)
                throw new InvalidDataException("Invalid motion dimensions");
            for(int offset=8+channels*4;offset<data.Length;offset+=4)
            {
                float value=BitConverter.ToSingle(data,offset);
                if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidDataException("Nonfinite motion sample");
            }
            var asset=ScriptableObject.CreateInstance<OriginalMotionAsset>();
            asset.name=Path.GetFileNameWithoutExtension(context.assetPath);asset.frameCount=frames;asset.channelCount=channels;asset.decodedC8=data;
            context.AddObjectToAsset("motion",asset);context.SetMainObject(asset);
        }
    }

    [CustomEditor(typeof(OriginalMotionAsset))]
    public sealed class OriginalMotionInspector : UnityEditor.Editor
    {
        int frame;
        static readonly string[] Names={"Travel","Root position","Root auxiliary","BASE","Spine1","Hip","Spine auxiliary","Neck","Head","R_Shoulder","R_Arm","R_ForeArm","R_Hand","L_Shoulder","L_Arm","L_ForeArm","L_Hand","R_UpLeg","R_Leg","R_Foot","L_UpLeg","L_Leg","L_Foot"};
        public override void OnInspectorGUI()
        {
            var motion=(OriginalMotionAsset)target;
            EditorGUILayout.HelpBox("Original motion samples decoded successfully. Bone retargeting is not verified; these are source data, not playable Unity AnimationClips.",MessageType.Info);
            EditorGUILayout.LabelField("Frames / channels",motion.frameCount+" / "+motion.channelCount);
            EditorGUILayout.LabelField("Sample rate",motion.frameRate+" fps");
            frame=EditorGUILayout.IntSlider("Frame",frame,0,motion.frameCount-1);
            EditorGUILayout.LabelField("Time",(frame/motion.frameRate).ToString("F3")+" s");
            EditorGUILayout.Space();EditorGUILayout.LabelField("Native channel values",EditorStyles.boldLabel);
            for(int c=0;c<motion.channelCount;c++)
            {
                var sample=motion.GetSample(frame,c);
                EditorGUILayout.LabelField(c+"  "+(c<Names.Length?Names[c]:"Auxiliary"),sample.ToString("F4"));
            }
        }
    }
}
