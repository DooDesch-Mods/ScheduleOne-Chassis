using System;
using System.Collections.Generic;
using System.Globalization;
using Chassis.Api;
using Chassis.Net;

namespace Chassis.Seating
{
    /// <summary>
    /// The seated body's hand reaches for the glovebox, the light switch, the cabin light and the ignition key when the
    /// player uses them, and the switch goes when the hand gets there.
    ///
    /// The local player's use waits for the hand: <see cref="Use"/> starts the reach on the local body, tells the host,
    /// and sends the use itself <see cref="SeatRig.ReachOut"/> seconds later. The host passes the reach on to every
    /// peer (req.reach vehicle player switch out -> reach ...), where the same body reaches with the same timing; the use
    /// follows it over the same path, so the switch moves on each peer as that peer's hand arrives.
    ///
    /// Preference <c>HandReachesForSwitches</c> (default on) turns it off for the local player's own uses: the switch
    /// goes at once and no reach is sent, so nobody sees it.
    /// </summary>
    internal static class HandReach
    {
        /// <summary>Head look priority while a hand reaches: above the view direction of <see cref="HeadLook"/>.</summary>
        internal const int LookPriority = HeadLook.Priority + 10;

        /// <summary>Preference HandReachesForSwitches, also set by `chassis reach on|off` for the session.</summary>
        internal static bool Enabled = true;

        private sealed class Pending
        {
            internal float At, Started;
            internal string Name, Vehicle;
            internal Action Send;
        }

        private static readonly List<Pending> _pending = new List<Pending>();
        private static readonly List<Pending> _due = new List<Pending>();

        internal static void LoadSetting()
        {
            try
            {
                var cat = MelonLoader.MelonPreferences.CreateCategory("Chassis");
                var entry = cat.CreateEntry("HandReachesForSwitches", true, "Hand reaches for switches",
                    "The seated body's hand reaches for the glovebox, the light switch, the cabin light and the ignition before the switch goes. Off: switches go at once.");
                Enabled = entry.Value;
                Core.Log.Msg("[Reach] hand reaches for switches " + (Enabled ? "on" : "off") + " (preference HandReachesForSwitches).");
            }
            catch (Exception e)
            {
                Enabled = true;
                Core.Log.Warning("[Reach] reading the HandReachesForSwitches preference failed - the hand reaches: " + e.Message);
            }
        }

        internal static void Reset()
        {
            // Uses still waiting for a hand go out now; leaving the world drops them otherwise.
            FlushAll("world reset");
        }

