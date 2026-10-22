using Chassis.Compat;
using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Chassis.Seating;

namespace Chassis.Debugging
{
    /// <summary>
    /// Console commands under one word: <c>chassis &lt;subcommand&gt;</c>. <c>telemetry</c> and <c>zones</c> are there
    /// in every build, for authors of interiors; everything else is a DEBUG dev command. Console, not hotkeys, so
    /// every check can be scripted through the bridge.
    ///
    /// Both Console.SubmitCommand overloads are patched (string + List&lt;string&gt;): depending on the caller either
    /// one fires, and dispatch dedupes per frame and signature so a command never runs twice.
    /// </summary>
    internal static class DebugConsolePatch
    {
        internal const string Word = "chassis";

#if DEBUG
        internal const string Usage = "chassis help, chassis telemetry, chassis zones, chassis status, chassis drive 1 0";
#else
        internal const string Usage = "chassis help, chassis telemetry, chassis zones, chassis zones off";
#endif

        private static readonly List<string> _pending = new List<string>();

        internal static bool TryHandle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return Dispatch(raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }

        internal static bool TryHandle(GameStringList args)
        {
            if (args == null || args.Count == 0) return false;
            string[] parts = new string[args.Count];
            for (int i = 0; i < args.Count; i++) parts[i] = args[i];
            return Dispatch(parts);
        }

        private static int _lastFrame = -1;
        private static string _lastSig = "";

