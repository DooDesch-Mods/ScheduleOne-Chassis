#if DEBUG
using System;
using Chassis.Api;
using Chassis.Vehicles;

namespace Chassis.Debugging
{
    /// <summary>
    /// <c>chassis car ...</c>: horn, indicators, lock, lids and rain on the vehicle the local player sits in, or else
    /// the nearest one within 15 m. The same calls the keys and the public API make.
    /// </summary>
    internal static class VehicleCommands
    {
        internal static void Help()
        {
            Say("car                          - the vehicle: code, profile, owner, signal, parts");
            Say("car enter [code] [seat]      - get into the nearest vehicle, or the nearest of this code (lock checked); seat 1+ as passenger");
            Say("car put <code> <x> <y> <z> [yaw] - spawn a car at this pose (host), stand beside it");
            Say("car bones                    - head, hips, knees and feet of your avatar in the vehicle space");
            Say("car remove                   - despawn the vehicle you sit in, or the nearest one (host)");
            Say("car honk [flash]             - horn (key G)");
            Say("car blink l|r|off            - indicator (arrow keys)");
            Say("car lock on|off              - lock or unlock (owner only)");
            Say("car part <name> open|close   - lid from the profile");
            Say("car side front|rear          - which end the storage is opened from");
            Say("car rain on|off|auto         - force rain for the wipers");
            Say("car assign <code>|off        - give the vehicle another profile");
            Say("car owners                   - every owner record this peer knows");
        }

