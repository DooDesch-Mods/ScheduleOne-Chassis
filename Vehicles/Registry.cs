using System;
using System.Collections.Generic;
using Chassis.Api;

namespace Chassis.Vehicles
{
    /// <summary>
    /// The vehicles of the world, read from <c>VehicleManager.AllVehicles</c> once a second, and which of them use a
    /// profile. The per-vehicle subsystems (parts, indicators, lights) walk these lists instead of the manager.
    /// </summary>
    internal static class Registry
    {
        private const float RefreshSeconds = 1f;

        private static readonly List<LandVehicle> _all = new List<LandVehicle>();
        private static readonly List<LandVehicle> _profiled = new List<LandVehicle>();
        private static readonly HashSet<int> _alive = new HashSet<int>();
        private static readonly List<LandVehicle> _unlisted = new List<LandVehicle>();
        private static float _next;
        private static bool _warned;

        internal static List<LandVehicle> All => _all;
        internal static List<LandVehicle> Profiled => _profiled;

        /// <summary>Re-reads the lists on the next frame, after a profile or an assignment changed.</summary>
        internal static void Touch() => _next = 0f;

        internal static void Tick()
        {
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + RefreshSeconds;
            _all.Clear();
            _profiled.Clear();
            if (!NetworkSingleton<VehicleManager>.InstanceExists)
            {
                if (!_warned) Core.Log.Warning("[Vehicles] no vehicle manager yet - horn, indicators and parts wait.");
                _warned = true;
                return;
            }
            _warned = false;
            var list = NetworkSingleton<VehicleManager>.Instance.AllVehicles;
            _alive.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var v = list[i];
                if (v == null) continue;
                _all.Add(v);
                _alive.Add(v.GetInstanceID());
                if (ChassisApi.For(v) != null) _profiled.Add(v);
            }
            _unlisted.Clear();
            ChassisApi.PruneAssignments(_alive, _unlisted);
            foreach (var v in _unlisted)
            {
                _all.Add(v);
                if (ChassisApi.For(v) != null) _profiled.Add(v);
            }
        }

        /// <summary>The vehicle with this FishNet object id, or null.</summary>
        internal static LandVehicle ById(int id)
        {
            if (id < 0) return null;
            foreach (var v in _all) if (v != null && Net.Ids.Of(v) == id) return v;
            // A vehicle spawned within the last second is not in the list yet.
            return Net.Ids.Find<LandVehicle>(id);
        }

        internal static string Name(LandVehicle v) => v == null ? "a vehicle that is gone" : v.vehicleCode + " net " + Net.Ids.Of(v);

        internal static void Reset()
        {
            _all.Clear();
            _profiled.Clear();
            _next = 0f;
            _warned = false;
        }
    }
}