        private static bool Dispatch(string[] parts)
        {
            if (parts.Length == 0) return false;
            if (!string.Equals(parts[0], Word, StringComparison.OrdinalIgnoreCase)) return false;

            string sig = string.Join(" ", parts);
            int frame = Time.frameCount;
            if (frame == _lastFrame && sig == _lastSig) return true;
            _lastFrame = frame; _lastSig = sig;

            string sub = parts.Length > 1 ? parts[1].ToLower() : "help";
            try
            {
                switch (sub)
                {
                    case "help":
                        Say("telemetry [net id]           - instrument sources with validity, gauges and switches of your car or the nearest one");
                        Say("zones [off]                  - hit zones of the dash switches (green) and the reach (yellow rings)");
#if DEBUG
                        Say("aim <switch>                 - turn the first-person view to a switch of your car");
                        Say("click | scroll <n>           - click or turn the switch in view, as the mouse would");
                        Say("outline                      - where the highlight copies of the switch in view sit");
                        Say("ctl <switch> [value]         - use a switch of your car through the API");
                        Say("coast [km/h] [s]             - hold your car at that speed along its facing (default 40 km/h, 5 s)");
                        Say("creator                      - finish an open character creator with the default looks (test clients)");
                        Say("notice                       - close the offense notice after an arrest");
                        Say("hints [format]               - vehicle key hints; a format with {0} relabels the arrow keys");
                        Say("status                       - seated bodies and pushers, view, key hints, current vehicle");
                        Say("showroom [tp|enter|hover|press <i>] [m] - showroom cars; tp to car i's door (m metres out, facing it), enter gets in, hover/press fire its prompt events");
                        Say("showroom ghost on|off        - draw the hidden display props over their cars (alignment check)");
                        Say("view [pitch]                 - first person on/off while seated (key V); a pitch turns it on looking down that far");
                        Say("exit                         - leave the vehicle (as the Interact key)");
                        Say("drive <fwd> <steer> [s]       - driver input for s seconds (default 3)");
                        Say("despawn car                  - remove the nearest car within 30 m");
                        Say("cam <dist> <yaw> <height>|off - orbit camera around your vehicle or the nearest seated body");
                        Say("cam hands|handl|handr <dist> <yaw> <height> - the same, aimed at both hands or one");
                        Say("grip [markers on|off] [wrap palm f1 f2 f3 t1 t2 t3 roll at] - steering wheel grip: colour markers, live angles");
                        Say("feet                         - ankle axes of every seated body (side x near -1 = sole down)");
                        Say("profile list|show|save|reload|set <field> <values> [code] [seat] - seat profiles (see 'chassis profile')");
                        Say("target seat|patrol|near|gun|shoot|keep|list - shots into cars (see 'chassis target')");
                        Say("car [enter|honk|blink|lock|part|side|rain|assign|owners] - horn, indicators, lock, lids (see 'chassis car help')");
                        Say("tune [part|remove|set|clear|stats|demo] - paint, parts, lights and driving of a car (see 'chassis tune help')");
                        Say("interior on|off|rebuild      - base interior and tinted glass in the vanilla cars");
                        Say("interior fit on|off          - the kit gives way where it reaches out of the body (off = formula sizes)");
                        Say("interior probe [off]         - red cubes on kit corners outside the body of the car you sit in or the nearest");
                        Say("interior lift <m>            - lowest cushion top above the car's underside, fits the vanilla cars again");
                        Say("interior glass [r g b a] [smooth] [metal] [premul 0|1] [inside] - retint the glass live");
#endif
                        break;

                    case "telemetry":
                        Telemetry(parts);
                        break;

                    case "zones":
                        Say(Instruments.Cockpit.SetZones(!(parts.Length > 2 && parts[2].ToLowerInvariant() == "off")));
                        break;

#if DEBUG
                    case "aim":
                    {
                        var lv = Target(null);
                        if (lv == null || parts.Length < 3) { Say("aim: sit in a car and name a switch"); break; }
                        Instruments.Controls.Bound hit = null;
                        foreach (var b in Instruments.Controls.Of(lv))
                            if (string.Equals(b.P.Name, parts[2], StringComparison.OrdinalIgnoreCase)) hit = b;
                        if (hit == null) { Say("aim: no switch '" + parts[2] + "' bound on " + lv.vehicleCode); break; }
                        Say("aim: " + Seating.FirstPersonView.LookAt(hit.T.TransformPoint(hit.BoxCenter)));
                        break;
                    }
                    case "creator":
                    {
                        // A joining test client without saved looks starts in the character creator; finish it with the defaults.
#if IL2CPP
                        var cc = Singleton<Il2CppScheduleOne.CharacterCreator.CharacterCreator>.Instance;
#else
                        var cc = Singleton<ScheduleOne.CharacterCreator.CharacterCreator>.Instance;
#endif
                        if (cc == null || !cc.IsOpen) Say("creator: not open");
                        else { cc.Done(); Say("creator: done with the default looks"); }
                        break;
                    }
                    case "notice":
                    {
                        // The offense notice after an arrest holds the player until its Close button; tests close it here.
#if IL2CPP
                        var ns = Singleton<Il2CppScheduleOne.UI.ArrestNoticeScreen>.Instance;
#else
                        var ns = Singleton<ScheduleOne.UI.ArrestNoticeScreen>.Instance;
#endif
                        if (ns == null) Say("notice: no offense notice screen");
                        else { ns.Exit(); Say("notice: closed"); }
                        break;
                    }
                    case "coast":
                    {
                        // coast <km/h> [s]: holds the car at that speed along its facing, for the speedometer and the odometer.
                        var lv = Target(null);
                        if (lv == null) { Say("coast: no vehicle"); break; }
                        float kmh = F(parts, 2, 40f), sec = F(parts, 3, 5f);
                        MelonLoader.MelonCoroutines.Start(Coast(lv, kmh / 3.6f, sec));
                        Say("coast: " + Vehicles.Registry.Name(lv) + " held at " + kmh.ToString("0", CultureInfo.InvariantCulture) + " km/h for " + sec.ToString("0.#", CultureInfo.InvariantCulture) + " s");
                        break;
                    }
                    case "click":
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        string done = Instruments.Cockpit.ClickHovered();
                        Say("click: " + done + " in " + sw.Elapsed.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture) + " ms");
                        break;
                    }
                    case "outline":
                    {
                        // The outline copies of the switch in view: where they sit against the switch and its parent.
                        var hb = Instruments.Cockpit.Hovered;
                        if (hb == null) { Say("outline: no switch in view"); break; }
                        Say("outline: '" + hb.P.Name + "' parent " + (hb.T.parent != null ? hb.T.parent.name : "none") + ", " + hb.Outline.Count + " copy(ies), switch scale " + hb.T.lossyScale.ToString("F3"));
                        for (int i = 0; i < hb.Outline.Count; i++)
                        {
                            var go = hb.Outline[i];
                            if (go == null) continue;
                            var mf = go.GetComponent<MeshFilter>();
                            Bounds mb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : default;
                            Transform up = hb.T.parent != null ? hb.T.parent : hb.T;
                            Vector3 mn = up.InverseTransformPoint(go.transform.TransformPoint(mb.min)), mx = up.InverseTransformPoint(go.transform.TransformPoint(mb.max));
                            Vector3 smn = up.InverseTransformPoint(hb.T.TransformPoint(mb.min)), smx = up.InverseTransformPoint(hb.T.TransformPoint(mb.max));
                            Say("outline " + i + ": active " + go.activeInHierarchy + " local " + go.transform.localPosition.ToString("F4") + " scale " + go.transform.localScale.ToString("F3")
                                + ", copy " + mn.ToString("F4") + ".." + mx.ToString("F4") + ", switch " + smn.ToString("F4") + ".." + smx.ToString("F4") + " in the parent's space");
                        }
                        break;
                    }
                    case "scroll":
                        Say("scroll: " + Instruments.Cockpit.ScrollHovered(parts.Length > 2 && int.TryParse(parts[2], out int notches) ? notches : 1));
                        break;
                    case "ctl":
                    {
                        var lv = Target(null);
                        if (lv == null || parts.Length < 3) { Say("ctl: sit in a car and name a switch"); break; }
                        float? val = parts.Length > 3 ? F(parts, 3, 0f) : (float?)null;
                        Api.ChassisApi.UseControl(lv, parts[2], val);
                        Say("ctl: used '" + parts[2] + "'" + (val.HasValue ? " with " + val.Value.ToString(CultureInfo.InvariantCulture) : "") + ", value now " + Api.ChassisApi.ControlValue(lv, parts[2]).ToString("0.##", CultureInfo.InvariantCulture));
                        break;
                    }

                    case "showroom":
                        {
                            // showroom [tp|enter <i>]: the display cars; tp stands at car i's door, enter also gets in.
                            string act = parts.Length > 2 ? parts[2].ToLower() : "";
                            if (act == "tp" || act == "enter")
                            {
                                int si = parts.Length > 3 && int.TryParse(parts[3], out int n) ? n : -1;
                                var car = Rideables.Showroom.CarAt(si, out string dn);
                                if (car == null) { Say("showroom " + act + ": no car at index " + si + (dn != null ? " (" + dn + ")" : "") + ", 0-" + (Rideables.Showroom.Count - 1)); break; }
                                // tp <i> [m]: stand m metres out from the driver's door, facing the car, so the
                                // interaction raycast hits it and the real hover prompt shows.
                                Vector3 door = car.GetExitPoint(0).position;
                                Vector3 outward = Vector3.ProjectOnPlane(door - car.transform.position, Vector3.up).normalized;
                                float back = parts.Length > 4 && float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float bm) ? bm : 0f;
                                PlayerSingleton<PlayerMovement>.Instance.Teleport(door + outward * back + Vector3.up * 0.1f, true);
                                if (act == "tp") PlayerSingleton<PlayerCamera>.Instance.LookAt(car.transform.position + Vector3.up * 1.1f, 0.3f);
                                if (act == "enter") car.EnterVehicle();
                                Say("showroom " + act + ": " + dn + " " + Vehicles.Registry.Name(car));
                                break;
                            }
                            if (act == "ghost") { Say(Rideables.Showroom.Ghost(parts.Length > 3 && parts[3] == "on")); break; }
                            if (act == "hover" || act == "press")
                            {
                                Say(Rideables.Showroom.Press(parts.Length > 3 && int.TryParse(parts[3], out int pi) ? pi : -1, act == "press"));
                                break;
                            }
                            Say(Rideables.Showroom.Describe());
                        }
                        break;