        /// <summary>True for the switches a hand reaches for: the light switch, the cabin light, the glovebox and the ignition key.</summary>
        internal static bool Reaches(ControlProfile c)
        {
            if (c == null) return false;
            string a = c.Action ?? string.Empty;
            return string.Equals(a, "lights", StringComparison.OrdinalIgnoreCase)
                || string.Equals(a, Vehicles.Glovebox.ControlName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a, Vehicles.Ignition.ControlName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Name, Vehicles.CabinLight.ControlName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The local player uses a switch: with a reaching switch and the setting on, the hand reaches first and
        /// <paramref name="send"/> runs when it arrives; otherwise <paramref name="send"/> runs now.
        /// </summary>
        internal static void Use(LandVehicle lv, ControlProfile c, Action send)
        {
            if (!Reaches(c)) { send(); return; }
            string name = c.Name;
            if (!Enabled)
            {
                Core.Log.Msg("[Reach] '" + name + "' used at once (preference HandReachesForSwitches off).");
                send();
                return;
            }
            var lp = Player.Local;
            var rig = Occupants.Of(lp);
            if (rig == null || !rig.Alive || rig.IsPusher || rig.Vehicle != lv.transform)
            {
                Core.Log.Msg("[Reach] '" + name + "' used at once: the local body does not sit in " + Vehicles.Registry.Name(lv) + ".");
                send();
                return;
            }
            var bound = Find(lv, name);
            if (bound == null)
            {
                Core.Log.Warning("[Reach] '" + name + "' is not bound on " + Vehicles.Registry.Name(lv) + " - nothing to reach for, used at once.");
                send();
                return;
            }
            // A second use while a hand is still on its way: the first use goes now, the hand turns to the new switch.
            FlushAll("a new reach");
            float out_ = rig.ReachFor(bound, -1f, out string why);
            if (out_ < 0f)
            {
                Core.Log.Msg("[Reach] '" + name + "' used at once: " + why + ".");
                send();
                return;
            }
            int vehicle = Ids.Of(lv), player = Ids.Of(lp);
            if (vehicle >= 0 && player >= 0)
                ChassisNet.ToHost("req.reach", vehicle.ToString(CultureInfo.InvariantCulture), player.ToString(CultureInfo.InvariantCulture), name,
                    out_.ToString("0.###", CultureInfo.InvariantCulture));
            else Core.Log.Warning("[Reach] " + Vehicles.Registry.Name(lv) + " or the local player has no network id - the other players do not see the reach.");
            _pending.Add(new Pending { At = Time.time + out_, Started = Time.time, Name = name, Vehicle = Vehicles.Registry.Name(lv), Send = send });
        }

        /// <summary>Host: passes a player's reach on to everyone, after checking the player sits in that vehicle.</summary>
        internal static void HostReach(int vehicle, int player, string name, float out_)
        {
            var lv = Vehicles.Registry.ById(vehicle);
            if (lv == null) { Core.Log.Warning("[Reach] reach for '" + name + "' on net " + vehicle + ", which is not here - dropped."); return; }
            var p = Ids.PlayerById(player);
            if (p == null) { Core.Log.Warning("[Reach] reach from player net " + player + ", who is not here - dropped."); return; }
            if (p.CurrentVehicle == null || p.CurrentVehicle.transform != lv.transform) { Core.Log.Msg("[Reach] " + p.PlayerName + " does not sit in " + Vehicles.Registry.Name(lv) + " - reach not passed on."); return; }
            float t = float.IsNaN(out_) || float.IsInfinity(out_) ? SeatRig.ReachOutMin : Mathf.Clamp(out_, SeatRig.ReachOutMin, SeatRig.ReachOutMax);
            ChassisNet.ToAll("reach", vehicle.ToString(CultureInfo.InvariantCulture), player.ToString(CultureInfo.InvariantCulture), name, t.ToString("0.###", CultureInfo.InvariantCulture));
        }

        /// <summary>Everyone: a player's hand reaches for a switch. The local player's own reach already runs.</summary>
        internal static void OnReach(int vehicle, int player, string name, float out_)
        {
            var p = Ids.PlayerById(player);
            if (p == null) { Core.Log.Warning("[Reach] reach from player net " + player + ", who is not here - not shown."); return; }
            if (p.IsLocalPlayer) return;
            var lv = Vehicles.Registry.ById(vehicle);
            if (lv == null) { Core.Log.Warning("[Reach] " + p.PlayerName + " reaches in net " + vehicle + ", which is not here - not shown."); return; }
            var rig = Occupants.Of(p);
            if (rig == null || !rig.Alive) { Core.Log.Msg("[Reach] " + p.PlayerName + "'s body is not seated on this peer - reach for '" + name + "' not shown."); return; }
            var bound = Find(lv, name);
            if (bound == null) { Core.Log.Warning("[Reach] '" + name + "' is not bound on " + Vehicles.Registry.Name(lv) + " here - " + p.PlayerName + "'s reach not shown."); return; }
            if (rig.ReachFor(bound, out_, out string why) < 0f) Core.Log.Msg("[Reach] " + p.PlayerName + "'s reach for '" + name + "' not shown: " + why + ".");
        }

        private static Instruments.Controls.Bound Find(LandVehicle lv, string name)
        {
            foreach (var b in Instruments.Controls.Of(lv))
                if (string.Equals(b.P.Name, name, StringComparison.OrdinalIgnoreCase)) return b;
            return null;
        }

        /// <summary>Every frame: sends the uses whose hand has arrived.</summary>
        internal static void Tick()
        {
            if (_pending.Count == 0) return;
            _due.Clear();
            float now = Time.time;
            foreach (var p in _pending) if (now >= p.At) _due.Add(p);
            foreach (var p in _due)
            {
                _pending.Remove(p);
                Fire(p, "the hand arrived");
            }
        }

        private static void FlushAll(string why)
        {
            if (_pending.Count == 0) return;
            _due.Clear();
            _due.AddRange(_pending);
            _pending.Clear();
            foreach (var p in _due) Fire(p, why);
        }

        private static void Fire(Pending p, string why)
        {
            Core.Log.Msg("[Reach] '" + p.Name + "' in " + p.Vehicle + " used after " + (Time.time - p.Started).ToString("0.00", CultureInfo.InvariantCulture) + " s (" + why + ").");
            try { p.Send(); }
            catch (Exception e) { Core.Log.Error("[Reach] using '" + p.Name + "' after the reach failed: " + e); }
        }

        /// <summary>`chassis reach`: the setting, uses waiting for a hand, and per seated player the reach state.</summary>
        internal static string Describe()
        {
            var sb = new System.Text.StringBuilder("reach: hand reaches for switches " + (Enabled ? "on" : "off") + ", " + _pending.Count + " use(s) waiting");
            int n = 0;
            foreach (var rig in Occupants.All)
            {
                if (!rig.Alive || rig.Player == null || rig.IsPusher) continue;
                n++;
                sb.Append("\nreach: ").Append(rig.DescribeReach());
            }
            if (n == 0) sb.Append("\nreach: no seated player");
            return sb.ToString();
        }
    }
}
