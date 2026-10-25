using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    public static class JinVersusRetargetSetup
    {
        public static void Build()
        {
            AssetDatabase.Refresh();
            var scene=EditorSceneManager.OpenScene("Assets/_Project/Scenes/LocalVersus.unity");
            var game=UnityEngine.Object.FindFirstObjectByType<VersusGame>();
            game.jinRetargetedStance=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Resources/JinRetargeted/Combat/idle.bytes");
            if(!game.jinRetargetedStance)throw new Exception("Missing runtime stance");
            EditorUtility.SetDirty(game);EditorSceneManager.SaveScene(scene);
            var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/JinPlayable.prefab"));
            try
            {
                var rig=root.GetComponent<JinRig>();rig.Initialize();
                rig.retarget=root.AddComponent<JinRetargetMotion>();rig.retarget.Initialize(game.jinRetargetedStance);
                var fighter=new FighterState {health=100,facing=1};var rules=new MatchRules();float time=0;
                rig.Render(fighter,rules,time,false);
                if(rig.retarget.Weight!=1)throw new Exception("Retarget idle inactive");
                foreach(int facing in new[]{1,-1})
                {
                    fighter.facing=facing;
                    for(int action=0;action<4;action++)
                    {
                        fighter.attack=action;
                        for(int frame=0;frame<45;frame++)
                        {fighter.attackFrame=frame;time+=1f/60;rig.Render(fighter,rules,time,false);}
                        if(rig.retarget.Weight!=1)throw new Exception("Combat animation inactive");
                        fighter.attackFrame=rules.moves[action].startup;time+=1f/60;rig.Render(fighter,rules,time,false);
                        if(Mathf.Abs(rig.retarget.SampleFrame-new[]{10,10,14,13}[action])>.001f)throw new Exception("Strike timing mismatch");
                        fighter.attack=-1;
                        for(int frame=0;frame<15;frame++){time+=1f/60;rig.Render(fighter,rules,time,false);}
                        if(rig.retarget.Weight!=1)throw new Exception("Retarget idle did not resume");
                    }
                }
                foreach(string state in new[]{"moving","guard","crouch","stun","KO"})
                {
                    fighter.guard=state=="guard";fighter.crouch=state=="crouch";
                    fighter.stun=state=="stun"?10:0;fighter.health=state=="KO"?0:100;
                    for(int frame=0;frame<15;frame++){time+=1f/60;rig.Render(fighter,rules,time,state=="moving");}
                    float expected=(state=="moving" || state=="crouch")?1:0;
                    if(rig.retarget.Weight!=expected)throw new Exception("State failed: "+state);
                }
                Directory.CreateDirectory("Validation");
                File.WriteAllText("Validation/versus-retarget.json","{\"passed\":true,\"mappingVersion\":3,\"clips\":8,\"attackTransitions\":8,\"fallbackStates\":3}");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
            ProjectSetup.BuildWindows();
        }
    }
}