                    case "status":
                        Say(Seating.Occupants.Describe());
                        Say(Seating.FirstPersonView.Describe());
                        Say(Vehicles.KeyHints.Describe());
                        {
                            var lp = Player.Local;
                            var cur = lp != null && lp.CurrentVehicle != null ? lp.CurrentVehicle.GetComponent<LandVehicle>() : null;
                            if (cur != null) Say("vehicle " + cur.vehicleCode + " net " + Net.Ids.Of(cur) + " speed " + cur.Speed_Kmh.ToString("F1") + " km/h throttle " + cur.currentThrottle.ToString("F2") + " steer " + cur.CurrentSteerAngle.ToString("F0"));
                        }
                        break;
                    case "feet":
                        {
                            int n = 0;
                            foreach (var rig in Seating.Occupants.All) { Say(rig.DescribeFeet()); n++; }
                            foreach (var rig in Seating.NpcRiders.All) { Say(rig.DescribeFeet()); n++; }
                            if (n == 0) Say("feet: nobody is seated");
                        }
                        break;
                    case "exit":
                        {
                            // The same call the Interact key makes in LandVehicle.Update.
                            var veh = Player.Local != null ? Player.Local.CurrentVehicle : null;
                            if (veh == null) { Say("exit: not in a vehicle"); break; }
                            var lv = veh.GetComponent<LandVehicle>();
                            if (lv == null) { Say("exit: current vehicle has no LandVehicle"); break; }
                            lv.ExitVehicle();
                            Say("exit: left net " + Net.Ids.Of(lv));
                        }
                        break;
                    case "crouch":
                        {
                            // crouch [on|off]: the crouch key, without the headroom check of PlayerMovement.TryToggleCrouch.
                            // crouch keep|fix: a seated body stays crouched as in vanilla, or sits upright.
                            if (parts.Length > 2 && (parts[2] == "keep" || parts[2] == "fix"))
                            {
                                Seating.SeatRig.DebugKeepCrouch = parts[2] == "keep";
                                Say("crouch: seated bodies " + (Seating.SeatRig.DebugKeepCrouch ? "stay crouched" : "sit upright"));
                                break;
                            }
                            var pm = PlayerSingleton<PlayerMovement>.Instance;
                            if (pm == null) { Say("crouch: no PlayerMovement"); break; }
                            bool on = parts.Length > 2 ? parts[2] != "off" : !pm.IsCrouched;
                            pm.SetCrouched(on);
                            Say("crouch: player crouched " + pm.IsCrouched + ", avatar crouched " + (Player.Local != null && Player.Local.Avatar != null && Player.Local.Avatar.Animation != null && Player.Local.Avatar.Animation.IsCrouched));
                        }
                        break;
                    case "cam":
                        Say(LookCam.Set(parts));
                        break;
                    case "grip":
                        Say(Seating.SeatRig.Tune(parts));
                        break;
                    case "drive":
                        {
                            // drive <forward> <steer> [seconds]: stands in for the driver's keys.
                            float f = parts.Length > 2 ? float.Parse(parts[2], CultureInfo.InvariantCulture) : 1f;
                            float st = parts.Length > 3 ? float.Parse(parts[3], CultureInfo.InvariantCulture) : 0f;
                            float sec = parts.Length > 4 ? float.Parse(parts[4], CultureInfo.InvariantCulture) : 3f;
                            Seating.DriveInput.DebugDrive = new Vector2(f, st);
                            Seating.DriveInput.DebugUntil = Time.time + sec;
                            Say("drive: forward " + f + " steer " + st + " for " + sec + " s");
                        }
                        break;
                    case "view":
                        if (parts.Length > 2 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float vp))
                        {
                            Seating.FirstPersonView.Set(true, "console");
                            Seating.FirstPersonView.Look(vp);
                        }
                        else Seating.FirstPersonView.Set(!Seating.FirstPersonView.Active, "console");
                        Say(Seating.FirstPersonView.Describe());
                        break;
                    case "hints":
                        // hints [arrow format]: the current key hints; a format ({0} = arrow glyph) relabels the arrow keys.
                        if (parts.Length > 2)
                        {
                            Vehicles.KeyHints.ArrowFormat = string.Join(" ", parts, 2, parts.Length - 2);
                            Vehicles.KeyHints.Rebuild();
                        }
                        Say(Vehicles.KeyHints.Describe() + ", arrows '" + Vehicles.KeyHints.ArrowFormat + "'");
                        break;
                    case "profile":
                        ProfileCommands.Run(parts);
                        break;

