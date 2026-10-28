#if DEBUG
using System;
using System.Globalization;
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
            Say("car go [throttle] [steer] [s] - drive through the vanilla control override, -1..1 each (driver)");
            Say("car slide [km/h] [deg] [s]   - hold the body at a speed turned off its facing, for tyre slip and drift smoke");
            Say("car wheels                   - drift state, smoke and smoke colour per wheel");
            Say("car honk [flash]             - horn (key G)");
            Say("car hear [d1,d2,...]         - plays the horn from each distance and reports the level at the listener");
            Say("car blink l|r|off            - indicator (arrow keys)");
            Say("car lock on|off              - lock or unlock (owner only)");
            Say("car part <name> open|close   - lid from the profile");
            Say("car side front|rear          - which end the storage is opened from");
            Say("car rain on|off|auto         - force rain for the wipers");
            Say("car assign <code>|off        - give the vehicle another profile");
            Say("car owners                   - every owner record this peer knows");
            Say("car owner <player code>      - give the vehicle to another player (host)");
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
                        + " speed " + lv.Speed_Kmh.ToString("F1") + " steer " + lv.CurrentSteerAngle.ToString("F1")
                        + " at " + lv.transform.position.ToString("F1") + " yaw " + lv.transform.eulerAngles.y.ToString("F0"));
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
#if IL2CPP
                case "hear":
                    MelonLoader.MelonCoroutines.Start(Hear(lv, arg));
                    Say("car hear: started");
                    break;
#endif
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
                case "owner":
                    if (arg.Length == 0) { Say("car owner <player code>"); break; }
                    Say(ChassisApi.SetOwner(lv, arg) ? "car owner: " + Registry.Name(lv) + " now belongs to " + arg : "car owner: refused, see the log");
                    break;
                case "go":
                    {
                        if (!lv.LocalPlayerIsDriver) { Say("car go: sit in the driver seat first ('chassis car enter')"); break; }
                        float throttle = Arg(p, 3, 1f), steer = Arg(p, 4, 0f), sec = Arg(p, 5, 3f);
                        MelonLoader.MelonCoroutines.Start(Go(lv, Mathf.Clamp(throttle, -1f, 1f), Mathf.Clamp(steer, -1f, 1f), sec));
                        Say("car go: " + Registry.Name(lv) + " throttle " + throttle.ToString("0.##", CultureInfo.InvariantCulture) + " steer " + steer.ToString("0.##", CultureInfo.InvariantCulture) + " for " + sec.ToString("0.#", CultureInfo.InvariantCulture) + " s");
                        break;
                    }
                case "slide":
                    {
                        float kmh = Arg(p, 3, 40f), angle = Arg(p, 4, 30f), sec = Arg(p, 5, 3f);
                        MelonLoader.MelonCoroutines.Start(Slide(lv, kmh / 3.6f, angle, sec));
                        Say("car slide: " + Registry.Name(lv) + " held at " + kmh.ToString("0", CultureInfo.InvariantCulture) + " km/h, " + angle.ToString("0", CultureInfo.InvariantCulture) + " deg off its facing, for " + sec.ToString("0.#", CultureInfo.InvariantCulture) + " s");
                        break;
                    }
                case "wheels":
                    if (lv.wheels == null) { Say("car wheels: " + Registry.Name(lv) + " has no wheel list"); break; }
                    foreach (var w in lv.wheels)
                    {
                        if (w == null) continue;
                        var ps = w.DriftParticles;
                        Say("car wheels: " + w.gameObject.name + " drifting " + w.IsDrifting + " for " + w.DriftTime.ToString("F2", CultureInfo.InvariantCulture) + " s, smoke "
                            + (ps == null ? "none" : (ps.isPlaying ? "playing" : "stopped") + " with " + ps.particleCount + " particle(s), colour #" + ColorUtility.ToHtmlStringRGB(ps.main.startColor.color)));
                    }
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

        private static float Arg(string[] p, int i, float fallback) =>
            p.Length > i && float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : fallback;

        // Drives through the vanilla control override, so the wheels get real torque and steer, slip included.
        private static System.Collections.IEnumerator Go(LandVehicle lv, float throttle, float steer, float seconds)
        {
            float until = Time.time + seconds;
            Vector3 start = lv.transform.position;
            float top = 0f;
            while (lv != null && Time.time < until)
            {
                lv.overrideControls = true;
                lv.throttleOverride = throttle;
                lv.steerOverride = steer;
                top = Mathf.Max(top, Mathf.Abs(lv.Speed_Kmh));
                yield return null;
            }
            if (lv == null) { Core.Log.Warning("[Console] car go ended: the vehicle is gone."); yield break; }
            lv.overrideControls = false;
            lv.throttleOverride = 0f;
            lv.steerOverride = 0f;
            Core.Log.Msg("[Console] car go ended after " + Vector3.Distance(start, lv.transform.position).ToString("F1", CultureInfo.InvariantCulture) + " m, top " + top.ToString("F0", CultureInfo.InvariantCulture) + " km/h.");
        }

        // Holds the body at a speed along a heading turned off its facing, so the tyres slip sideways and the vanilla
        // drift check fires without a long run-up.
        private static System.Collections.IEnumerator Slide(LandVehicle lv, float metresPerSecond, float angle, float seconds)
        {
            float until = Time.time + seconds;
            int smokeFrames = 0, frames = 0;
            while (lv != null && Time.time < until)
            {
                var rb = lv.GetComponent<Rigidbody>();
                if (rb == null) { Core.Log.Warning("[Console] car slide stopped: the vehicle has no Rigidbody."); yield break; }
                Vector3 v = Quaternion.Euler(0f, angle, 0f) * lv.transform.forward * metresPerSecond;
                rb.velocity = new Vector3(v.x, rb.velocity.y, v.z);
                frames++;
                if (lv.wheels != null)
                    foreach (var w in lv.wheels)
                        if (w != null && w.DriftParticles != null && w.DriftParticles.isPlaying) { smokeFrames++; break; }
                yield return null;
            }
            if (lv == null) { Core.Log.Warning("[Console] car slide ended: the vehicle is gone."); yield break; }
            Core.Log.Msg("[Console] car slide ended: drift smoke played in " + smokeFrames + " of " + frames + " frame(s).");
        }

