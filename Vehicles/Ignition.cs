using System;
using System.Collections.Generic;
using Chassis.Api;
using Chassis.Instruments;
using HarmonyLib;

namespace Chassis.Vehicles
{
    /// <summary>
    /// The ignition lock and the auto ignition button of a cockpit. Both are switches of the profile, so the host checks
    /// that the player sits in the car, every peer stores the state and the host saves it with the car.
    ///
    /// With auto ignition on (the default) the engine runs as in vanilla: on when someone takes the driver seat, off when
    /// the seat is empty. Turning the key switches auto ignition off; from then on the key alone says whether the engine
    /// runs while someone drives. A car without these switches in its profile always runs as in vanilla.
    ///
    /// Vanilla has no engine state of its own: the driver seat starts the engine sound and the exhaust, the throttle
    /// works for whoever drives. An engine that is off here skips the start sound and the exhaust, fades the idle and
    /// loop sounds out and gives the drive wheels no throttle; braking, steering, lights and indicators stay.
    /// </summary>
    internal static class Ignition
    {
        internal const string ControlName = "ignition", AutoName = "auto_ignition";

        /// <summary>The engine state each peer last applied, by vehicle instance id; missing means as vanilla.</summary>
        private static readonly Dictionary<int, bool> _applied = new Dictionary<int, bool>();
        private static readonly HashSet<int> _blockLogged = new HashSet<int>();

        /// <summary>True when the car has the ignition switches in its profile.</summary>
        internal static bool Has(LandVehicle lv)
        {
            var p = lv != null ? ChassisApi.For(lv) : null;
            return p != null && p.Control(ControlName) != null && p.Control(AutoName) != null;
        }

        /// <summary>Auto ignition: on unless the stored button says off.</summary>
        internal static bool Auto(LandVehicle lv)
        {
            if (!Has(lv)) return true;
            var cab = Store.Of(lv);
            return cab == null || !cab.Controls.TryGetValue(AutoName, out float v) || v > 0.5f;
        }

        /// <summary>The key's stored position: on or off. Only read while auto ignition is off.</summary>
        private static bool KeyOn(LandVehicle lv)
        {
            var cab = Store.Of(lv);
            return cab != null && cab.Controls.TryGetValue(ControlName, out float v) && v > 0.5f;
        }

        /// <summary>The engine may run: auto ignition on, or the key turned on.</summary>
        internal static bool Allowed(LandVehicle lv) => lv == null || Auto(lv) || KeyOn(lv);

        /// <summary>The engine runs: allowed and someone at the wheel.</summary>
        internal static bool Running(LandVehicle lv) => lv != null && Sources.HasDriver(lv) && Allowed(lv);

        /// <summary>What the key shows: the engine with auto ignition on, the key's own position with it off.</summary>
        internal static float KeyShown(LandVehicle lv) => Auto(lv) ? (Sources.HasDriver(lv) ? 1f : 0f) : (KeyOn(lv) ? 1f : 0f);

        /// <summary>Host, after the key's own use went out: auto ignition goes off with it, for everyone.</summary>
        internal static void HostKey(LandVehicle lv, string id, float value)
        {
            if (!Auto(lv)) return;
            Core.Log.Msg("[Ignition] key turned in " + Registry.Name(lv) + " - auto ignition off.");
            Net.ChassisNet.ToAll("ctl", id, AutoName, "0", "-1");
        }

        /// <summary>Everyone: a switch of the ignition changed. Starts or stops the engine to match.</summary>
        internal static void Changed(LandVehicle lv, string why)
        {
            if (lv == null) return;
            bool run = Running(lv);
            int key = lv.GetInstanceID();
            bool was = _applied.TryGetValue(key, out bool a) ? a : Sources.HasDriver(lv);
            _applied[key] = run;
            Core.Log.Msg("[Ignition] " + Registry.Name(lv) + ": " + why + " - engine " + (run ? "on" : "off") + ", auto ignition " + (Auto(lv) ? "on" : "off") + ".");
            if (run == was) return;
            if (run) Start(lv);
            else Stop(lv);
        }

        private static void Start(LandVehicle lv)
        {
            _blockLogged.Remove(lv.GetInstanceID());
            var snd = lv.GetComponentInChildren<GameVehicleSound>(true);
            if (snd == null) Core.Log.Msg("[Ignition] " + Registry.Name(lv) + " has no engine sound to start.");
            else
            {
                try
                {
#if IL2CPP
                    snd.EngineStart();
#else
                    AccessTools.Method(typeof(GameVehicleSound), "EngineStart").Invoke(snd, null);
#endif
                }
                catch (Exception e) { Core.Log.Error("[Ignition] the engine sound of " + Registry.Name(lv) + " could not start: " + e.Message); }
            }
            foreach (var fx in lv.GetComponentsInChildren<GameVehicleFX>(true))
            {
                try { fx.OnVehicleStart(); }
                catch (Exception e) { Core.Log.Error("[Ignition] the exhaust of " + Registry.Name(lv) + " could not start: " + e.Message); }
            }
        }

        private static void Stop(LandVehicle lv)
        {
            // The idle and loop sounds fade out through SoundPatch; the exhaust stops here.
            foreach (var fx in lv.GetComponentsInChildren<GameVehicleFX>(true))
            {
                try { fx.OnVehicleStop(); }
                catch (Exception e) { Core.Log.Error("[Ignition] the exhaust of " + Registry.Name(lv) + " could not stop: " + e.Message); }
            }
        }