                    case "car":
                        VehicleCommands.Run(parts);
                        break;

                    case "target":
                        TargetCommands.Run(parts);
                        break;

                    case "tune":
                        TuneCommands.Run(parts);
                        break;

                    case "interior":
                    {
                        string op = parts.Length > 2 ? parts[2].ToLowerInvariant() : "";
                        if (op == "on") { Interior.BaseInterior.Enabled = true; Say("interior on - vanilla cars get the base interior within a second"); }
                        else if (op == "off") { Interior.BaseInterior.Enabled = false; Say("interior off, " + Interior.BaseInterior.RemoveAll() + " removed"); }
                        else if (op == "lift" && parts.Length > 3)
                        {
                            Interior.BaseInterior.CushionAboveUnderside = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                            Say("interior lift: cushions at least " + Interior.BaseInterior.CushionAboveUnderside + " m above the underside, " + Interior.BaseInterior.Refit() + " code(s) fitted again");
                        }
                        else if (op == "fit" && parts.Length > 3)
                        {
                            Interior.BaseInterior.FitToShell = parts[3] != "off";
                            int n = Interior.BaseInterior.RemoveAll();
                            Say("interior fit " + (Interior.BaseInterior.FitToShell ? "on" : "off") + ": " + n + " removed, " + Interior.BaseInterior.Refit() + " code(s) fitted again");
                        }
                        else if (op == "probe")
                        {
                            // probe [off]: the car the player sits in, else the nearest one within 30 m.
                            var lp = Player.Local;
                            LandVehicle car = lp != null && lp.CurrentVehicle != null ? lp.CurrentVehicle.GetComponent<LandVehicle>() : null;
                            if (car == null && lp != null)
                            {
                                float best = 30f;
                                foreach (var v in UnityEngine.Object.FindObjectsOfType<LandVehicle>())
                                {
                                    float dist = Vector3.Distance(v.transform.position, lp.transform.position);
                                    if (dist < best) { best = dist; car = v; }
                                }
                            }
                            Say("interior probe: " + Interior.BaseInterior.Probe(car, !(parts.Length > 3 && parts[3] == "off")));
                        }
                        else if (op == "rebuild") { int n = Interior.BaseInterior.RemoveAll(); Interior.BaseInterior.Reset(); Say("interior: " + n + " removed, rebuilding from the current profiles"); }
                        else if (op == "glass")
                        {
                            // glass [r g b a] [smoothness] [metallic]: retints every tinted window already made.
                            var ci = System.Globalization.CultureInfo.InvariantCulture;
                            Color? tint = null;
                            float sm = Interior.BaseInterior.GlassSmoothness, me = Interior.BaseInterior.GlassMetallic;
                            if (parts.Length >= 7) tint = new Color(float.Parse(parts[3], ci), float.Parse(parts[4], ci), float.Parse(parts[5], ci), float.Parse(parts[6], ci));
                            if (parts.Length >= 8) sm = float.Parse(parts[7], ci);
                            if (parts.Length >= 9) me = float.Parse(parts[8], ci);
                            bool pre = parts.Length >= 10 ? parts[9] != "0" : Interior.BaseInterior.GlassPremultiply;
                            float inn = parts.Length >= 11 ? float.Parse(parts[10], ci) : Interior.BaseInterior.GlassInside;
                            int n = Interior.BaseInterior.Retint(tint, sm, me, pre, inn);
                            Say("interior glass: " + n + " material(s), tint " + (tint.HasValue ? tint.Value.ToString() : "kept") + ", smoothness " + sm + ", metallic " + me + ", premultiply " + pre + ", inside " + inn);
                        }
                        else Say(Interior.BaseInterior.Describe() + " - use on|off|rebuild|glass [r g b a] [smooth] [metal]");
                        break;
                    }

