using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace Versus.Remaster.Editor
{
    [InitializeOnLoad]
    public static class StagingFlowValidation
    {
        static int phase,frames;
        static int jin,kazuya;
        static StagingFlowValidation(){EditorApplication.update+=Poll;}
        static object Private(object value,string field)=>value.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(value);
        static void Poll()
        {
            if(File.Exists("Temp/validate-staging-flow")&&!EditorApplication.isCompiling&&!EditorApplication.isPlayingOrWillChangePlaymode)
            {File.Delete("Temp/validate-staging-flow");phase=0;SessionState.SetBool("StagingFlowValidation",true);EditorApplication.isPlaying=true;}
            if(!SessionState.GetBool("StagingFlowValidation",false)||!EditorApplication.isPlaying)return;
            Application.runInBackground=true;EditorApplication.QueuePlayerLoopUpdate();
            if(Time.time<.1f)return;
            Directory.CreateDirectory("Validation/Staging");
            try
            {
                var game=UnityEngine.Object.FindFirstObjectByType<VersusGame>();game.automaticFight=false;
                var entrance=(LocalVersusEntrance)Private(game,"entrance");
                if(phase==0)
                {
                    if(entrance==null)throw new Exception("Enable entrances for the flow check.");
                    jin=entrance.JinStagingMove;kazuya=entrance.KazuyaStagingMove;
                    if(entrance.Active)return;
                    Win(game,0);phase=1;frames=Time.frameCount;return;
                }
                var victory=(VictoryStaging)Private(game,"victory");
                if(phase==1||phase==2)
                {
                    if(Time.frameCount<frames+3)return;
                    if(victory==null||!victory.Active)throw new Exception("Victory staging did not start.");
                    if(!victory.Complete)return;
                    typeof(VersusGame).GetMethod("StartMatch",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(game,null);
                    if(Private(game,"victory")!=null)throw new Exception("Victory not cleared on rematch.");
                    if(phase==1){game.battle.BeginFight();Win(game,1);phase=2;frames=Time.frameCount;return;}
                    File.WriteAllText("Validation/Staging/flow.json","{\"passed\":true,\"jinEntrance\":"+jin+",\"kazuyaEntrance\":"+kazuya+",\"bothWinnerSequencesCompleted\":true,\"rematchClearedStaging\":true}");
                    SessionState.SetBool("StagingFlowValidation",false);EditorApplication.isPlaying=false;
                }
            }
            catch(Exception e){File.WriteAllText("Validation/Staging/flow-error.txt",e.ToString());SessionState.SetBool("StagingFlowValidation",false);EditorApplication.isPlaying=false;Debug.LogException(e);}
        }
        static void Win(VersusGame game,int winner)
        {
            game.battle.fighters[winner].wins=game.config.rules.roundsToWin-1;
            game.battle.fighters[1-winner].health=0;
            game.battle.Tick(PlayerCommand.Neutral,PlayerCommand.Neutral);
            if(game.battle.phase!=MatchPhase.MatchOver||game.battle.winner!=winner)throw new Exception("Could not prepare winner.");
        }
    }
}
