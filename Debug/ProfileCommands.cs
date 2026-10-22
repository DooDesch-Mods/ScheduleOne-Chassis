#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using Chassis.Api;
using Newtonsoft.Json;

namespace Chassis.Debugging
{
    /// <summary>
    /// <c>chassis profile ...</c>: read and move profile values live. Every change is written to the vehicle code's
    /// JSON file at once and seated rigs re-read it the same frame, so a screenshot right after shows the new value.
    /// </summary>
    internal static class ProfileCommands
    {
        internal static void Help()
        {
            Say("profile list                         - vehicle codes with a profile");
            Say("profile show [code]                  - the profile as JSON (code: default the vehicle you sit in)");
            Say("profile set <field> <values> [code] [seat] - move a value, writes the JSON:");
            Say("    root|eyes|gripstart|gripend|elbow|foot|footrot|knee <x> <y> <z>");
            Say("    leanstart|leanend <x> <y> <z> <w>, yaw|recline|recovery|handrotw|footrotw|followmax|rimradius|rimthick|rimoffset|gripangle <v>, handmode rim|fixed|wheel");
            Say("    stand|handle|pushelbow <x> <y> <z>, reach <v>");
            Say("profile save [code] | reload         - write the JSON | read defaults and files again");
        }

        internal static void Run(string[] p)
        {
            string op = p.Length > 2 ? p[2].ToLower() : "list";
            switch (op)
            {
                case "list":
                    foreach (var code in ChassisApi.Codes) Say(code + " (" + ChassisApi.Get(code).Seats.Count + " seat(s))");
                    break;
                case "show":
                {
                    var prof = ChassisApi.Get(CodeArg(p, 3));
                    if (prof == null) { Say("no profile for '" + CodeArg(p, 3) + "'"); return; }
                    foreach (var line in JsonConvert.SerializeObject(prof, ProfileJson.Settings).Split('\n')) Say(line.TrimEnd());
                    break;
                }
                case "save":
                    Say("saved " + ChassisApi.Save(CodeArg(p, 3)));
                    break;
                case "reload":
                    Say("profiles: " + ChassisApi.Reload());
                    break;
                case "set":
                    Set(p);
                    break;
                default:
                    Say("profile: unknown '" + op + "'");
                    break;
            }
        }

        /// <summary>The profile code of the vehicle the local player sits in or pushes, else null.</summary>
        private static string CurrentCode()
        {
            var lv = Player.Local != null && Player.Local.CurrentVehicle != null
                ? Player.Local.CurrentVehicle.GetComponent<LandVehicle>() : null;
            if (lv == null && Player.Local != null) lv = ChassisApi.PushedBy(Player.Local);
            return lv != null ? ChassisApi.CodeOf(lv) : null;
        }

        private static string CodeArg(string[] p, int i) => p.Length > i ? p[i] : CurrentCode();

        private static void Set(string[] p)
        {
            if (p.Length < 4) { Help(); return; }
            string field = p[3].ToLower();
            int argc = field switch
            {
                "yaw" or "recline" or "recovery" or "handrotw" or "footrotw" or "reach" or "followmax" or "handmode" or "rimradius" or "rimthick" or "rimoffset" or "gripangle" => 1,
                "leanstart" or "leanend" => 4,
                _ => 3,
            };
            string code = p.Length > 4 + argc ? p[4 + argc] : CurrentCode();
            int seatIndex = p.Length > 5 + argc && int.TryParse(p[5 + argc], out int si) ? si : 0;
            var prof = ChassisApi.Get(code);
            if (prof == null) { Say("no profile for '" + code + "'"); return; }
            var seat = prof.Seat(seatIndex);
            var v = V(p, 4);

            switch (field)
            {
                case "root": Need(seat).Root = v; break;
                case "yaw": Need(seat).Yaw = F(p, 4, 0f); break;
                case "recline": Need(seat).Recline = F(p, 4, 0f); break;
                case "eyes": Need(seat).Eyes = v; break;
                case "gripstart": Hands(seat).GripStart = v; break;
                case "gripend": Hands(seat).GripEnd = v; break;
                case "elbow": Hands(seat).Elbow = v; break;
                case "recovery": Hands(seat).Recovery = F(p, 4, 0.35f); break;
                case "handrotw": Hands(seat).RotationWeight = Mathf.Clamp01(F(p, 4, 0f)); break;
                case "handmode": Hands(seat).Mode = p.Length > 4 ? p[4].ToLower() : "fixed"; break;
                case "followmax": Hands(seat).FollowMax = F(p, 4, 90f); break;
                case "rimradius": Hands(seat).RimRadius = Mathf.Max(0f, F(p, 4, 0f)); break;
                case "rimthick": Hands(seat).RimThickness = Mathf.Max(0f, F(p, 4, 0.03f)); break;
                case "gripangle": Hands(seat).GripAngle = F(p, 4, 0f); break;
                case "rimoffset": Hands(seat).RimOffset = F(p, 4, 0f); break;
                case "leanstart": Hands(seat).LeanStart = v; Hands(seat).LeanStartWeight = Mathf.Clamp01(F(p, 7, 0f)); break;
                case "leanend": Hands(seat).LeanEnd = v; Hands(seat).LeanEndWeight = Mathf.Clamp01(F(p, 7, 0f)); break;
                case "foot": Feet(seat).Position = v; break;
                case "footrot": Feet(seat).Rotation = v; break;
                case "footrotw": Feet(seat).RotationWeight = Mathf.Clamp01(F(p, 4, 1f)); break;
                case "knee": Feet(seat).Knee = v; break;
                case "stand": PushOf(prof).Stand = v; break;
                case "handle": PushOf(prof).Handle = v; break;
                case "pushelbow": PushOf(prof).Elbow = v; break;
                case "reach": PushOf(prof).Reach = F(p, 4, 1.2f); break;
                default: Say("profile set: unknown field '" + field + "'"); return;
            }
            ChassisApi.Save(code);
            ChassisApi.NotifyChanged(code);
            Say(code + " seat " + seatIndex + " " + field + " = " + (argc == 1 ? F(p, 4, 0f).ToString(CultureInfo.InvariantCulture) : ProfileJson.F(v)));
        }

        private static SeatProfile Need(SeatProfile s) => s ?? throw new InvalidOperationException("that seat has no profile");
        private static HandProfile Hands(SeatProfile s) => Need(s).Hands ?? (s.Hands = new HandProfile());
        private static FootProfile Feet(SeatProfile s) => Need(s).Feet ?? (s.Feet = new FootProfile());
        private static PushProfile PushOf(ChassisProfile p) => p.Push ?? (p.Push = new PushProfile());

        private static Vector3 V(string[] p, int i) => new Vector3(F(p, i, 0f), F(p, i + 1, 0f), F(p, i + 2, 0f));

        private static float F(string[] p, int i, float fallback)
        {
            if (p.Length <= i) return fallback;
            return float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }

        private static void Say(string line) => DebugConsolePatch.Say(line);
    }
}
#endif