                    case "despawn":
                    {
                        // despawn car: the nearest car within 30 m, so test runs do not pile cars up at one spot.
                        if (parts.Length < 3 || parts[2].ToLower() != "car") { Say("despawn: use 'despawn car'"); break; }
                        var car = Target(null);
                        if (car == null) { Say("despawn car: no car within 30 m"); break; }
                        Say("despawning " + Vehicles.Registry.Name(car));
                        Compat.Interop.Despawn(car.gameObject);
                        break;
                    }

                    case "lights":
                        // Lamp meshes of a car (VehicleLights) and every renderer with its materials, in the car's root space.
                        Lights(Target(parts.Length > 2 ? parts[2] : null));
                        break;
#endif
                    default:
                        Say("unknown subcommand '" + sub + "', try 'chassis help'");
                        break;
                }
            }
            catch (Exception e)
            {
                Core.Log.Error("[Console] '" + sig + "' failed: " + e);
                Say("failed: " + e.Message);
            }
            return true;
        }

        /// <summary>The car the local player sits in; else the one with that net id; else the nearest within 30 m.</summary>
        internal static LandVehicle Target(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                if (!int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) { Say("not a net id: '" + id + "'"); return null; }
                return Net.Ids.Find<LandVehicle>(n);
            }
            var lp = Player.Local;
            if (lp == null) return null;
            if (lp.CurrentVehicle != null)
            {
                var cur = lp.CurrentVehicle.GetComponent<LandVehicle>();
                if (cur != null) return cur;
            }
            LandVehicle best = null; float bd = 30f;
            foreach (var v in UnityEngine.Object.FindObjectsOfType<LandVehicle>())
            {
                float d = Vector3.Distance(v.transform.position, lp.transform.position);
                if (d < bd) { bd = d; best = v; }
            }
            return best;
        }

        private static void Telemetry(string[] p)
        {
            var v = Target(p.Length > 2 ? p[2] : null);
            if (v == null) { Say("telemetry: no vehicle (sit in one, stand within 30 m, or give a net id)"); return; }
            Say("telemetry of " + Vehicles.Registry.Name(v) + ", units " + (Instruments.Sources.Mph ? "mph" : "km/h")
                + (Api.ChassisApi.Get(v.vehicleCode) == null ? ", no Chassis profile" : ""));
            foreach (var d in Instruments.Sources.All)
            {
                var r = Instruments.Sources.Read(v, d.Name);
                Say("  " + d.Name.PadRight(16) + (r.Valid ? r.Value.ToString("0.###", CultureInfo.InvariantCulture) : "invalid").PadRight(9) + d.Unit + (d.Custom ? " (mod)" : ""));
            }
            foreach (var line in Instruments.Gauges.Describe(v)) Say(line);
            foreach (var line in Instruments.Controls.Describe(v)) Say(line);
        }