#if IL2CPP
        /// <summary>
        /// Moves the listener away from the vehicle step by step and plays the horn locally at each distance. The level
        /// is the loudest RMS of the mixed output during the honk minus the quiet second before it, so wind and traffic
        /// do not count as horn.
        /// </summary>
        private static System.Collections.IEnumerator Hear(LandVehicle lv, string list)
        {
            var ci = CultureInfo.InvariantCulture;
            var steps = new System.Collections.Generic.List<float>();
            foreach (var part in (list.Length > 0 ? list : "5,15,30,60,100,150,250,400").Split(','))
                if (float.TryParse(part, NumberStyles.Float, ci, out float d)) steps.Add(d);
            var move = PlayerSingleton<PlayerMovement>.Instance;
            Vector3 home = Player.Local.transform.position;
            Vector3 car = lv.transform.position;
            int id = Chassis.Net.Ids.Of(lv);
            var buf = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float>(1024);
            foreach (float d in steps)
            {
                Vector3 at = car + Vector3.right * d;
                if (Physics.Raycast(at + Vector3.up * 300f, Vector3.down, out RaycastHit hit, 600f, ~0, QueryTriggerInteraction.Ignore)) at = hit.point;
                move.Teleport(at + Vector3.up * 0.2f, true);
                float quiet = 0f, loud = 0f;
                float end = Time.realtimeSinceStartup + 1f;
                while (Time.realtimeSinceStartup < end) { quiet = Mathf.Max(quiet, Rms(buf)); yield return null; }
                Horn.OnHonk(id, false);
                end = Time.realtimeSinceStartup + 0.8f;
                float lowest = float.MaxValue;
                while (Time.realtimeSinceStartup < end)
                {
                    loud = Mathf.Max(loud, Rms(buf));
                    var playing = lv.transform.Find("ChassisHorn")?.GetComponent<AudioLowPassFilter>();
                    if (playing != null) lowest = Mathf.Min(lowest, playing.cutoffFrequency);
                    yield return null;
                }
                var listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
                float real = listener != null ? Vector3.Distance(listener.transform.position, car) : -1f;
                var hs = lv.transform.Find("ChassisHorn")?.GetComponent<AudioSource>();
                var lp = hs != null ? hs.GetComponent<AudioLowPassFilter>() : null;
                string curve = hs == null ? "no source" : hs.rolloffMode == AudioRolloffMode.Custom
                    ? "gain " + hs.GetCustomCurve(AudioSourceCurveType.CustomRolloff).Evaluate(Mathf.Clamp01(real / hs.maxDistance)).ToString("F3", ci)
                    : hs.rolloffMode.ToString();
                if (lp != null) curve += ", cutoff while playing " + lowest.ToString("F0", ci) + " Hz";
                Say("car hear: " + d.ToString("F0", ci) + " m (listener " + real.ToString("F0", ci) + " m) " + curve + ", horn " + Mathf.Max(0f, loud - quiet).ToString("F4", ci)
                    + " (peak " + loud.ToString("F4", ci) + ", quiet " + quiet.ToString("F4", ci) + ")");
            }
            move.Teleport(home, true);
            var src = lv.transform.Find("ChassisHorn")?.GetComponent<AudioSource>();
            if (src != null)
                Say("car hear: source " + src.rolloffMode + " " + src.minDistance.ToString("F0", ci) + "-" + src.maxDistance.ToString("F0", ci)
                    + " m, blend " + src.spatialBlend.ToString("F2", ci) + ", at " + src.transform.position.ToString("F1"));
            Say("car hear: done");
        }

        private static float Rms(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> buf)
        {
            AudioListener.GetOutputData(buf, 0);
            double sum = 0;
            for (int i = 0; i < buf.Length; i++) sum += buf[i] * buf[i];
            return (float)Math.Sqrt(sum / buf.Length);
        }
#endif

        private static void Say(string line) => DebugConsolePatch.Say(line);
    }
}
#endif
