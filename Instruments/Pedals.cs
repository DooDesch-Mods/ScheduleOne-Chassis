using System.Collections.Generic;
using System.Globalization;
using Chassis.Api;
using Chassis.Net;
using Chassis.Vehicles;

namespace Chassis.Instruments
{
    /// <summary>
    /// Throttle and handbrake, which vanilla keeps on the driver's peer only (<c>currentThrottle</c>,
    /// <c>HandbrakeApplied</c>). The local driver sends them through the host when they change, at most ten times a
    /// second, and every peer keeps the last value per vehicle:
    ///   req.pedal vehicle throttle handbrake -> pedal vehicle throttle handbrake
    /// On the driver's peer, and on the host for a vehicle an NPC drives, the vehicle's own values are read directly.
    /// </summary>
    internal static class Pedals
    {
        private const float SendEvery = 0.1f;
        private const float Quantum = 0.05f;

        private struct State { internal float Throttle; internal bool Handbrake; }

        private static readonly Dictionary<int, State> _synced = new Dictionary<int, State>();

        private static LandVehicle _sentFor;
        private static float _sentThrottle = float.NaN;
        private static bool _sentHandbrake;
        private static float _lastSend;

        private static bool Local(LandVehicle v) =>
            v.LocalPlayerIsDriver || (ChassisNet.IsHost && v.DriverPlayer == null && Sources.HasDriver(v));

        internal static GaugeReading Throttle(LandVehicle v)
        {
            if (Local(v)) return new GaugeReading(v.currentThrottle, true);
            if (v.DriverPlayer == null) return new GaugeReading(0f, !Sources.HasDriver(v));
            return _synced.TryGetValue(Ids.Of(v), out State s) ? new GaugeReading(s.Throttle, true) : GaugeReading.Invalid;
        }

        internal static GaugeReading Handbrake(LandVehicle v)
        {
            if (Local(v)) return new GaugeReading(v.HandbrakeApplied ? 1f : 0f, true);
            if (v.DriverPlayer == null) return new GaugeReading(0f, !Sources.HasDriver(v));
            return _synced.TryGetValue(Ids.Of(v), out State s) ? new GaugeReading(s.Handbrake ? 1f : 0f, true) : GaugeReading.Invalid;
        }

        /// <summary>Local driver: send throttle and handbrake when they changed.</summary>
        internal static void Tick()
        {
            var lp = Player.Local;
            var v = lp != null && lp.CurrentVehicle != null ? lp.CurrentVehicle.GetComponent<LandVehicle>() : null;
            if (v == null || !v.LocalPlayerIsDriver)
            {
                if (_sentFor != null)
                {
                    // Leaving the wheel: the others see the pedals released.
                    Send(_sentFor, 0f, false);
                    _sentFor = null;
                }
                return;
            }
            if (ChassisNet.IsHost && NobodyElse()) return;
            float t = Mathf.Round(v.currentThrottle / Quantum) * Quantum;
            bool h = v.HandbrakeApplied;
            if (v == _sentFor && t == _sentThrottle && h == _sentHandbrake) return;
            if (Time.unscaledTime - _lastSend < SendEvery) return;
            Send(v, t, h);
        }

        /// <summary>A host alone in its world has nobody to tell.</summary>
        private static bool NobodyElse()
        {
            try { return InstanceFinder.ServerManager == null || InstanceFinder.ServerManager.Clients.Count <= 1; }
            catch (System.Exception e) { Core.Log.Warning("[Pedals] could not count the clients, sending anyway: " + e.Message); return false; }
        }

        private static void Send(LandVehicle v, float t, bool h)
        {
            _sentFor = v; _sentThrottle = t; _sentHandbrake = h; _lastSend = Time.unscaledTime;
            ChassisNet.ToHost("req.pedal", Ids.Of(v).ToString(CultureInfo.InvariantCulture), t.ToString("0.##", CultureInfo.InvariantCulture), h ? "1" : "0");
        }

        /// <summary>Everyone: a driver's pedals, passed on by the host.</summary>
        internal static void OnPedal(int vehicle, float throttle, bool handbrake) =>
            _synced[vehicle] = new State { Throttle = Mathf.Clamp(throttle, -1f, 1f), Handbrake = handbrake };

        internal static void Reset()
        {
            _synced.Clear();
            _sentFor = null;
            _sentThrottle = float.NaN;
            _lastSend = 0f;
        }
    }
}