        internal static void Run(string[] p)
        {
            string op = p.Length > 2 ? p[2].ToLower() : "";
            string arg = p.Length > 3 ? p[3].ToLower() : "";
            if (op == "help") { Help(); return; }
            if (op == "owners") { Say(Fleet.Describe()); Say(Indicators.Describe()); return; }
            if (op == "put")
            {
                // put <code> <x> <y> <z> [yaw]: a vanilla car at an exact pose (host), the player 3 m to its left -
                // for look checks on a free spot instead of 4 m ahead of the player.
                if (p.Length < 7) { Say("car put <code> <x> <y> <z> [yaw]"); return; }
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var pos = new Vector3(float.Parse(p[4], ci), float.Parse(p[5], ci), float.Parse(p[6], ci));
                var rot = Quaternion.Euler(0f, p.Length > 7 ? float.Parse(p[7], ci) : 0f, 0f);
                var made = NetworkSingleton<VehicleManager>.Instance.SpawnAndReturnVehicle(p[3], pos, rot, true);
                if (made == null) { Say("car put: '" + p[3] + "' not spawned (unknown code or not the host)"); return; }
                PlayerSingleton<PlayerMovement>.Instance.Teleport(pos + rot * Vector3.left * 3f + Vector3.up * 0.2f, true);
                Say("car put: " + Registry.Name(made) + " at " + pos.ToString("F1") + " yaw " + rot.eulerAngles.y.ToString("F0"));
                return;
            }
            if (op == "rain")
            {
                Parts.RainOverride = arg == "on" ? true : arg == "off" ? (bool?)false : null;
                Say("rain " + (Parts.RainOverride.HasValue ? (Parts.RainOverride.Value ? "forced on" : "forced off") : "from the weather"));
                return;
            }
            var lv = Target(op == "enter" ? arg : "");
            if (lv == null) { Say("car: no vehicle" + (op == "enter" && arg != "" ? " '" + arg + "'" : "") + " within 15 m"); return; }
            switch (op)
            {
                case "":
                {
                    var profile = ChassisApi.For(lv);
                    Say(Registry.Name(lv) + " profile " + (profile != null ? "'" + profile.Code + "'" : "none") + " owner '" + ChassisApi.OwnerOf(lv) + "'"
                        + (ChassisApi.IsLocked(lv) ? " locked" : "") + " signal " + ChassisApi.Indicator(lv) + " headlights " + (lv.lights != null && lv.lights.HeadlightsOn)
                        + " speed " + lv.Speed_Kmh.ToString("F1") + " steer " + lv.CurrentSteerAngle.ToString("F1"));
                    Say(Parts.Describe(lv));
                    break;
                }
                case "enter":
                {
                    if (!Fleet.CanEnter(lv, out string why)) { Say("car enter: refused, " + why); break; }
                    // A seat index: the seats before it count as taken for this one call, as vanilla takes the first free seat.
                    int seat = p.Length > 4 && int.TryParse(p[4], out int s) ? s : 0;
                    var seats = lv.Seats;
                    if (seat < 0 || seat >= seats.Length) { Say("car enter: " + Registry.Name(lv) + " has seats 0-" + (seats.Length - 1)); break; }
                    var filled = new System.Collections.Generic.List<VehicleSeat>();
                    for (int i = 0; i < seat; i++) if (seats[i].Occupant == null) { seats[i].Occupant = Player.Local; filled.Add(seats[i]); }
                    try { lv.EnterVehicle(); }
                    finally { foreach (var f in filled) f.Occupant = null; }
                    Say("car enter: " + Registry.Name(lv) + " seat " + seat + (lv.LocalPlayerIsDriver ? " (driver)" : " (passenger)"));
                    break;
                }
                case "bones":
                {
                    // Head, hips and feet of the local avatar in the vehicle's space, to fit the seats against roof and underside.
                    var av = Player.Local != null ? Player.Local.Avatar : null;
                    if (av == null) { Say("car bones: no avatar"); break; }
                    foreach (var t in av.GetComponentsInChildren<Transform>(true))
                    {
                        string n = t.name.ToLowerInvariant();
                        if (n.Contains("head") || n.Contains("hip") || n.Contains("foot") || n.Contains("toe") || n.Contains("knee") || n.Contains("calf") || n.Contains("shin"))
                            Say("car bones: " + t.name + " " + lv.transform.InverseTransformPoint(t.position).ToString("F2"));
                    }
                    break;
                }
                case "remove":
                    if (!InstanceFinder.IsServer) { Say("car remove: only the host despawns vehicles"); break; }
                    if (Player.Local != null && Player.Local.CurrentVehicle != null) lv.ExitVehicle();
                    Say("car remove: " + Registry.Name(lv));
                    Compat.Interop.Despawn(lv.gameObject);
                    break;
                case "honk":
                    ChassisApi.Honk(lv, arg == "flash");
                    Say("car honk: asked");
                    break;
                case "blink":
                    ChassisApi.SetIndicator(lv, arg == "l" || arg == "left" ? -1 : arg == "r" || arg == "right" ? 1 : 0);
                    Say("car blink: asked " + arg);
                    break;
                case "lock":
                    ChassisApi.SetLocked(lv, arg != "off");
                    Say("car lock: asked " + (arg != "off" ? "on" : "off"));
                    break;
                case "part":
                    if (p.Length < 5) { Say("car part <name> open|close"); break; }
                    ChassisApi.SetPart(lv, p[3], p[4].ToLower() != "close");
                    Say("car part: asked " + p[3] + " " + p[4]);
                    break;
                case "side":
                    ChassisApi.SetStorageEnd(lv, arg == "front");
                    Say("car side: asked " + arg);
                    break;
                case "assign":
                    if (arg == "off" || arg.Length == 0) ChassisApi.Unassign(lv);
                    else ChassisApi.Assign(lv, p[3]);
                    Say("car assign: " + Registry.Name(lv) + " uses '" + ChassisApi.CodeOf(lv) + "'");
                    break;
                default:
                    Say("car: unknown '" + op + "', try 'chassis car help'");
                    break;
            }
        }

        internal static LandVehicle Target(string code)
        {
            var lp = Player.Local;
            if (lp == null) return null;
            if (lp.CurrentVehicle != null) return lp.CurrentVehicle.GetComponent<LandVehicle>();
            // A net id picks that vehicle at any distance, so a test client can board the host's car from anywhere.
            if (int.TryParse(code, out int id))
            {
                foreach (var v in Registry.All)
                    if (v != null && Net.Ids.Of(v) == id) return v;
                return null;
            }
            LandVehicle best = null;
            float bd = 15f;
            foreach (var v in Registry.All)
            {
                if (v == null || (code != "" && ChassisApi.CodeOf(v) != code)) continue;
                float d = Vector3.Distance(v.transform.position, lp.transform.position);
                if (d < bd) { bd = d; best = v; }
            }
            return best;
        }

        private static void Say(string line) => DebugConsolePatch.Say(line);
    }
}
#endif
