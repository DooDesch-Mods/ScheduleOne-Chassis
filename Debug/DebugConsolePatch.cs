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
        internal const string Usage = "chassis help, chassis telemetry, chassis zones, chassis chairs, chassis demo tp, chassis demo off";
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
                        Say("ctl <switch> [value]         - use a switch of your car through the API");
                        Say("coast [km/h] [s]             - hold your car at that speed along its facing (default 40 km/h, 5 s)");
                        Say("creator                      - finish an open character creator with the default looks (test clients)");
                        Say("notice                       - close the offense notice after an arrest");
                        Say("chairs                       - every wheelchair in the scene with its position");
                        Say("hints [format]               - vehicle key hints; a format with {0} relabels the arrow keys");
                        Say("demo [tp [n]]                - sit in the nearest wheelchair (tp: any distance, n: index from 'chairs')");
                        Say("demo off                     - stand up again");
                        Say("demo state                   - seat, grip and IK state");
                        Say("demo seat <x> <y> <z> [yaw]  - avatar root in the chair's space");
                        Say("demo fit [gap]               - hip joints <gap> m above the seat top (default 0.09)");
                        Say("demo foot <x> <y> <z> [rotW] - right foot in the chair's space (left is mirrored)");
                        Say("demo footrot <x> <y> <z>     - right foot rotation against the chair in degrees (yaw, roll mirrored)");
                        Say("demo knee|elbow <x> <y> <z>  - bend goal in the chair's space (left is mirrored)");
                        Say("demo lean <x> <y> <z> [w]    - spine reach point in the chair's space and its weight");
                        Say("demo scale <s>               - chair size against the original");
                        Say("demo parts                   - every chair mesh with centre and size");
                        Say("demo feet                    - solved ankles and toes in the chair's space");
                        Say("demo probe [frames]          - range of root, ankle, player and chair over frames (shake check)");
                        Say("demo mark <x> <y> <z> [size] | bones [size] | clear - spheres at a chair-space point (red) and its mirror (blue)");
                        Say("demo grip <x> <y> <z> [rotW] - right-hand grip from the wheel centre (left is mirrored)");
                        Say("demo cam out [dist] [yaw] [height] | fp [pitch] [forward] | off - look-check camera");
                        Say("status                       - seated bodies, swapped props, pushers, view, current vehicle");
                        Say("sit [tp <i>]                  - sit on the nearest wheelchair prop (as the Sit prompt); tp: go to prop i first");
                        Say("push [off [net]|fake]        - push the nearest occupied wheelchair, or let go; fake: push an empty one");
                        Say("view [pitch]                 - first person on/off while seated (key V); a pitch turns it on looking down that far");
                        Say("exit                         - leave the vehicle (as the Interact key)");
                        Say("move <yaw> [m]             - turn the nearest wheelchair vehicle by yaw, then shift it m along its facing");
                        Say("drive|pushin <fwd> <steer> [s] - driver or pusher input for s seconds (default 3; steer works in any car)");
                        Say("cam <dist> <yaw> <height>|off - orbit camera around your vehicle or the nearest wheelchair");
                        Say("feet                         - ankle axes of every seated body (side x near -1 = sole down)");
                        Say("spawn | despawn              - wheelchair vehicle 2 m ahead (host) | remove all");
                        Say("tree [i]                     - hierarchy of wheelchair prop i");
                        Say("profile list|show|save|reload|set <field> <values> [code] [seat] - seat profiles (see 'chassis profile')");
                        Say("car [enter|honk|blink|lock|part|side|rain|assign|owners] - horn, indicators, lock, lids (see 'chassis car help')");
                        Say("interior on|off|rebuild      - base interior and tinted glass in the vanilla cars");
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
                        Say("click: " + Instruments.Cockpit.ClickHovered());
                        break;
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

                    case "status":
                        Say(Seating.Occupants.Describe());
                        Say(Rideables.PropSwap.Describe());
                        Say(Rideables.Push.Describe());
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
                    case "wheels":
                        {
                            // Physics state of the nearest wheelchair vehicle: body and every wheel collider.
                            var lp = Player.Local;
                            LandVehicle v = null; float bd = float.MaxValue;
                            foreach (var x in UnityEngine.Object.FindObjectsOfType<LandVehicle>())
                            {
                                if (!Rideables.WheelchairType.Is(x) || lp == null) continue;
                                float d = Vector3.Distance(x.transform.position, lp.transform.position);
                                if (d < bd) { bd = d; v = x; }
                            }
                            if (v == null) { Say("wheels: no wheelchair vehicle"); break; }
                            var rb = v.GetComponent<Rigidbody>();
                            Say("body pos " + v.transform.position.ToString("F2") + " euler " + v.transform.eulerAngles.ToString("F0") + " scale " + v.transform.lossyScale.ToString("F2")
                                + " mass " + rb.mass + " kin " + rb.isKinematic + " com " + rb.centerOfMass.ToString("F2") + " sim " + v.IsPhysicallySimulated + " speed " + v.Speed_Kmh.ToString("F1"));
                            foreach (var wc in v.GetComponentsInChildren<WheelCollider>(true))
                            {
                                // GetGroundHit kills the IL2CPP process on a grounded wheel (WheelHit out struct); isGrounded is enough here.
                                bool g = wc.isGrounded;
                                wc.GetWorldPose(out var wp, out var wq);
                                Say(wc.name + " on " + wc.gameObject.name + " en " + wc.enabled + " r " + wc.radius + " lossy " + wc.transform.lossyScale.ToString("F3")
                                    + " local " + v.transform.InverseTransformPoint(wc.transform.position).ToString("F3") + " pose " + v.transform.InverseTransformPoint(wp).ToString("F3")
                                    + " grounded " + g + " motor " + wc.motorTorque.ToString("F0") + " brake " + wc.brakeTorque.ToString("F0") + " rpm " + wc.rpm.ToString("F0")
                                    + " sprung " + wc.sprungMass.ToString("F0") + " kg fwd stiff " + wc.forwardFriction.stiffness.ToString("F2")
                                    + " ext " + wc.forwardFriction.extremumSlip.ToString("F2") + "/" + wc.forwardFriction.extremumValue.ToString("F2"));
                            }
                            foreach (var c in v.GetComponentsInChildren<Collider>(false))
                                if (c.enabled && !c.isTrigger && c.As<WheelCollider>() == null) Say("solid " + c.name + " " + Compat.Interop.TypeName(c) + " bounds " + c.bounds.center.ToString("F2") + " ext " + c.bounds.extents.ToString("F2"));
                        }
                        break;
                    case "move":
                        {
                            // move <yaw deg> [metres]: turn the nearest wheelchair vehicle in place, then set it down
                            // that far along its new facing - for a clear run when a prop stands facing a wall.
                            var lp = Player.Local;
                            LandVehicle v = null; float bd = float.MaxValue;
                            foreach (var x in UnityEngine.Object.FindObjectsOfType<LandVehicle>())
                            {
                                if (!Rideables.WheelchairType.Is(x) || lp == null) continue;
                                float d = Vector3.Distance(x.transform.position, lp.transform.position);
                                if (d < bd) { bd = d; v = x; }
                            }
                            if (v == null) { Say("move: no wheelchair vehicle"); break; }
                            float yaw = parts.Length > 2 ? float.Parse(parts[2], CultureInfo.InvariantCulture) : 180f;
                            float metres = parts.Length > 3 ? float.Parse(parts[3], CultureInfo.InvariantCulture) : 0f;
                            var rb = v.GetComponent<Rigidbody>();
                            Quaternion rot = Quaternion.Euler(0f, v.transform.eulerAngles.y + yaw, 0f);
                            Vector3 pos = v.transform.position + rot * Vector3.forward * metres + Vector3.up * 0.05f;
                            rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                            v.transform.SetPositionAndRotation(pos, rot);
                            rb.position = pos; rb.rotation = rot;
                            Say("move: net " + Net.Ids.Of(v) + " now at " + pos.ToString("F1") + " facing " + rot.eulerAngles.y.ToString("F0"));
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
                    case "cam":
                        Say(LookCam.Set(parts));
                        break;
                    case "drive":
                    case "pushin":
                        {
                            // drive|pushin <forward> <steer> [seconds]: stands in for the keys of the driver or the pusher.
                            float f = parts.Length > 2 ? float.Parse(parts[2], CultureInfo.InvariantCulture) : 1f;
                            float st = parts.Length > 3 ? float.Parse(parts[3], CultureInfo.InvariantCulture) : 0f;
                            float sec = parts.Length > 4 ? float.Parse(parts[4], CultureInfo.InvariantCulture) : 3f;
                            if (sub == "drive") { Rideables.Push.DebugDrive = new Vector2(f, st); Rideables.Push.DebugUntil = Time.time + sec; }
                            else { Rideables.Push.DebugPush = new Vector2(f, st); Rideables.Push.DebugPushUntil = Time.time + sec; }
                            Say(sub + ": forward " + f + " steer " + st + " for " + sec + " s");
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
                    case "sit":
                        {
                            // Sit on the nearest wheelchair prop, as if Sit had been pressed on it.
                            var lp = Player.Local;
                            if (lp == null) { Say("sit: no local player"); break; }
                            Transform best = null; float bd = float.MaxValue;
                            var props = SitDemo.FindChairs().FindAll(c => c.GetComponentInParent<LandVehicle>() == null);
                            if (parts.Length > 3 && parts[2] == "tp" && int.TryParse(parts[3], out int ti) && ti >= 0 && ti < props.Count)
                            {
                                // Stand 1.6 m in front of prop ti first; the Sit then comes from there.
                                best = props[ti];
                                Vector3 fwd = Vector3.ProjectOnPlane(best.forward, Vector3.up).normalized;
                                PlayerSingleton<PlayerMovement>.Instance.Teleport(best.position + fwd * 1.6f + Vector3.up * 0.1f, true);
                                bd = 1.6f;
                            }
                            else foreach (var c in props)
                            {
                                float d = Vector3.Distance(c.position, lp.transform.position);
                                if (d < bd) { bd = d; best = c; }
                            }
                            if (best == null) { Say("sit: no wheelchair prop"); break; }
                            Net.ChassisNet.ToHost("req.sit", Rideables.PropSwap.KeyOf(best), Net.Ids.Of(lp).ToString());
                            Say("sit: asked for " + SitDemo.Path(best) + " at " + bd.ToString("F1") + " m");
                        }
                        break;
                    case "push":
                        {
                            // Push the nearest occupied wheelchair, as if Push had been pressed on its handles.
                            var lp = Player.Local;
                            if (lp == null) { Say("push: no local player"); break; }
                            if (parts.Length > 2 && parts[2] == "off") { Net.ChassisNet.ToHost("req.push", parts.Length > 3 ? parts[3] : "-1", Net.Ids.Of(lp).ToString(), "0"); Say("push: release asked"); break; }
                            LandVehicle best = null; float bd = float.MaxValue;
                            foreach (var v in UnityEngine.Object.FindObjectsOfType<LandVehicle>())
                            {
                                if (!Rideables.WheelchairType.Is(v)) continue;
                                float d = Vector3.Distance(v.transform.position, lp.transform.position);
                                if (d < bd) { bd = d; best = v; }
                            }
                            if (best == null) { Say("push: no wheelchair vehicle"); break; }
                            // fake: an empty chair counts as occupied, so one player can test pushing alone.
                            if (parts.Length > 2 && parts[2] == "fake") { best.IsOccupied = true; Say("push: net " + Net.Ids.Of(best) + " marked occupied"); }
                            Net.ChassisNet.ToHost("req.push", Net.Ids.Of(best).ToString(), Net.Ids.Of(lp).ToString(), "1");
                            Say("push: asked for net " + Net.Ids.Of(best) + " at " + bd.ToString("F1") + " m");
                        }
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
                    case "chairs":
                    {
                        var chairs = SitDemo.FindChairs();
                        for (int i = 0; i < chairs.Count; i++) Say(i + ": " + SitDemo.Path(chairs[i]) + " at " + chairs[i].position.ToString("F1"));
                        Say("chairs: " + chairs.Count);
                        break;
                    }

                    case "demo":
                        Demo(parts);
                        break;

                    case "profile":
                        ProfileCommands.Run(parts);
                        break;

                    case "car":
                        VehicleCommands.Run(parts);
                        break;

                    case "interior":
                    {
                        string op = parts.Length > 2 ? parts[2].ToLowerInvariant() : "";
                        if (op == "on") { Interior.BaseInterior.Enabled = true; Say("interior on - vanilla cars get the base interior within a second"); }
                        else if (op == "off") { Interior.BaseInterior.Enabled = false; Say("interior off, " + Interior.BaseInterior.RemoveAll() + " removed"); }
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

                    case "spawn":
                    {
                        var local = Player.Local;
                        if (local == null) { Say("spawn: no local player"); break; }
                        Vector3 fwd = Vector3.ProjectOnPlane(local.transform.forward, Vector3.up).normalized;
                        var lv = Rideables.WheelchairSpawn.Spawn(local.transform.position + fwd * 2f + Vector3.up * 0.3f, Quaternion.LookRotation(fwd, Vector3.up), "console");
                        Say(lv != null ? "spawned wheelchair net " + Net.Ids.Of(lv) : "spawn failed, see the log");
                        break;
                    }

                    case "despawn":
                    {
                        int n = 0;
                        foreach (var lv in UnityEngine.Object.FindObjectsOfType<LandVehicle>())
                            if (Rideables.WheelchairType.Is(lv)) { Rideables.WheelchairSpawn.Despawn(lv, "console"); n++; }
                        Say("despawned " + n);
                        break;
                    }

                    case "tree":
                    {
                        // Hierarchy of a wheelchair prop (index from 'chairs') with local poses, components and collider bounds.
                        var chairs = SitDemo.FindChairs();
                        int i = parts.Length > 2 && int.TryParse(parts[2], out int ci) ? ci : 0;
                        if (i >= chairs.Count) { Say("tree: no chair " + i); break; }
                        Tree(chairs[i], chairs[i], 0);
                        break;
                    }

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
        private static LandVehicle Target(string id)
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

        private static void Demo(string[] p)
        {
            string op = p.Length > 2 ? p[2].ToLower() : "";
            switch (op)
            {
                case "":
                    Say(SitDemo.Start(false));
                    break;
                case "tp":
                    Say(SitDemo.Start(true, p.Length > 3 && int.TryParse(p[3], out int idx) ? idx : -1));
                    break;
                case "off":
                    Say(SitDemo.Stop());
                    break;
                case "state":
                    Say(SitDemo.Describe());
                    break;
                case "seat":
                    SitDemo.SeatOffset = new Vector3(F(p, 3, SitDemo.SeatOffset.x), F(p, 4, SitDemo.SeatOffset.y), F(p, 5, SitDemo.SeatOffset.z));
                    SitDemo.SeatYaw = F(p, 6, SitDemo.SeatYaw);
                    Say("seat " + SitDemo.SeatOffset.ToString("F2") + " yaw " + SitDemo.SeatYaw.ToString("F0"));
                    break;
                case "foot":
                    SitDemo.FootOffset = V(p, 3, SitDemo.FootOffset);
                    SitDemo.FootRotationWeight = Mathf.Clamp01(F(p, 6, SitDemo.FootRotationWeight));
                    SitDemo.ApplyGripWeights();
                    Say("foot " + SitDemo.FootOffset.ToString("F2") + " rotW " + SitDemo.FootRotationWeight.ToString("F2"));
                    break;
                case "comps":
                    foreach (var line in SitDemo.Components()) Say(line);
                    break;
                case "probe":
                    Say(SitDemo.StartProbe((int)F(p, 3, 120f)));
                    break;
                case "footrot":
                    SitDemo.FootRotation = V(p, 3, SitDemo.FootRotation);
                    Say("foot rotation " + SitDemo.FootRotation.ToString("F0"));
                    break;
                case "knee":
                    SitDemo.KneeGoal = V(p, 3, SitDemo.KneeGoal);
                    Say("knee goal " + SitDemo.KneeGoal.ToString("F2"));
                    break;
                case "elbow":
                    SitDemo.ElbowGoal = V(p, 3, SitDemo.ElbowGoal);
                    Say("elbow goal " + SitDemo.ElbowGoal.ToString("F2"));
                    break;
                case "lean":
                    SitDemo.SpineGoal = V(p, 3, SitDemo.SpineGoal);
                    SitDemo.SpineWeight = Mathf.Clamp01(F(p, 6, SitDemo.SpineWeight));
                    Say("lean " + SitDemo.SpineGoal.ToString("F2") + " w " + SitDemo.SpineWeight.ToString("F2"));
                    break;
                case "scale":
                    Say(SitDemo.SetScale(F(p, 3, SitDemo.ChairScale)));
                    break;
                case "parts":
                    foreach (var line in SitDemo.Parts()) Say(line);
                    break;
                case "feet":
                    Say(SitDemo.Feet());
                    break;
                case "mark":
                    if (p.Length > 3 && p[3].ToLower() == "clear") Say(SitDemo.ClearMarks());
                    else if (p.Length > 3 && p[3].ToLower() == "bones") Say(SitDemo.MarkBones(F(p, 4, 0.04f)));
                    else Say(SitDemo.Mark(V(p, 3, Vector3.zero), F(p, 6, 0.03f)));
                    break;
                case "fit":
                    Say(SitDemo.Fit(F(p, 3, 0.09f)));
                    break;
                case "grip":
                    SitDemo.GripOffset = new Vector3(F(p, 3, SitDemo.GripOffset.x), F(p, 4, SitDemo.GripOffset.y), F(p, 5, SitDemo.GripOffset.z));
                    SitDemo.GripRotationWeight = Mathf.Clamp01(F(p, 6, SitDemo.GripRotationWeight));
                    SitDemo.ApplyGripWeights();
                    Say("grip " + SitDemo.GripOffset.ToString("F2") + " rotW " + SitDemo.GripRotationWeight.ToString("F2"));
                    break;
                case "cam":
                {
                    string mode = p.Length > 3 ? p[3].ToLower() : "out";
                    Say(SitDemo.SetCamera(mode, F(p, 4, mode == "fp" ? 10f : 2.6f), F(p, 5, mode == "fp" ? 0.05f : 35f), F(p, 6, 1.2f)));
                    break;
                }
                default:
                    Say("demo: unknown '" + op + "', try 'chassis help'");
                    break;
            }
        }

        private static void Tree(Transform root, Transform t, int depth)
        {
            string comps = "";
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                string n = Compat.Interop.TypeName(c);
                if (n == "Transform") continue;
                var col = c.As<Collider>();
                if (col != null)
                {
                    Bounds b = col.bounds;
                    n += "[" + (col.isTrigger ? "trigger " : "") + "c " + root.InverseTransformPoint(b.center).ToString("F2") + " s " + root.InverseTransformVector(b.size).ToString("F2") + " layer " + LayerMask.LayerToName(t.gameObject.layer) + "]";
                }
                comps += " " + n;
            }
            Say(new string(' ', depth * 2) + t.name + " lp " + t.localPosition.ToString("F3") + " le " + t.localEulerAngles.ToString("F0") + " ls " + t.localScale.ToString("F3")
                + (depth == 0 ? " lossy " + t.lossyScale.ToString("F3") : " inRoot " + root.InverseTransformPoint(t.position).ToString("F3")) + " |" + comps);
            for (int k = 0; k < t.childCount; k++) Tree(root, t.GetChild(k), depth + 1);
        }

        private static Vector3 V(string[] p, int i, Vector3 fallback) =>
            new Vector3(F(p, i, fallback.x), F(p, i + 1, fallback.y), F(p, i + 2, fallback.z));

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