#if DEBUG
        private static void Lights(LandVehicle v)
        {
            if (v == null) { Say("lights: no vehicle (sit in one, stand within 30 m, or give a net id)"); return; }
            Transform root = v.transform;
            Say("lights of " + Vehicles.Registry.Name(v));
            var vl = v.GetComponent<VehicleLights>();
            if (vl == null) Say("  no VehicleLights");
            else
            {
                LightGroup("head", vl.headLightMeshes, root);
                LightGroup("brake", vl.brakeLightMeshes, root);
                LightGroup("reverse", vl.reverseLightMeshes, root);
            }
            foreach (var line in Vehicles.VanillaLamps.Describe(v)) Say(line);
            foreach (var r in v.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                var names = new List<string>();
                foreach (var m in mats)
                {
                    if (m == null) { names.Add("null"); continue; }
                    Texture t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                    names.Add(m.name + (t != null ? "[" + t.name + " " + t.width + "x" + t.height + "]" : ""));
                }
                Say("  r " + r.name + " on=" + r.enabled + " " + RootBounds(r, root) + " mats " + string.Join(", ", names));
            }
        }

        private static void LightGroup(string what, MeshRenderer[] rs, Transform root)
        {
            if (rs == null) { Say("  " + what + ": none"); return; }
            foreach (var r in rs)
                if (r != null) Say("  " + what + " " + r.name + " " + RootBounds(r, root) + " mat " + (r.sharedMaterial != null ? r.sharedMaterial.name : "null"));
        }

        /// <summary>The mesh bounds of a renderer, as min .. max in the root's space.</summary>
        private static string RootBounds(Renderer r, Transform root)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return "(no mesh)";
            Bounds b = mf.sharedMesh.bounds;
            Matrix4x4 m = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
            Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(c);
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            }
            return min.ToString("F2") + " .. " + max.ToString("F2");
        }

        private static System.Collections.IEnumerator Coast(LandVehicle lv, float metresPerSecond, float seconds)
        {
            float until = Time.time + seconds;
            Vector3 start = lv.transform.position;
            while (lv != null && Time.time < until)
            {
                var rb = lv.GetComponent<Rigidbody>();
                if (rb == null) { Core.Log.Warning("[Console] coast stopped: the vehicle has no Rigidbody."); yield break; }
                Vector3 v = lv.transform.forward * metresPerSecond;
                rb.velocity = new Vector3(v.x, rb.velocity.y, v.z);
                yield return null;
            }
            if (lv == null) { Core.Log.Warning("[Console] coast ended: the vehicle is gone."); yield break; }
            Core.Log.Msg("[Console] coast ended after " + Vector3.Distance(start, lv.transform.position).ToString("F1", CultureInfo.InvariantCulture) + " m.");
        }

        private static float F(string[] p, int i, float fallback)
        {
            if (p.Length <= i) return fallback;
            if (float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v;
            Say("not a number: '" + p[i] + "', kept " + fallback.ToString(CultureInfo.InvariantCulture));
            return fallback;
        }
#endif

        internal static void Say(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            Core.Log.Msg(line);
            _pending.Add("[Chassis] " + line);
        }

        internal static void FlushToConsole()
        {
            if (_pending.Count == 0) return;
            for (int i = 0; i < _pending.Count; i++)
            {
                try { GameConsole.Log(_pending[i]); }
                catch (Exception e) { Core.Log.Warning("[Console] could not echo into the game console: " + e.Message); }
            }
            _pending.Clear();
        }
    }

    [HarmonyPatch(typeof(GameConsole), nameof(GameConsole.SubmitCommand), new Type[] { typeof(string) })]
    internal static class Chassis_Console_SubmitCommand_String_Patch
    {
        private static bool Prefix(string args)
        {
            try { return !DebugConsolePatch.TryHandle(args); }
            catch (Exception e) { Core.Log.Error("[Console] dispatch failed: " + e); return true; }
        }
    }

    [HarmonyPatch(typeof(GameConsole), nameof(GameConsole.SubmitCommand), new Type[] { typeof(GameStringList) })]
    internal static class Chassis_Console_SubmitCommand_List_Patch
    {
        private static bool Prefix(GameStringList args)
        {
            try { return !DebugConsolePatch.TryHandle(args); }
            catch (Exception e) { Core.Log.Error("[Console] dispatch failed: " + e); return true; }
        }
    }
}
