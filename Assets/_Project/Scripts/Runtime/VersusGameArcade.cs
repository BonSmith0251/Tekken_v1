using System;
using UnityEngine;
using Versus.Remaster.Arcade;

namespace Versus.Remaster
{
    public partial class VersusGame
    {
        static ArcadeMatchRequest pendingArcade;
        public static VersusGame ArcadeCurrent { get; private set; }
        ArcadeMatchRequest arcadeRequest;
        ArcadeMatchController arcadeController;
        bool ownsArcadeConfig;
        public bool ArcadeActive => arcadeRequest != null;
        public ArcadeMatchRequest ArcadeRequest => arcadeRequest;
        public long ArcadeFightTicks => arcadeController?.FightTicks ?? 0;
        public ArcadeMatchResult ArcadeResult => arcadeController?.Result;
        public bool ArcadeVictoryComplete => ArcadeResult != null &&
            (ArcadeResult.Winner < 0 || victory != null && victory.Complete);
        public event Action<ArcadeMatchResult> ArcadeMatchCompleted;
        // Set by the arcade director while a full-screen arcade page covers the match.
        public bool ArcadeCallsHidden { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetArcadeRequests() { pendingArcade = null; ArcadeCurrent = null; }

        // Queue immediately before loading this exact scene. A timeout/load failure must cancel it;
        // a different scene cannot consume it. The request reports initialization failure/readiness.
        public static ArcadeMatchRequest PrepareArcadeMatch(ArcadeMatchSetup setup, ArcadeInput input,
            string scenePath = "Assets/_Project/Scenes/LocalVersus2.unity")
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            ParseArcadeFighter(setup.PlayerOne); ParseArcadeFighter(setup.PlayerTwo);
            if (pendingArcade != null && pendingArcade.State == ArcadeRequestState.Pending)
                throw new InvalidOperationException("An arcade match is already queued. Cancel it before replacing it.");
            return pendingArcade = new ArcadeMatchRequest(setup, input, scenePath);
        }

        public static bool CancelArcadeMatch(ArcadeMatchRequest request)
        {
            if (request == null || !ReferenceEquals(request, pendingArcade) || !request.Cancel()) return false;
            pendingArcade = null; return true;
        }

        static Fighter ParseArcadeFighter(string name)
        {
            if (!Enum.TryParse(name, true, out Fighter fighter) || !Enum.IsDefined(typeof(Fighter), fighter))
                throw new ArgumentException("Unknown arcade fighter: " + name);
            return fighter;
        }

        void Awake()
        {
            try
            {
                if (pendingArcade != null && pendingArcade.TryClaim(gameObject.scene.path))
                {
                    arcadeRequest = pendingArcade; pendingArcade = null; ArcadeCurrent = this;
                    playerOneFighter = ParseArcadeFighter(arcadeRequest.Setup.PlayerOne);
                    playerTwoFighter = ParseArcadeFighter(arcadeRequest.Setup.PlayerTwo);
                    characterSelect = false; playEntrance = arcadeRequest.Setup.PlayEntrance;
                    automaticFight = false; effectTuningPanel = effectDemosOnly = menu = help = paused = false;
                    // Presentation writes names/rules into config; keep those changes on an owned copy.
                    config = config ? Instantiate(config) : ScriptableObject.CreateInstance<RemasterConfig>();
                    ownsArcadeConfig = true;
                }
                InitializeMatch();
                if (ArcadeActive) arcadeRequest.MarkReady();
            }
            catch (Exception error)
            {
                if (!ArcadeActive) throw;
                arcadeRequest.Fail(error); enabled = false; Time.timeScale = 1;
                Debug.LogException(error, this);
            }
        }

        public void SuspendArcadeMatch(bool suspended)
        {
            if (arcadeController == null) throw new InvalidOperationException("Arcade match is not initialized.");
            arcadeController.SetSuspended(suspended);
            if (suspended) Time.timeScale = 1;
        }

        void TickArcadeMatch()
        {
            CapturePrevious();
            arcadeController.Tick(battle, out commands[0], out commands[1]);
            AfterTick();
            // The callback runs after presentation has observed the terminal tick. Subscribers should
            // schedule scene changes for their next update, then wait for victory or their own timeout.
            if (arcadeController.TryTakeCompletion(out var result)) ArcadeMatchCompleted?.Invoke(result);
        }

        void OnDestroy()
        {
            arcadeRequest?.Release();
            if (ArcadeCurrent == this) ArcadeCurrent = null;
            if (ownsArcadeConfig && config) Destroy(config);
        }
    }
}
