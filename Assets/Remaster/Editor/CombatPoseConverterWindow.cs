using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    public sealed class CombatPoseConverterWindow : EditorWindow
    {
        public static readonly string[] BoneNames={"BASE","Spine1","Hip","Neck","Head",
            "R_Shoulder","R_Arm","R_ForeArm","R_Hand","L_Shoulder","L_Arm","L_ForeArm","L_Hand",
            "R_UpLeg","R_Leg","R_Foot","L_UpLeg","L_Leg","L_Foot"};
        const string ToolFolder="Tools/CombatPoseConverter";
        const string OutputFolder="Assets/Remaster/Editor/ConvertedCombat";
        static string Project=>Path.GetFullPath(".");
        [SerializeField] string python=@"C:\Python311\python.exe";
        [SerializeField] string source=ToolFolder+"/sources/left_punch.t7native";
        [SerializeField] string profile="Assets/Remaster/Editor/CombatPoseLab/calibration-v3.json";
        [SerializeField] string referenceSource=ToolFolder+"/sources/idle.t7native";
        [SerializeField] string targetPose="Assets/Remaster/Editor/RetargetedMotions/common_0462.bytes";
        [SerializeField] int sourceFrame,targetFrame,rootMode;
        [SerializeField] string lastOutput,log="Choose a native 64/C8 motion and a calibration profile.";
        [SerializeField] bool calibration;
        Vector2 scroll;
        Process process;
        readonly StringBuilder pendingLog=new StringBuilder();
        readonly object logLock=new object();
        Action completed;
        string pendingOutput;
        bool failed;

        [MenuItem("Remaster/Combat pose converter")]
        public static void Open()=>GetWindow<CombatPoseConverterWindow>("Combat pose converter");
        void OnEnable()
        {
            if(profile=="Assets/Remaster/Editor/CombatPoseLab/calibration.json")
                profile="Assets/Remaster/Editor/CombatPoseLab/calibration-v3.json";
            EditorApplication.update+=Poll;
        }
        void OnDisable()
        {
            EditorApplication.update-=Poll;
            if(process!=null)
            {
                // The child only writes the explicit output chosen by this window.
                // Stop it when the editor window closes or scripts reload.
                try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}
                process.Dispose();process=null;
            }
        }
        void OnGUI()
        {
            scroll=EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Combat pose converter",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Convert original body motion to Jin's Unity skeleton. Version 3 fixes compressed spherical rotations. Recreate profiles and clips made with older versions; compare results with original gameplay before final use.",MessageType.Info);
            using(new EditorGUI.DisabledScope(process!=null))
            {
                Browse("Python executable",ref python,"exe");
                Browse("Original motion",ref source,"");
                Browse("Calibration profile",ref profile,"json");
                rootMode=EditorGUILayout.Popup("Root movement",rootMode,new[]{"In place","Preserve source translation"});
                if(GUILayout.Button("Convert motion..."))Guard(ConvertSelected);
                calibration=EditorGUILayout.Foldout(calibration,"Create a calibration from a reference pose",true);
                if(calibration)
                {
                    EditorGUILayout.HelpBox("Choose a source frame and a matching target pose. You can pose Jin's bones in the scene, select Jin's root, and capture that pose. Matching one pose does not verify the rest of the animation.",MessageType.Info);
                    Browse("Reference motion",ref referenceSource,"");
                    sourceFrame=Math.Max(0,EditorGUILayout.IntField("Source frame (starts at 0)",sourceFrame));
                    Browse("Target pose / JRT clip",ref targetPose,"bytes");
                    targetFrame=Math.Max(0,EditorGUILayout.IntField("Target frame (starts at 0)",targetFrame));
                    if(GUILayout.Button("Capture selected Jin pose..."))Guard(CaptureSelected);
                    if(GUILayout.Button("Create calibration profile..."))Guard(CreateCalibration);
                }
                if(GUILayout.Button("Open bundled sample previews"))JinRetargetPreview.OpenCombat();
                using(new EditorGUI.DisabledScope(string.IsNullOrEmpty(lastOutput)||!File.Exists(lastOutput)))
                    if(GUILayout.Button("Preview / export last converted clip"))Guard(()=>JinRetargetPreview.OpenConverted(lastOutput));
            }
            if(process!=null)
            {
                EditorGUILayout.HelpBox("Converting...",MessageType.Info);
                if(GUILayout.Button("Cancel conversion"))Guard(()=>
                {
                    if(!process.HasExited)process.Kill();
                    completed=null;pendingOutput=null;
                    log="Conversion cancelled. Any partial output can be replaced with a new conversion.";
                });
            }
            EditorGUILayout.LabelField("Result",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(log,failed?MessageType.Error:MessageType.Info);
            EditorGUILayout.EndScrollView();
        }
        static void Browse(string label,ref string value,string extension)
        {
            EditorGUILayout.BeginHorizontal();
            value=EditorGUILayout.TextField(label,value);
            if(GUILayout.Button("Browse",GUILayout.Width(65)))
            {
                string chosen=EditorUtility.OpenFilePanel(label,Project,extension);
                if(!string.IsNullOrEmpty(chosen))value=chosen;
            }
            EditorGUILayout.EndHorizontal();
        }
        void Guard(Action action)
        {
            try{action();}catch(Exception error){failed=true;log=error.Message;Repaint();}
        }
        void ConvertSelected()
        {
            RequireFile(python);RequireFile(source);RequireFile(profile);
            Directory.CreateDirectory(OutputFolder);
            string path=EditorUtility.SaveFilePanelInProject("Save converted combat clip",
                Path.GetFileNameWithoutExtension(source),"bytes","Choose a new converted clip.",OutputFolder);
            if(string.IsNullOrEmpty(path))return;
            RequireNewOutput(path);pendingOutput=path;
            Run(new[]{"convert","--input",Path.GetFullPath(source),"--profile",Path.GetFullPath(profile),
                "--output",Path.GetFullPath(path),"--root-motion",rootMode==0?"in-place":"source"},
                ()=>{lastOutput=path;JinRetargetPreview.OpenConverted(path);});
        }
        void CaptureSelected()
        {
            var selected=Selection.activeGameObject;
            if(selected==null)throw new InvalidOperationException("Select Jin's root GameObject first.");
            // Validate the selection before opening the save dialog.
            PoseBytes(selected);
            Directory.CreateDirectory(OutputFolder);
            string path=EditorUtility.SaveFilePanelInProject("Save selected Jin pose","JinReferencePose",
                "bytes","Capture the current local bone transforms.",OutputFolder);
            if(string.IsNullOrEmpty(path))return;
            RequireNewOutput(path);
            File.WriteAllBytes(path,PoseBytes(selected));AssetDatabase.Refresh();
            targetPose=path;targetFrame=0;failed=false;log="Captured 19 body bones. Choose the matching source frame, then create a calibration.";
        }
        void CreateCalibration()
        {
            RequireFile(python);RequireFile(referenceSource);RequireFile(targetPose);
            Directory.CreateDirectory(OutputFolder);
            string path=EditorUtility.SaveFilePanelInProject("Save combat calibration","JinCalibration",
                "json","Use a new profile name to preserve existing calibration.",OutputFolder);
            if(string.IsNullOrEmpty(path))return;
            if(File.Exists(path))throw new IOException("Profile already exists. Choose a new name.");
            pendingOutput=path;
            Run(new[]{"calibrate","--source",Path.GetFullPath(referenceSource),"--target",Path.GetFullPath(targetPose),
                "--source-frame",sourceFrame.ToString(),"--target-frame",targetFrame.ToString(),
                "--output",Path.GetFullPath(path)},()=>profile=path);
        }
        static void RequireFile(string path)
        {
            if(!File.Exists(path))throw new FileNotFoundException("File not found: "+path);
        }
        static void RequireNewOutput(string path)
        {
            if(File.Exists(path)||File.Exists(Path.ChangeExtension(path,"conversion.json")))
                throw new IOException("Output or conversion report already exists. Choose a new name.");
        }
        static byte[] PoseBytes(GameObject root)
        {
            var transforms=root.GetComponentsInChildren<Transform>(true);
            var selected=BoneNames.Select(name=>
            {
                var matches=transforms.Where(t=>t.name==name).ToArray();
                if(matches.Length!=1)throw new InvalidDataException("Expected exactly one "+name+" bone under the selected root.");
                return matches[0];
            }).ToArray();
            using(var stream=new MemoryStream())
            using(var writer=new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("JRT1"));writer.Write(1);writer.Write(19);writer.Write(60);
                var p=selected[0].localPosition;WriteFinite(writer,p.x);WriteFinite(writer,p.y);WriteFinite(writer,p.z);
                foreach(var bone in selected)
                {
                    var q=bone.localRotation.normalized;
                    WriteFinite(writer,q.x);WriteFinite(writer,q.y);WriteFinite(writer,q.z);WriteFinite(writer,q.w);
                }
                return stream.ToArray();
            }
        }
        static void WriteFinite(BinaryWriter writer,float value)
        {
            if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidDataException("Pose has a non-finite transform.");
            writer.Write(value);
        }
        // Windows argv quoting, without a shell. Preserve spaces, quotes, and trailing backslashes.
        static string Quote(string value)
        {
            var result=new StringBuilder("\"");int slashes=0;
            foreach(char c in value)
            {
                if(c=='\\'){slashes++;continue;}
                if(c=='"'){result.Append('\\',slashes*2+1);result.Append(c);}
                else{result.Append('\\',slashes);result.Append(c);}
                slashes=0;
            }
            result.Append('\\',slashes*2);result.Append('"');return result.ToString();
        }
        ProcessStartInfo StartInfo(string[] args)=>new ProcessStartInfo
        {
            FileName=Path.GetFullPath(python),
            Arguments=string.Join(" ",new[]{Path.GetFullPath(ToolFolder+"/combat_pose_converter.py")}.Concat(args).Select(Quote)),
            WorkingDirectory=Project,UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardOutput=true,RedirectStandardError=true
        };
        void Run(string[] args,Action onSuccess)
        {
            RequireFile(ToolFolder+"/combat_pose_converter.py");
            lock(logLock)pendingLog.Clear();
            failed=false;log="Converting...";completed=onSuccess;
            process=new Process {StartInfo=StartInfo(args)};
            process.OutputDataReceived+=Receive;process.ErrorDataReceived+=Receive;
            try
            {
                process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
            }
            catch{process.Dispose();process=null;completed=null;throw;}
        }
        void Receive(object sender,DataReceivedEventArgs args)
        {
            if(args.Data!=null)lock(logLock)pendingLog.AppendLine(args.Data);
        }
        void Poll()
        {
            if(process==null||!process.HasExited)return;
            process.WaitForExit();int code=process.ExitCode;process.Dispose();process=null;
            lock(logLock)log=pendingLog.ToString();
            failed=code!=0;
            if(failed)log="Conversion failed (exit "+code+").\n"+log;
            else log="Conversion completed. Original-game fidelity remains unverified.\n"+log;
            var callback=completed;completed=null;
            AssetDatabase.Refresh();
            if(!failed && callback!=null)Guard(callback);
            pendingOutput=null;Repaint();
        }
        public static void Validate()
        {
            Directory.CreateDirectory("Validation/CombatConverterWindow");
            string folder="Validation/CombatConverterWindow/"+Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(folder);
            var window=CreateInstance<CombatPoseConverterWindow>();
            GameObject jin=null;
            try
            {
                jin=Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remaster/Resources/JinPlayable.prefab"));
                string pose=folder+"/reference pose.bytes",profile=folder+"/calibration profile.json",output=folder+"/converted clip.bytes";
                File.WriteAllBytes(pose,PoseBytes(jin));
                RunCheck(window,new[]{"calibrate","--source",window.referenceSource,"--target",pose,"--output",profile},true);
                RunCheck(window,new[]{"convert","--input",window.referenceSource,"--profile",profile,"--output",output},true);
                var actual=File.ReadAllBytes(output);var expected=File.ReadAllBytes(pose);
                for(int offset=16;offset<expected.Length;offset+=4)
                    if(Math.Abs(BitConverter.ToSingle(actual,offset)-BitConverter.ToSingle(expected,offset))>1e-5)
                        throw new Exception("Captured calibration pose did not round-trip.");
                JinRetargetPreview.ValidateConverted(output);
                RunCheck(window,new[]{"convert","--input",window.referenceSource,"--profile",profile,"--output",output},false);
                RunCheck(window,new[]{"convert","--input","missing motion.anim","--profile",profile,"--output",folder+"/missing.bytes"},false);
                File.WriteAllText("Validation/combat-converter-window.json",
                    "{\"passed\":true,\"poseCaptureRoundTrip\":true,\"customPreviewExport\":true,\"existingOutputProtected\":true,\"missingInputRejected\":true,\"originalGameVerified\":false}");
            }
            finally{if(jin!=null)DestroyImmediate(jin);DestroyImmediate(window);}
        }
        static void RunCheck(CombatPoseConverterWindow window,string[] args,bool success)
        {
            using(var p=new Process {StartInfo=window.StartInfo(args)})
            {
                p.Start();
                var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
                if(!p.WaitForExit(60000)){p.Kill();throw new TimeoutException("Converter check timed out");}
                System.Threading.Tasks.Task.WaitAll(stdout,stderr);
                if((p.ExitCode==0)!=success)throw new Exception("Converter check failed: "+stdout.Result+stderr.Result);
            }
        }
    }
}
