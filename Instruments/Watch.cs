using System.Collections.Generic;
using Chassis.Api;
using Chassis.Vehicles;

namespace Chassis.Instruments
{
    /// <summary>
    /// Raises the driving events of <see cref="ChassisApi"/> on every peer: headlights, brakes, reverse and driver,
    /// compared with the last frame. Only runs while a mod listens.
    /// </summary>
    internal static class Watch
    {
        private const int Lights = 1, Brake = 2, Reverse = 4, Driver = 8;

        private static readonly Dictionary<int, int> _last = new Dictionary<int, int>();
        private static readonly List<int> _gone = new List<int>();
        private static readonly HashSet<int> _seen = new HashSet<int>();

        internal static void Tick()
        {
            if (!ChassisApi.HasDrivingWatchers) { if (_last.Count > 0) _last.Clear(); return; }
            _seen.Clear();
            foreach (var v in Registry.All)
            {
                if (v == null) continue;
                int key = v.GetInstanceID();
                _seen.Add(key);
                int now = (v.lights != null && v.lights.HeadlightsOn ? Lights : 0) | (v.BrakesApplied ? Brake : 0) | (v.IsReversing ? Reverse : 0) | (Sources.HasDriver(v) ? Driver : 0);
                if (!_last.TryGetValue(key, out int was)) { _last[key] = now; continue; }
                if (was == now) continue;
                _last[key] = now;
                int changed = was ^ now;
                if ((changed & Lights) != 0) ChassisApi.RaiseWatch(0, v, (now & Lights) != 0);
                if ((changed & Brake) != 0) ChassisApi.RaiseWatch(1, v, (now & Brake) != 0);
                if ((changed & Reverse) != 0) ChassisApi.RaiseWatch(2, v, (now & Reverse) != 0);
                if ((changed & Driver) != 0) ChassisApi.RaiseWatch(3, v, (now & Driver) != 0);
            }
            if (_last.Count == _seen.Count) return;
            _gone.Clear();
            foreach (var kv in _last) if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (int k in _gone) _last.Remove(k);
        }

        internal static void Reset() => _last.Clear();
    }
}