        /// <summary>The local driver pressed the ignition key: turns the key as a click on the lock would.</summary>
        internal static void KeyPressed(LandVehicle lv)
        {
            if (lv == null) { Core.Log.Msg("[Ignition] ignition key on foot - nothing to switch."); return; }
            if (!Has(lv)) { Core.Log.Msg("[Ignition] " + Registry.Name(lv) + " has no ignition lock in its profile - the key does nothing."); return; }
            Controls.UseLocal(lv, ControlName, KeyShown(lv) > 0.5f ? 0f : 1f);
        }

        /// <summary>The ignition key binding, for whoever sits in the car; the host checks the seat.</summary>
        internal static void Tick()
        {
            if (!Bindings.Ignition.Down() || GameInput.IsTyping) return;
            var lp = Player.Local;
            var lv = lp != null && lp.CurrentVehicle != null ? lp.CurrentVehicle.GetComponent<LandVehicle>() : null;
            if (lv == null) { Core.Log.Msg("[Ignition] ignition key on foot - nothing to switch."); return; }
            if (lv.State != null && lv.LocalPlayerIsDriver && !lv.State.IsAcceptingInput) { Core.Log.Msg("[Ignition] ignition key in " + Registry.Name(lv) + " ignored: the vehicle takes no input."); return; }
            KeyPressed(lv);
        }

        internal static void Forget(LandVehicle lv)
        {
            if (lv == null) return;
            _applied.Remove(lv.GetInstanceID());
            _blockLogged.Remove(lv.GetInstanceID());
        }

        internal static void Reset()
        {
            _applied.Clear();
            _blockLogged.Clear();
        }

        private static void LogBlocked(LandVehicle lv, string what)
        {
            if (_blockLogged.Add(lv.GetInstanceID()))
                Core.Log.Msg("[Ignition] " + Registry.Name(lv) + ": engine off, " + what + " (logged once until the engine runs).");
        }

        // ---- patches -----------------------------------------------------------------------------------------------

        /// <summary>The driver seat starts the engine sound; an engine that is off stays quiet.</summary>
        [HarmonyPatch(typeof(GameVehicleSound), "EngineStart")]
        private static class StartSoundPatch
        {
            private static bool Prefix(GameVehicleSound __instance)
            {
                try
                {
                    var lv = __instance.Vehicle;
                    if (lv == null) return true;
                    if (Allowed(lv)) { _applied[lv.GetInstanceID()] = true; return true; }
                    _applied[lv.GetInstanceID()] = false;
                    LogBlocked(lv, "no start sound");
                    return false;
                }
                catch (Exception e) { Core.Log.Error("[Ignition] checking the engine start failed, the engine starts: " + e.Message); return true; }
            }
        }

        /// <summary>Idle and loop sounds fade out while the engine is off, as when the driver gets out.</summary>
        [HarmonyPatch(typeof(GameVehicleSound), "UpdateIdle")]
        private static class IdlePatch
        {
            private static void Prefix(GameVehicleSound __instance, ref bool engineRunning)
            {
                if (engineRunning && __instance.Vehicle != null && !Allowed(__instance.Vehicle)) engineRunning = false;
            }
        }

        [HarmonyPatch(typeof(GameVehicleSound), "UpdateEngineLoop")]
        private static class LoopPatch
        {
            private static void Prefix(GameVehicleSound __instance, ref bool engineRunning)
            {
                if (engineRunning && __instance.Vehicle != null && !Allowed(__instance.Vehicle)) engineRunning = false;
            }
        }

        /// <summary>No exhaust from an engine that is off.</summary>
        [HarmonyPatch(typeof(GameVehicleFX), nameof(GameVehicleFX.OnVehicleStart))]
        private static class ExhaustPatch
        {
            private static bool Prefix(GameVehicleFX __instance)
            {
                try
                {
                    var lv = __instance.GetComponentInParent<LandVehicle>();
                    return lv == null || Allowed(lv);
                }
                catch (Exception e) { Core.Log.Error("[Ignition] checking the exhaust failed, it starts: " + e.Message); return true; }
            }
        }

        /// <summary>
        /// The local driver's throttle, after the keys were read: none from an engine that is off. A pedal against the
        /// direction of travel brakes in vanilla, and still does. Called from the throttle postfix in DriveInput, before
        /// a rim-driven car takes the throttle.
        /// </summary>
        internal static void LimitThrottle(LandVehicle lv)
        {
            if (lv.currentThrottle == 0f || Allowed(lv)) return;
            float speed = lv.Speed_Kmh;
            bool brakes = Mathf.Abs(speed) >= 4f && Mathf.Sign(speed) != Mathf.Sign(lv.currentThrottle);
            if (brakes) return;
            lv.currentThrottle = 0f;
            LogBlocked(lv, "no throttle");
        }

        internal static string Describe(LandVehicle lv)
        {
            if (lv == null) return "no vehicle";
            if (!Has(lv)) return Registry.Name(lv) + ": no ignition lock in the profile - engine as vanilla";
            return Registry.Name(lv) + ": auto ignition " + (Auto(lv) ? "on" : "off") + ", key " + (KeyOn(lv) ? "on" : "off")
                + ", key shows " + KeyShown(lv).ToString("0") + ", driver " + Sources.HasDriver(lv) + ", engine " + (Running(lv) ? "on" : "off")
                + ", rpm " + Sources.Read(lv, "rpm").Value.ToString("0") + ", speed " + lv.Speed_Kmh.ToString("0.0") + " km/h";
        }
    }
}
