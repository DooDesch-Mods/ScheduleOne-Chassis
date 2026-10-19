using MelonLoader;

[assembly: MelonInfo(typeof(Chassis.Core), "Chassis", DooDesch.ModVersion.Current, "DooDesch", "https://github.com/DooDesch-Mods/ScheduleOne-Chassis")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace Chassis
{
    /// <summary>
    /// MelonLoader entry point for Chassis - the vehicle framework: visible occupants with their hands on the wheel,
    /// first-person cabins and new rideables, for vanilla vehicles and for mods that bring their own.
    /// </summary>
    public sealed class Core : MelonMod
    {
        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log { get; private set; }

        private bool _inWorld;

        public override void OnInitializeMelon()
        {
            Instance = this;
            Log = LoggerInstance;

            // No PatchAll here: MelonLoader applies the [HarmonyPatch] classes of a MelonMod itself, and a second
            // PatchAll runs every prefix and postfix twice per call.
            int patched = 0;
            foreach (var _ in HarmonyInstance.GetPatchedMethods()) patched++;
            Log.Msg("[Core] Harmony patches applied by MelonLoader on " + patched + " method(s).");
            Net.ChassisNet.Apply(HarmonyInstance);
            Net.ChassisSync.Init();
            Interior.BaseInterior.LoadSetting();

#if DEBUG
            Log.Msg("Chassis (DEBUG) loaded. Type 'chassis help' in the console.");
#else
            Log.Msg("Chassis loaded.");
#endif
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _inWorld = sceneName == "Main";
            if (!_inWorld) ResetWorld();
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            _inWorld = false;
            ResetWorld();
        }

        private static void ResetWorld()
        {
            Seating.FirstPersonView.Reset();
            Rideables.Push.Reset();
            Seating.Occupants.Reset();
            Rideables.PropSwap.Reset();
            Vehicles.Registry.Reset();
            Vehicles.Fleet.Reset();
            Vehicles.Indicators.Reset();
            Vehicles.Parts.Reset();
            Vehicles.AutoLights.Reset();
            Interior.BaseInterior.Reset();
            Seating.NpcRiders.Reset();
#if DEBUG
            Seating.SitDemo.Reset();
#endif
        }

        public override void OnLateUpdate()
        {
            if (!_inWorld) return;
            Guard("push placement", Rideables.Push.LateTick);
            Guard("parts", Vehicles.Parts.LateTick);
#if DEBUG
            Seating.SitDemo.LateProbe();
            Guard("look cam", Debugging.LookCam.LateTick);
#endif
        }

        public override void OnUpdate()
        {
            if (!_inWorld) return;
            Guard("vehicles", Vehicles.Registry.Tick);
            Guard("interior", Interior.BaseInterior.Tick);
            Guard("npc riders", Seating.NpcRiders.Tick);
            Guard("owners", Vehicles.Fleet.Tick);
            Guard("horn", Vehicles.Horn.Tick);
            Guard("indicators", Vehicles.Indicators.Tick);
            Guard("auto lights", Vehicles.AutoLights.Tick);
            Guard("prop swap", Rideables.PropSwap.Tick);
            Guard("push", Rideables.Push.Tick);
            Guard("view", Seating.FirstPersonView.Tick);
            Guard("sweep", Seating.Occupants.Sweep);
#if DEBUG
            Debugging.ConsoleRegistration.EnsureRegistered();
            Debugging.DebugConsolePatch.FlushToConsole();
#endif
        }

        /// <summary>One failing subsystem logs its error and the others still run this frame.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, float> _lastError = new System.Collections.Generic.Dictionary<string, float>();
        private static readonly System.Collections.Generic.Dictionary<string, int> _muted = new System.Collections.Generic.Dictionary<string, int>();

        private static void Guard(string what, System.Action a)
        {
            try { a(); }
            catch (System.Exception e)
            {
                // At most one log per subsystem per 5 s, so a broken frame loop does not flood the log.
                if (_lastError.TryGetValue(what, out float t) && UnityEngine.Time.time - t < 5f)
                {
                    _muted.TryGetValue(what, out int n); _muted[what] = n + 1;
                    return;
                }
                _lastError[what] = UnityEngine.Time.time;
                _muted.TryGetValue(what, out int muted); _muted[what] = 0;
                Log.Error("[Core] " + what + " failed this frame" + (muted > 0 ? " (and " + muted + " more time(s) in the last 5 s)" : "") + ": " + e);
            }
        }
    }
}
