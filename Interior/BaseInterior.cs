using System;
using System.Collections.Generic;
using Chassis.Api;
using Chassis.Vehicles;

namespace Chassis.Interior
{
    /// <summary>
    /// The base interior for the vanilla cars, which have none: seats, a dashboard and a steering wheel from simple
    /// shapes, the inside of the body shell drawn as a dark lining, and tinted see-through windows in place of the
    /// opaque ones. Built on every peer for each vehicle whose profile has an <see cref="InteriorProfile"/>.
    ///
    /// A vanilla car gets a fitted profile the first time one of its kind is seen: the window band of the body mesh
    /// gives the belt line, the roof and the windscreen; the vanilla seat transforms give where each seat stands.
    /// A JSON file for the code replaces the fitted values, as for any profile.
    /// </summary>
    internal static class BaseInterior
    {
        internal const string RootName = "Chassis_Interior";
        internal const string WheelName = "Chassis_Wheel";
        private const string LiningName = "Chassis_Lining";

        /// <summary>The vanilla cars, by vehicle code.</summary>
        internal static readonly HashSet<string> VanillaCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hotbox", "cheetah", "dinkler", "hounddog", "shitbox", "shitbox_police", "bruiser", "bruiser_police", "veeper",
        };

        /// <summary>Vanilla bodies whose cabin is closed by a lid at the belt line.</summary>
        private static readonly HashSet<string> LiddedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cheetah", "shitbox", "shitbox_police" };

        /// <summary>
        /// Lowest cushion top of a fitted seat above the lowest point of the body. Below zero on purpose: the hips rest
        /// about 0.11 m above the cushion, so their underside still stays in the car, and the head 1.07 m above it stays
        /// under the roof of the low cabins. The debug console tunes it live.
        /// </summary>
        internal static float CushionAboveUnderside = -0.08f;

        /// <summary>Lowest ankle above the lowest point of the body, so the feet stay in the car; the knees bend up instead.</summary>
        private const float FeetAboveUnderside = 0.10f;

        /// <summary>Hips above the cushion top for the fitted seats.</summary>
        private const float HipHeight = 0.08f;

        /// <summary>The fitted kit gives way where it would reach out of the body. The debug console switches it, to compare.</summary>
        internal static bool FitToShell = true;

        /// <summary>Vanilla cars get the base interior. Set from the preference at start; the debug console switches it live.</summary>
        internal static bool Enabled;

        /// <summary>Reads <c>BaseInterior</c> from the Chassis preferences (on unless the player turned it off).</summary>
        internal static void LoadSetting()
        {
            try
            {
                var cat = MelonLoader.MelonPreferences.CreateCategory("Chassis");
                var entry = cat.CreateEntry("BaseInterior", true, "Base interior",
                    "Seats, dashboard, steering wheel and tinted glass for the vanilla cars, and visible NPC drivers.");
                Enabled = entry.Value;
                Core.Log.Msg("[Interior] base interior " + (Enabled ? "on" : "off") + " (preference BaseInterior).");
            }
            catch (Exception e)
            {
                Enabled = true;
                Core.Log.Warning("[Interior] reading the BaseInterior preference failed - the base interior stays on: " + e.Message);
            }
        }

        private sealed class Built
        {
            internal LandVehicle V;
            internal GameObject Root;
            internal readonly List<Renderer> Glazed = new List<Renderer>();
            internal readonly List<Material[]> Original = new List<Material[]>();
            internal readonly List<GameObject> Linings = new List<GameObject>();
            internal readonly List<MeshFilter> Opened = new List<MeshFilter>();
            internal readonly List<Mesh> Closed = new List<Mesh>();
        }

        private static readonly Dictionary<int, Built> _built = new Dictionary<int, Built>();
        private static readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private static readonly HashSet<string> _fitFailed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<int> _gone = new List<int>();
        private static readonly Dictionary<string, Func<LandVehicle, GameObject>> _factories = new Dictionary<string, Func<LandVehicle, GameObject>>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<ChassisProfile> _fitted = new HashSet<ChassisProfile>();
        private static readonly HashSet<string> _fittedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Bounds> _bands = new Dictionary<string, Bounds>(StringComparer.OrdinalIgnoreCase);

        // ---- interiors from mods -----------------------------------------------------------------------------------

        internal static void SetFactory(string code, Func<LandVehicle, GameObject> build) => _factories[code] = build;

        internal static bool RemoveFactory(string code) => !string.IsNullOrEmpty(code) && _factories.Remove(code);

        /// <summary>A profile Chassis fitted to a vanilla car, whose gauges and switches name the base kit.</summary>
        internal static bool IsFitted(ChassisProfile p) => p != null && _fitted.Contains(p);

        /// <summary>Fits every vanilla code again from one of its cars and rebuilds their interiors. Returns the codes fitted.</summary>
        internal static int Refit()
        {
            int n = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var lv in Registry.All)
            {
                if (lv == null) continue;
                string code = ChassisApi.CodeOf(lv);
                if (!VanillaCodes.Contains(code) || !seen.Add(code)) continue;
                var old = ChassisApi.Get(code);
                if (old != null && !_fittedCodes.Contains(code)) { Core.Log.Msg("[Interior] refit: '" + code + "' has a profile of its own - kept."); continue; }
                if (old != null) _fitted.Remove(old);
                _fitFailed.Remove(code);
                FitVanilla(lv);
                Rebuild(code);
                n++;
            }
            return n;
        }

        /// <summary>Takes the interior out of every vehicle of this code; the next tick builds it again.</summary>
        internal static void Rebuild(string code)
        {
            _gone.Clear();
            foreach (var kv in _built)
                if (kv.Value.V != null && string.Equals(ChassisApi.CodeOf(kv.Value.V), code, StringComparison.OrdinalIgnoreCase)) _gone.Add(kv.Key);
            foreach (int k in _gone)
            {
                Remove(_built[k]);
                _built.Remove(k);
            }
            _next = 0f;
            if (_gone.Count > 0) Core.Log.Msg("[Interior] rebuilding " + _gone.Count + " interior(s) of '" + code + "'.");
        }

        /// <summary>Takes the interior out of one car so the next tick builds it again; false when it has none built.</summary>
        internal static bool RebuildVehicle(LandVehicle lv)
        {
            if (lv == null || !_built.TryGetValue(lv.GetInstanceID(), out var b)) return false;
            Remove(b);
            _built.Remove(lv.GetInstanceID());
            _next = 0f;
            Core.Log.Msg("[Interior] rebuilding the interior of " + Registry.Name(lv) + ".");
            return true;
        }

        /// <summary>
        /// The vehicle's cockpit is in place for gauges and switches to bind: it has no base interior, or the interior is
        /// built. Before that, their paths would not be found yet.
        /// </summary>
        internal static bool Ready(LandVehicle lv, ChassisProfile profile)
        {
            if (profile?.Interior == null || !Enabled) return true;
            return _built.TryGetValue(lv.GetInstanceID(), out var b) && b.Root != null;
        }

        // ---- per second, from the registry -------------------------------------------------------------------------

        private static float _next;

        internal static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            if (_built.Count > 0)
            {
                _gone.Clear();
                foreach (var kv in _built) if (kv.Value.V == null) _gone.Add(kv.Key);
                foreach (int k in _gone) _built.Remove(k);
            }
            if (!Enabled) return;
            foreach (var lv in Registry.All)
            {
                if (lv == null || _built.ContainsKey(lv.GetInstanceID())) continue;
                string code = ChassisApi.CodeOf(lv);
                if (VanillaCodes.Contains(code) && ChassisApi.Get(code) == null && !_fitFailed.Contains(code)) FitVanilla(lv);
                var profile = ChassisApi.For(lv);
                if (profile?.Interior == null) continue;
                Build(lv, profile);
            }
        }

        // ---- fitting a vanilla car ---------------------------------------------------------------------------------

        /// <summary>The window band of a car in its root space, or false when its body has no window material.</summary>
        internal static bool WindowBand(LandVehicle lv, GlassProfile glass, out Bounds band)
        {
            band = default;
            bool any = false;
            Transform root = lv.transform;
            foreach (var r in lv.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null || r.gameObject.name == LiningName) continue;
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length && i < mesh.subMeshCount; i++)
                {
                    if (!IsWindow(mats[i], glass)) continue;
                    Bounds b = mesh.GetSubMesh(i).bounds;
                    foreach (Vector3 c in Corners(b))
                    {
                        Vector3 p = root.InverseTransformPoint(r.transform.TransformPoint(c));
                        if (!any) { band = new Bounds(p, Vector3.zero); any = true; }
                        else band.Encapsulate(p);
                    }
                }
                // The lower LODs repeat the same windows.
                if (any) break;
            }
            return any;
        }

        /// <summary>
        /// A profile for a vanilla car: one kit seat per vanilla seat (a bench where two sit side by side behind the
        /// front row), a dashboard below the windscreen and a steering wheel ahead of the driver.
        /// </summary>
        private static void FitVanilla(LandVehicle lv)
        {
            string code = ChassisApi.CodeOf(lv);
            try
            {
                var glass = new GlassProfile();
                if (!WindowBand(lv, glass, out Bounds band))
                {
                    _fitFailed.Add(code);
                    Core.Log.Warning("[Interior] " + code + " has no window material - it keeps the vanilla cabin.");
                    return;
                }
                var seats = lv.Seats;
                if (seats == null || seats.Length == 0)
                {
                    _fitFailed.Add(code);
                    Core.Log.Warning("[Interior] " + code + " has no seats - it keeps the vanilla cabin.");
                    return;
                }
                Transform root = lv.transform;
                float belt = band.min.y, roof = band.max.y;
                // The avatars' heads are large for the vanilla cabins. The body sits low enough that the top of the head
                // and a tall hairstyle stay under the roof: they reach about 1.2 m above the cushion. First person looks
                // from the natural eye height, under the roof and at least just above the belt line.
                float cushion = Mathf.Min(belt + (roof - belt) * 0.45f - 0.70f, roof - 1.26f);
                // In the low cabins that rule puts the cushion so low that seat, hips and legs show under the car. The
                // cushion rises to just below the underside, and the feet below rise into the car further down.
                float underside = Underside(lv, root, Lod0(lv.GetComponentInChildren<LODGroup>(true)));
                float lowered = cushion;
                if (!float.IsNaN(underside)) cushion = Mathf.Max(cushion, underside + CushionAboveUnderside);
                float eyes = Mathf.Max(Mathf.Min(cushion + 0.86f, roof - 0.08f), belt + 0.19f);
                // These bodies close the cabin with a lid at the belt line. It is cut out of the body mesh; where that
                // fails, dashboard, wheel and eyes sit above it.
                bool lid = LiddedCodes.Contains(code);
                bool opened = lid && OpenShell(lv, glass, code, band, Lod0(lv.GetComponentInChildren<LODGroup>(true)), null) > 0;
                bool closed = lid && !opened;
                float lift = closed ? 0.16f : 0f;
                if (closed) eyes = Mathf.Max(eyes, Mathf.Min(belt + 0.30f, roof - 0.08f));
                float floor = cushion - 0.32f;

                var profile = new ChassisProfile { Code = code, Interior = new InteriorProfile { Glass = glass, OpenLid = lid } };
                var parts = profile.Interior.Parts;

                int driver = -1;
                float frontZ = float.MinValue;
                var local = new Vector3[seats.Length];
                for (int i = 0; i < seats.Length; i++)
                {
                    if (seats[i] == null) continue;
                    local[i] = root.InverseTransformPoint(seats[i].transform.position);
                    if (seats[i].isDriverSeat) driver = i;
                    frontZ = Mathf.Max(frontZ, local[i].z);
                }
                if (driver < 0) driver = 0;

                // Some vanilla seat transforms sit right at the windscreen; every row moves back until the front row
                // has room for a wheel and a chest behind the dashboard.
                float shift = Mathf.Min(0f, band.max.z - 0.7f - frontZ);
                for (int i = 0; i < seats.Length; i++) local[i].z += shift;
                frontZ += shift;

                // Steering wheel and dashboard, from the driver's place.
                Vector3 d = local[driver];
                float seatZ = d.z;
                // In the short cabins the windscreen starts barely 0.2 m ahead of where the wheel wants to be; the
                // wheel stays clear of the windscreen and the dashboard gets shallower instead of covering the wheel.
                float dashFront = band.max.z;
                Vector3 wheelAt = new Vector3(d.x, belt - 0.13f + lift, Mathf.Min(seatZ + 0.5f, dashFront - 0.18f));
                parts.Add(new KitPart { Kind = "wheel", Position = wheelAt, Rotation = new Vector3(22f, 0f, 0f), Size = new Vector3(0.17f, 0f, 0.3f), Color = new Color(0.12f, 0.12f, 0.13f) });
                float dashBack = wheelAt.z + 0.08f;
                var dash = new KitPart
                {
                    Kind = "dash",
                    Position = new Vector3(band.center.x, belt + 0.02f + lift, dashFront),
                    Size = new Vector3(Mathf.Max(0.6f, band.size.x - 0.12f), closed ? 0.2f : Mathf.Clamp(belt + 0.02f - floor - 0.3f, 0.2f, 0.45f), Mathf.Max(0.08f, dashFront - dashBack)),
                    Color = new Color(0.22f, 0.22f, 0.23f),
                };
                parts.Add(dash);

                // The kit stays inside the body. The windscreen often starts above the belt line and leans back over the
                // dashboard; the dashboard's front edge gives way, then its top. The rear window and the roof slope
                // down over the back row; the seat backs get shorter. The test runs against the body as read back
                // from the GPU, without the cabin lid that is cut out.
                _bands[code] = band;
                var shell = ShellFit.Of(lv, Lod0(lv.GetComponentInChildren<LODGroup>(true)), CabinCentre(band), lid ? band : (Bounds?)null, m => IsWindow(m, glass), out string shellReport);
                var fitNotes = new List<string>();
                if (shell == null) Core.Log.Warning("[Interior] " + code + ": the shell test cannot run (" + shellReport + ") - the kit keeps its formula sizes.");
                else if (FitToShell)
                {
                    float seen = belt - BeltMargin;
                    int before = OutsideCount(shell, dash, underside, seen), now = before;
                    float back0 = dash.Position.z, top0 = dash.Position.y;
                    while (now > 0 && dash.Size.z - DashStep >= 0.08f)
                    {
                        dash.Position -= new Vector3(0f, 0f, DashStep);
                        dash.Size -= new Vector3(0f, 0f, DashStep);
                        now = OutsideCount(shell, dash, underside, seen);
                    }
                    while (now > 0 && top0 - dash.Position.y < DashMaxDrop - 0.001f)
                    {
                        dash.Position -= new Vector3(0f, DashStep, 0f);
                        now = OutsideCount(shell, dash, underside, seen);
                    }
                    if (before > 0)
                        fitNotes.Add("dash " + before + " corner(s) outside, front moved back " + (back0 - dash.Position.z).ToString("F2") + ", top lowered " + (top0 - dash.Position.y).ToString("F2") + (now > 0 ? ", " + now + " still outside" : ""));
                }
                float dashTop = dash.Position.y;
                Vector3 gaugesAt = new Vector3(d.x, dashTop - 0.06f, dashBack);
                parts.Add(new KitPart { Kind = "gauges", Position = gaugesAt, Size = new Vector3(0.3f, 0.1f, 0.05f), Color = new Color(0.16f, 0.16f, 0.17f) });
                AddInstruments(profile, gaugesAt, wheelAt, new Vector3(band.center.x, dashTop, dashBack));

                // Seats: the front row one each, a row behind it as one bench.
                var done = new bool[seats.Length];
                for (int i = 0; i < seats.Length; i++)
                {
                    if (seats[i] == null || done[i]) continue;
                    bool front = local[i].z > frontZ - 0.3f;
                    if (front)
                    {
                        parts.Add(new KitPart { Kind = "seat", Position = new Vector3(local[i].x, cushion, local[i].z), Size = new Vector3(0.5f, 0.62f, 0.48f) });
                        done[i] = true;
                        continue;
                    }
                    float minX = local[i].x, maxX = local[i].x;
                    for (int j = i; j < seats.Length; j++)
                    {
                        if (seats[j] == null || done[j] || Mathf.Abs(local[j].z - local[i].z) > 0.3f) continue;
                        minX = Mathf.Min(minX, local[j].x); maxX = Mathf.Max(maxX, local[j].x);
                        done[j] = true;
                    }
                    parts.Add(new KitPart { Kind = "bench", Position = new Vector3((minX + maxX) * 0.5f, cushion, local[i].z), Size = new Vector3(maxX - minX + 0.55f, 0.62f, 0.48f) });
                }
                if (shell != null)
                {
                    foreach (var part in parts)
                    {
                        if (!FitToShell || (part.Kind != "seat" && part.Kind != "bench")) continue;
                        int before = OutsideCount(shell, part, underside, belt - BeltMargin), now = before;
                        float back0 = part.Size.y;
                        while (now > 0 && part.Size.y - BackStep >= BackMin - 0.001f)
                        {
                            part.Size -= new Vector3(0f, BackStep, 0f);
                            now = OutsideCount(shell, part, underside, belt - BeltMargin);
                        }
                        if (before > 0)
                            fitNotes.Add(part.Kind + " at z " + part.Position.z.ToString("F2") + " " + before + " corner(s) outside, back " + back0.ToString("F2") + " -> " + part.Size.y.ToString("F2") + (now > 0 ? ", " + now + " still outside" : ""));
                    }
                    // What still reaches out above the belt line after the fit, part by part.
                    var still = new List<string>();
                    foreach (var part in parts)
                    {
                        int n = OutsideCount(shell, part, underside, belt - BeltMargin);
                        if (n > 0) still.Add(part.Kind + (string.IsNullOrEmpty(part.Name) ? "" : " " + part.Name) + " " + n);
                    }
                    Core.Log.Msg("[Interior] " + code + " shell test (" + shellReport + "): " + (!FitToShell ? "fit off" : fitNotes.Count > 0 ? string.Join("; ", fitNotes) : "every part inside") + "." + (still.Count > 0 ? " Still outside: " + string.Join(", ", still) + "." : ""));
                    if (still.Count > 0) Core.Log.Warning("[Interior] " + code + ": " + still.Count + " kit part(s) still reach out of the body.");
                }

                float feetRaised = 0f;
                profile.Steering = new SteeringProfile { Path = WheelName, Axis = Vector3.forward, Ratio = 5f, Max = 170f };
                for (int i = 0; i < seats.Length; i++)
                {
                    if (seats[i] == null) continue;
                    Vector3 point = root.TransformPoint(new Vector3(local[i].x, cushion, local[i].z));
                    var seat = ChassisApi.FitSeat(lv, i, point, root, null, HipHeight);
                    seat.Eyes = new Vector3(seat.Eyes.x, eyes, seat.Eyes.z);
                    if (seat.Feet != null && !float.IsNaN(underside) && seat.Feet.Position.y < underside + FeetAboveUnderside)
                    {
                        float up = underside + FeetAboveUnderside - seat.Feet.Position.y;
                        seat.Feet.Position += Vector3.up * up;
                        seat.Feet.Knee += Vector3.up * up;
                        feetRaised = Mathf.Max(feetRaised, up);
                    }
                    profile.Seats.Add(seat);
                }
                // Hands on the wheel need the wheel's transform; the kit builds it, so the driver's grip is set here.
                var ds = profile.Seat(driver);
                if (ds != null)
                {
                    ds.Hands.Mode = "wheel";
                    ds.Hands.GripStart = wheelAt + Vector3.right * 0.17f;
                }

                _fitted.Add(profile);
                bool used = ChassisApi.Register(profile);
                if (used) _fittedCodes.Add(code);
                Core.Log.Msg("[Interior] fitted " + code + ": window band " + ProfileJson.F(band.min) + " .. " + ProfileJson.F(band.max) + ", cushion "
                    + cushion.ToString("F2") + (cushion > lowered ? " (raised " + (cushion - lowered).ToString("F2") + " above the underside " + underside.ToString("F2") + ")" : "") + (feetRaised > 0f ? ", feet raised " + feetRaised.ToString("F2") : "") + ", driver z " + seatZ.ToString("F2") + (shift < 0f ? " (moved back " + (-shift).ToString("F2") + ")" : "") + (opened ? ", cabin lid cut out" : closed ? ", above the cabin lid" : "") + ", wheel z " + wheelAt.z.ToString("F2") + ", dash " + (dashFront - dashBack).ToString("F2") + " m deep, " + parts.Count + " kit part(s), " + profile.Seats.Count + " seat(s)" + (used ? "." : " - a JSON file wins."));
            }
            catch (Exception e)
            {
                _fitFailed.Add(code);
                Core.Log.Error("[Interior] fitting " + code + " failed - it keeps the vanilla cabin: " + e);
            }
        }

        /// <summary>Where the shell test starts its segments: the middle of the window band, halfway up.</summary>
        private static Vector3 CabinCentre(Bounds band) => new Vector3(band.center.x, (band.min.y + band.max.y) * 0.5f, band.center.z);

        private const string MarksName = "Chassis_ShellMarks";

        /// <summary>
        /// Debug: tests the built kit of <paramref name="lv"/> against its body and, with <paramref name="mark"/>, puts a
        /// small red cube on every corner outside and a green one at the cabin centre. Without it, removes the cubes.
        /// </summary>
        internal static string Probe(LandVehicle lv, bool mark)
        {
            if (lv == null) return "no car";
            string code = ChassisApi.CodeOf(lv);
            var old = lv.transform.Find(MarksName);
            if (old != null) { old.SetParent(null, false); UnityEngine.Object.Destroy(old.gameObject); }
            if (!_built.TryGetValue(lv.GetInstanceID(), out var b) || b.Root == null) return code + ": no interior built";
            if (!_bands.TryGetValue(code, out var band)) return code + ": not fitted in this session, no window band";
            var shell = ShellFit.Of(lv, Lod0(lv.GetComponentInChildren<LODGroup>(true)), CabinCentre(band), LiddedCodes.Contains(code) ? band : (Bounds?)null, m => false, out string report);
            if (shell == null) return code + ": shell test cannot run (" + report + ")";
            Transform root = lv.transform;
            var found = new List<Vector3>();
            var per = new List<string>();
            foreach (var mf in b.Root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                int from = found.Count;
                shell.OutsidePoints(mf.sharedMesh, root.worldToLocalMatrix * mf.transform.localToWorldMatrix, found);
                int n = Keep(found, from, band.min.y - BeltMargin);
                if (n == 0) continue;
                per.Add(mf.gameObject.name + " " + n);
                var at = new List<string>();
                for (int i = from; i < found.Count; i++) at.Add(ProfileJson.F(found[i]));
                Core.Log.Msg("[Interior] probe " + code + " " + mf.gameObject.name + " outside at " + string.Join(" ", at));
            }
            if (mark)
            {
                var holder = new GameObject(MarksName).transform;
                holder.SetParent(root, false);
                Cube(holder, shell.Centre, new Color(0.1f, 0.9f, 0.2f), 0.06f);
                foreach (var p in found) Cube(holder, p, new Color(1f, 0.05f, 0.05f), 0.035f);
            }
            return code + ": " + report + ", " + found.Count + " kit corner(s) outside above the belt line" + (per.Count > 0 ? " - " + string.Join(", ", per) : "") + (mark ? " (red cubes)" : "");
        }

        /// <summary>Drops the points from <paramref name="from"/> on that lie at or below <paramref name="minY"/>; returns how many stay.</summary>
        private static int Keep(List<Vector3> points, int from, float minY)
        {
            var above = new List<Vector3>();
            for (int i = from; i < points.Count; i++) if (points[i].y > minY) above.Add(points[i]);
            points.RemoveRange(from, points.Count - from);
            points.AddRange(above);
            return above.Count;
        }

        private static void Cube(Transform parent, Vector3 at, Color c, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = Vector3.one * size;
            go.GetComponent<MeshRenderer>().sharedMaterial = Backlit(c);
        }

        /// <summary>Steps and limits of the shell fit: the dashboard moves in 2 cm steps and drops at most 12 cm; seat backs shrink in 4 cm steps to 0.30 m.</summary>
        private const float DashStep = 0.02f, DashMaxDrop = 0.12f, BackStep = 0.04f, BackMin = 0.30f;

        /// <summary>
        /// How many corners of a kit part lie outside the shell above <paramref name="minY"/>. Below the belt line the
        /// body is opaque: a cushion corner just under the floor or a pedestal in the door does not show.
        /// </summary>
        private static int OutsideCount(ShellFit shell, KitPart part, float underside, float minY = float.NegativeInfinity)
        {
            Mesh mesh = MeshOf(part, underside, out Vector3 at);
            if (mesh == null) return 0;
            try
            {
                var found = new List<Vector3>();
                shell.OutsidePoints(mesh, Matrix4x4.TRS(at, Quaternion.Euler(part.Rotation), Vector3.one), found);
                int n = 0;
                foreach (var p in found) if (p.y > minY) n++;
                return n;
            }
            finally { UnityEngine.Object.Destroy(mesh); }
        }

        /// <summary>Kit corners this far below the belt line still count: the windscreen and the side windows start there.</summary>
        private const float BeltMargin = 0.05f;

        /// <summary>The kit's instruments: two needles, three warning lamps, the light and hazard switches, the indicator stalk and the horn.</summary>
        private static void AddInstruments(ChassisProfile profile, Vector3 gauges, Vector3 wheel, Vector3 stack)
        {
            var parts = profile.Interior.Parts;
            // The dials of the gauges kit: 0.22 of its width either side, 0.55 of its height down, on its face.
            const float DialX = 0.066f, DialY = -0.055f;
            var needle = new Vector3(0.004f, 0.032f, 0.002f);
            var orange = new Color(0.95f, 0.32f, 0.08f);
            parts.Add(new KitPart { Kind = "needle", Name = SpeedNeedle, Position = gauges + new Vector3(-DialX, DialY, -0.017f), Size = needle, Color = orange });
            parts.Add(new KitPart { Kind = "needle", Name = RpmNeedle, Position = gauges + new Vector3(DialX, DialY, -0.017f), Size = needle, Color = orange });
            var dark = new Color(0.1f, 0.1f, 0.1f);
            var lamp = new Vector3(0.011f, 0.007f, 0.004f);
            parts.Add(new KitPart { Kind = "lamp", Name = "Chassis_LampLeft", Position = gauges + new Vector3(-0.013f, -0.03f, -0.014f), Size = lamp, Color = dark });
            parts.Add(new KitPart { Kind = "lamp", Name = "Chassis_LampRight", Position = gauges + new Vector3(0.013f, -0.03f, -0.014f), Size = lamp, Color = dark });
            parts.Add(new KitPart { Kind = "lamp", Name = "Chassis_LampLights", Position = gauges + new Vector3(0f, -0.08f, -0.014f), Size = lamp, Color = dark });

            // Light switch left of the wheel, hazard switch on the centre stack (backlit, so it shows in a dark cabin),
            // horn in the hub. The stalk leaves the column down and to the left, between the left forearm and the lower
            // spoke, and its pale tip reaches past the rim.
            parts.Add(new KitPart { Kind = "rocker", Name = LightSwitch, Position = new Vector3(wheel.x - 0.25f, gauges.y - 0.05f, gauges.z - 0.008f), Size = new Vector3(0.022f, 0.034f, 0.014f), Color = new Color(0.13f, 0.13f, 0.14f) });
            parts.Add(new KitPart { Kind = "rocker", Name = HazardSwitch, Position = new Vector3(stack.x, stack.y - 0.12f, stack.z - 0.09f), Size = new Vector3(0.042f, 0.03f, 0.014f), Color = new Color(0.9f, 0.1f, 0.06f) });
            var column = Quaternion.Euler(22f, 0f, 0f);
            parts.Add(new KitPart { Kind = "stalk", Name = IndicatorStalk, Position = wheel + column * new Vector3(-0.02f, -0.01f, 0.05f), Rotation = new Vector3(22f, 0f, 55f), Size = new Vector3(0.2f, 0.012f, 0f), Color = new Color(0.1f, 0.1f, 0.11f) });
            parts.Add(new KitPart { Kind = "button", Name = HornButton, Parent = WheelName, Position = wheel + column * new Vector3(0f, 0f, -0.008f), Rotation = new Vector3(22f, 0f, 0f), Size = new Vector3(0.036f, 0f, 0.01f), Color = new Color(0.12f, 0.12f, 0.13f) });

            bool mph = Chassis.Instruments.Sources.Mph;
            profile.Gauges.Add(new GaugeProfile { Name = "speed", Kind = "needle", Path = SpeedNeedle, Source = "speed", Min = 0f, Max = mph ? 80f : 130f });
            profile.Gauges.Add(new GaugeProfile { Name = "rpm", Kind = "needle", Path = RpmNeedle, Source = "rpm", Min = 0f, Max = 7000f });
            var green = new Color(0.25f, 1f, 0.3f);
            profile.Gauges.Add(new GaugeProfile { Name = "indicator_left", Kind = "lamp", Path = "Chassis_LampLeft", Source = "indicator_left", Color = green });
            profile.Gauges.Add(new GaugeProfile { Name = "indicator_right", Kind = "lamp", Path = "Chassis_LampRight", Source = "indicator_right", Color = green });
            profile.Gauges.Add(new GaugeProfile { Name = "lights", Kind = "lamp", Path = "Chassis_LampLights", Source = "lights", Color = new Color(0.3f, 0.65f, 1f) });

            profile.Controls.Add(new ControlProfile { Name = "lights", Label = "Lights", Kind = "toggle", Path = LightSwitch, Action = "lights", Source = "lights", Axis = Vector3.right, Angle = 24f });
            profile.Controls.Add(new ControlProfile { Name = "hazard", Label = "Hazard lights", Kind = "toggle", Path = HazardSwitch, Action = "hazard", Source = "hazard", Axis = Vector3.right, Angle = 20f });
            profile.Controls.Add(new ControlProfile { Name = "indicator", Label = "Indicator", Kind = "knob", Path = IndicatorStalk, Action = "indicator", Source = "indicator", Axis = Vector3.back, Angle = 24f, Min = -1f, Max = 1f, Step = 1f, Default = 0f });
            profile.Controls.Add(new ControlProfile { Name = "horn", Label = "Horn", Kind = "button", Path = HornButton, Action = "horn", Axis = Vector3.forward, Press = 0.006f });
        }

        internal const string SpeedNeedle = "Chassis_SpeedNeedle", RpmNeedle = "Chassis_RpmNeedle";
        internal const string LightSwitch = "Chassis_LightSwitch", HazardSwitch = "Chassis_HazardSwitch", IndicatorStalk = "Chassis_IndicatorStalk", HornButton = "Chassis_HornButton";

        // ---- building ----------------------------------------------------------------------------------------------

        private static void Build(LandVehicle lv, ChassisProfile profile)
        {
            var b = new Built { V = lv };
            _built[lv.GetInstanceID()] = b;
            try
            {
                Transform frame = Frames.Resolve(lv.transform, profile.Frame);
                if (frame == null) { Core.Log.Warning("[Interior] " + Registry.Name(lv) + ": frame '" + profile.Frame + "' not found - no interior."); return; }
                var ip = profile.Interior;
                LODGroup lod = lv.GetComponentInChildren<LODGroup>(true);
                var kitRenderers = new List<Renderer>();

                var old = frame.Find(RootName);
                if (old != null)
                {
                    // Out of the frame at once: Destroy waits for the end of the frame, and gauges bind by name meanwhile.
                    old.SetParent(null, false);
                    UnityEngine.Object.Destroy(old.gameObject);
                }
                b.Root = new GameObject(RootName);
                b.Root.transform.SetParent(frame, false);

                string source;
                float underside = float.NaN, raised = 0f;
                if (_factories.TryGetValue(profile.Code, out var factory))
                {
                    source = "mod interior";
                    GameObject made = null;
                    try { made = factory(lv); }
                    catch (Exception e) { Core.Log.Error("[Interior] the interior factory for '" + profile.Code + "' failed on " + Registry.Name(lv) + ": " + e); }
                    if (made != null)
                    {
                        made.transform.SetParent(b.Root.transform, false);
                        foreach (var r in made.GetComponentsInChildren<Renderer>(true)) if (r != null) kitRenderers.Add(r);
                    }
                    else Core.Log.Warning("[Interior] the interior factory for '" + profile.Code + "' gave nothing for " + Registry.Name(lv) + " - the cabin stays empty.");
                }
                else
                {
                    source = "kit";
                    int n = 0;
                    var placed = new List<KeyValuePair<KitPart, Transform>>();
                    underside = Underside(lv, frame, Lod0(lod));
                    foreach (var part in ip.Parts)
                    {
                        string k = (part.Kind ?? "").ToLowerInvariant();
                        if (!float.IsNaN(underside) && (k == "seat" || k == "bench")) raised = Mathf.Max(raised, underside + SeatAboveUnderside - part.Position.y);
                    }
                    foreach (var part in ip.Parts)
                    {
                        var r = Piece(b.Root.transform, part, profile.Code + "_" + n++, underside);
                        if (r == null) continue;
                        kitRenderers.Add(r);
                        placed.Add(new KeyValuePair<KitPart, Transform>(part, r.transform));
                    }
                    // A part can hang below another one, e.g. the horn button below the wheel, and moves with it.
                    foreach (var kv in placed)
                    {
                        if (string.IsNullOrEmpty(kv.Key.Parent)) continue;
                        var parent = b.Root.transform.Find(kv.Key.Parent);
                        if (parent == null) { Core.Log.Warning("[Interior] kit part '" + kv.Key.Name + "' hangs below '" + kv.Key.Parent + "', which is not in the kit - it stays where it is."); continue; }
                        kv.Value.SetParent(parent, true);
                    }
                }

                int opened = 0;
                if (ip.OpenLid && ip.Glass != null && WindowBand(lv, ip.Glass, out Bounds band))
                    opened = OpenShell(lv, ip.Glass, profile.Code, band, Lod0(lod), b);
                int glazed = ip.Glass != null ? Glaze(lv, ip.Glass, b) : 0;
                int lined = ip.Lining ? Line(lv, ip, b, kitRenderers, lod) : 0;
                AddToLod(lod, kitRenderers, Registry.Name(lv));
                // The steering wheel, needles and switches only exist from now on; their rigs look them up again.
                Vehicles.Parts.Forget(lv);
                Instruments.Gauges.Forget(lv);
                Instruments.Controls.Forget(lv);
                Core.Log.Msg("[Interior] " + Registry.Name(lv) + ": " + kitRenderers.Count + " " + source + " renderer(s), " + glazed + " window slot(s) tinted, " + lined + " shell(s) lined" + (ip.OpenLid ? ", " + opened + " opened" : "") + (float.IsNaN(underside) ? "" : ", underside y " + underside.ToString("F2")) + (raised > 0f ? ", seats raised " + raised.ToString("F2") + " m into the body" : "") + ".");
            }
            catch (Exception e) { Core.Log.Error("[Interior] building the interior of " + Registry.Name(lv) + " failed: " + e); }
        }

        /// <summary>
        /// Lowest point of the car body in the frame: the most detailed body meshes, without the wheels and the kit.
        /// NaN when the car has no such mesh.
        /// </summary>
        private static float Underside(LandVehicle lv, Transform frame, HashSet<int> lod0)
        {
            float low = float.NaN;
            try
            {
                var wheels = new List<Transform>();
                if (lv.wheels != null) foreach (var w in lv.wheels) if (w != null) wheels.Add(w.transform);
                foreach (var r in lv.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r == null || r.gameObject.name.StartsWith("Chassis_") || InKit(r.transform)) continue;
                    if (lod0 != null && !lod0.Contains(r.GetInstanceID())) continue;
                    bool onWheel = false;
                    foreach (var w in wheels) if (r.transform.IsChildOf(w)) { onWheel = true; break; }
                    if (onWheel) continue;
                    var mf = r.GetComponent<MeshFilter>();
                    var mesh = mf != null ? mf.sharedMesh : null;
                    if (mesh == null) continue;
                    foreach (Vector3 c in Corners(mesh.bounds))
                    {
                        float y = frame.InverseTransformPoint(r.transform.TransformPoint(c)).y;
                        if (float.IsNaN(low) || y < low) low = y;
                    }
                }
            }
            catch (Exception e) { Core.Log.Warning("[Interior] measuring the underside of " + Registry.Name(lv) + " failed - the seats keep their full pedestals: " + e.Message); }
            return low;
        }

        /// <summary>True for anything under an interior root, including one that is still waiting to be destroyed.</summary>
        private static bool InKit(Transform t)
        {
            for (; t != null; t = t.parent) if (t.name == RootName) return true;
            return false;
        }

        /// <summary>Lowest height of a kit seat's cushion top above the lowest point of the body: a 4 cm cushion plus 1 cm.</summary>
        private const float SeatAboveUnderside = 0.05f;

        /// <summary>The mesh of a kit part and where it stands in the frame (seats rise above the underside), or null for an unknown kind.</summary>
        private static Mesh MeshOf(KitPart part, float underside, out Vector3 at)
        {
            Mesh mesh;
            string kind = (part.Kind ?? "").ToLowerInvariant();
            at = part.Position;
            switch (kind)
            {
                case "seat":
                case "bench":
                {
                    // In the low cabins the cushion sits just above the body's underside: it gets thinner, and the
                    // pedestal reaches at most 0.32 m down and ends above the underside, so nothing shows under the car.
                    float thick = 0.12f, drop = 0.32f;
                    if (!float.IsNaN(underside))
                    {
                        at.y = Mathf.Max(at.y, underside + SeatAboveUnderside);
                        thick = Mathf.Clamp(at.y - underside - 0.01f, SeatAboveUnderside - 0.01f, 0.12f);
                        drop = Mathf.Clamp(at.y - underside - 0.08f, 0f, 0.32f);
                    }
                    mesh = KitMesh.Seat(part.Size, drop, "Chassis_" + kind, thick);
                    break;
                }
                case "dash":
                    mesh = KitMesh.Dash(part.Size, "Chassis_dash");
                    break;
                case "wheel":
                    mesh = KitMesh.Wheel(part.Size.x, part.Size.z, "Chassis_wheel");
                    break;
                case "gauges":
                    mesh = KitMesh.Gauges(part.Size, "Chassis_gauges");
                    break;
                case "needle":
                    mesh = KitMesh.Needle(part.Size, "Chassis_needle");
                    break;
                case "rocker":
                    mesh = KitMesh.Rocker(part.Size, "Chassis_rocker");
                    break;
                case "button":
                    mesh = KitMesh.Button(part.Size, "Chassis_button");
                    break;
                case "stalk":
                    mesh = KitMesh.Stalk(part.Size, "Chassis_stalk");
                    break;
                case "lamp":
                    mesh = new KitMesh().Box(Vector3.zero, part.Size).Build("Chassis_lamp");
                    break;
                default:
                    Core.Log.Warning("[Interior] unknown kit part '" + part.Kind + "' - skipped.");
                    return null;
            }
            return mesh;
        }

        private static Renderer Piece(Transform parent, KitPart part, string name, float underside)
        {
            string kind = (part.Kind ?? "").ToLowerInvariant();
            Mesh mesh = MeshOf(part, underside, out Vector3 at);
            if (mesh == null) return null;
            string objectName = !string.IsNullOrEmpty(part.Name) ? part.Name : kind == "wheel" ? WheelName : "Chassis_" + kind + "_" + name;
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(part.Rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            var body = objectName == HazardSwitch ? Backlit(part.Color) : Flat(part.Color);
            if (mesh.subMeshCount > 1) r.sharedMaterials = new[] { body, Flat(Accent(kind, part.Color)) };
            else r.sharedMaterial = body;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        /// <summary>
        /// Swaps the body meshes of the most detailed LOD that carry windows for copies without the cabin lid. With no
        /// <paramref name="b"/> it only makes the copies, to learn whether the lid can go. Runs before the glass goes in,
        /// while the window slots still have their vanilla materials.
        /// </summary>
        private static int OpenShell(LandVehicle lv, GlassProfile glass, string code, Bounds band, HashSet<int> lod0, Built b)
        {
            int opened = 0;
            Transform root = lv.transform;
            foreach (var r in lv.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null || r.gameObject.name == LiningName) continue;
                if (lod0 != null && !lod0.Contains(r.GetInstanceID())) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mats = r.sharedMaterials;
                var window = new bool[mats.Length];
                bool any = false;
                for (int i = 0; i < mats.Length; i++) any |= window[i] = IsWindow(mats[i], glass);
                if (!any) continue;
                // Under a tuning cut, the cabin opens from the whole mesh; the tuning cuts the opened copy again.
                Mesh src = Chassis.Tune.Body.Uncut(mf) ?? mf.sharedMesh;
                Matrix4x4 toRoot = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                Mesh open = ShellCut.Open(src, code, toRoot, band, window, out string report);
                if (b == null) Core.Log.Msg("[Interior] " + code + " " + src.name + ": " + report + ".");
                if (open == null) continue;
                opened++;
                if (b == null) continue;
                b.Opened.Add(mf);
                b.Closed.Add(src);
                mf.sharedMesh = open;
            }
            return opened;
        }

        /// <summary>Puts the tinted glass into every window slot of the car's renderers.</summary>
        private static int Glaze(LandVehicle lv, GlassProfile glass, Built b)
        {
            int slots = 0;
            Material tinted = Glass(Chassis.Tune.Apply.TintOf(lv) ?? glass.Tint);
            foreach (var r in lv.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null || r.gameObject.name == LiningName) continue;
                var mats = r.sharedMaterials;
                Material[] copy = null;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!IsWindow(mats[i], glass)) continue;
                    if (copy == null) { copy = new Material[mats.Length]; for (int k = 0; k < mats.Length; k++) copy[k] = mats[k]; }
                    copy[i] = tinted;
                    slots++;
                }
                if (copy == null) continue;
                var original = new Material[mats.Length];
                for (int k = 0; k < mats.Length; k++) original[k] = mats[k];
                b.Glazed.Add(r);
                b.Original.Add(original);
                r.sharedMaterials = copy;
            }
            return slots;
        }

        /// <summary>
        /// Draws the inside of each glazed body shell: a copy of its mesh with front faces culled, in the lining colour,
        /// and the glass in the window slots, so the windows also look tinted from inside.
        /// </summary>
        private static int Line(LandVehicle lv, InteriorProfile ip, Built b, List<Renderer> lodRenderers, LODGroup lod)
        {
            int lined = 0;
            Material lining = Lining(ip.LiningColor);
            Material glassInside = ip.Glass != null ? Glass(Chassis.Tune.Apply.TintOf(lv) ?? ip.Glass.Tint, true) : null;
            var lod0 = Lod0(lod);
            for (int i = 0; i < b.Glazed.Count; i++)
            {
                var r = b.Glazed[i];
                if (r == null) continue;
                if (lod0 != null && !lod0.Contains(r.GetInstanceID())) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var go = new GameObject(LiningName);
                go.transform.SetParent(r.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var lr = go.AddComponent<MeshRenderer>();
                var orig = b.Original[i];
                var mats = new Material[orig.Length];
                for (int k = 0; k < orig.Length; k++) mats[k] = IsWindow(orig[k], ip.Glass) ? (glassInside ?? lining) : lining;
                lr.sharedMaterials = mats;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                b.Linings.Add(go);
                lodRenderers.Add(lr);
                lined++;
            }
            return lined;
        }

        private static HashSet<int> Lod0(LODGroup lod)
        {
            if (lod == null) return null;
            try
            {
                var lods = lod.GetLODs();
                if (lods == null || lods.Length == 0) return null;
                var set = new HashSet<int>();
                foreach (var r in lods[0].renderers) if (r != null) set.Add(r.GetInstanceID());
                return set;
            }
            catch (Exception e) { Core.Log.Warning("[Interior] reading the LOD group failed - every shell is lined: " + e.Message); return null; }
        }

        /// <summary>The kit fades out with the car's most detailed LOD.</summary>
        private static void AddToLod(LODGroup lod, List<Renderer> add, string who)
        {
            if (lod == null || add.Count == 0) return;
            try
            {
                var lods = lod.GetLODs();
                if (lods == null || lods.Length == 0) return;
                var first = lods[0];
                var list = new List<Renderer>();
                foreach (var r in first.renderers) if (r != null) list.Add(r);
                list.AddRange(add);
                first.renderers = list.ToArray();
                lods[0] = first;
                lod.SetLODs(lods);
            }
            catch (Exception e) { Core.Log.Warning("[Interior] " + who + ": the kit could not join the LOD group, it stays drawn at any distance: " + e.Message); }
        }

        // ---- removing ----------------------------------------------------------------------------------------------

        /// <summary>Takes every built interior out again and puts the original windows back.</summary>
        internal static int RemoveAll()
        {
            int n = 0;
            foreach (var b in _built.Values) if (Remove(b)) n++;
            _built.Clear();
            Core.Log.Msg("[Interior] removed " + n + " interior(s).");
            return n;
        }

        private static bool Remove(Built b)
        {
            try
            {
                for (int i = 0; i < b.Glazed.Count; i++) if (b.Glazed[i] != null) b.Glazed[i].sharedMaterials = b.Original[i];
                for (int i = 0; i < b.Opened.Count; i++) if (b.Opened[i] != null) b.Opened[i].sharedMesh = b.Closed[i];
                foreach (var go in b.Linings) if (go != null) UnityEngine.Object.Destroy(go);
                Instruments.Gauges.Forget(b.V);
                Instruments.Controls.Forget(b.V);
                if (b.Root != null)
                {
                    b.Root.transform.SetParent(null, false);
                    UnityEngine.Object.Destroy(b.Root);
                }
                Vehicles.Parts.Forget(b.V);
                return true;
            }
            catch (Exception e) { Core.Log.Error("[Interior] removing the interior of " + Registry.Name(b.V) + " failed: " + e.Message); return false; }
        }

        internal static void Reset()
        {
            _next = 0f;
            _built.Clear();
            _fitFailed.Clear();
        }

        internal static string Describe() => "interior " + (Enabled ? "on" : "off") + ", " + _built.Count + " car(s) built";

        // ---- materials ---------------------------------------------------------------------------------------------

        private static bool IsWindow(Material m, GlassProfile glass)
        {
            if (m == null || glass == null) return false;
            string name = m.name;
            foreach (string prefix in glass.Materials)
                if (!string.IsNullOrEmpty(prefix) && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static Material Lit(string key, Color c)
        {
            if (_materials.TryGetValue(key, out var mat) && mat != null) return mat;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) { Core.Log.Error("[Interior] URP/Lit shader not found - the kit uses the default shader."); shader = Shader.Find("Standard"); }
            mat = new Material(shader) { name = "Chassis_" + key };
            mat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            mat.SetColor("_BaseColor", c);
            mat.SetFloat("_Smoothness", 0.15f);
            mat.SetFloat("_Metallic", 0f);
            _materials[key] = mat;
            return mat;
        }

        private static Material Flat(Color c) => Lit("kit_" + c, c);

        /// <summary>A kit colour that glows faintly, like a switch lit from behind.</summary>
        private static Material Backlit(Color c)
        {
            string key = "backlit_" + c;
            if (_materials.TryGetValue(key, out var mat) && mat != null) return mat;
            mat = new Material(Lit("kit_" + c, c)) { name = "Chassis_" + key };
            mat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", c * 0.6f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            _materials[key] = mat;
            return mat;
        }

        /// <summary>The second colour of a kit piece: dark vents on the dashboard, pale dials, a lighter horn pad.</summary>
        private static Color Accent(string kind, Color c)
        {
            switch (kind)
            {
                case "gauges": return new Color(0.78f, 0.8f, 0.74f);
                case "wheel": return new Color(c.r * 1.9f, c.g * 1.9f, c.b * 1.9f);
                case "rocker": return new Color(0.85f, 0.85f, 0.8f);
                case "button": return new Color(c.r * 1.6f, c.g * 1.6f, c.b * 1.6f);
                case "stalk": return new Color(0.5f, 0.5f, 0.52f);
                default: return new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f);
            }
        }

        /// <summary>The lining shows the back faces of the body shell.</summary>
        private static Material Lining(Color c)
        {
            string key = "lining_" + c;
            if (_materials.TryGetValue(key, out var mat) && mat != null) return mat;
            mat = Lit(key, c);
            mat.SetFloat("_Cull", 1f); // front
            return mat;
        }

        /// <summary>The vanilla glass the copies start from: the RV's windows, a transparent URP/Lit the game ships.</summary>
        private const string VanillaGlassName = "Rv glass mat";

        private static Material _vanillaGlass;
        private static bool _vanillaGlassSought;

        /// <summary>Smoothness and metallic of the tinted glass; the reflections are what make it read as glass.</summary>
        internal static float GlassSmoothness = 0.97f, GlassMetallic = 0.6f;

        /// <summary>
        /// The inside face is lit with the outward normal, so it catches the sky; it gets this share of the tint's alpha
        /// and a duller surface, so the view out stays clear.
        /// </summary>
        internal static float GlassInside = 0.3f;

        /// <summary>Premultiplied alpha, as the vanilla Car_Glass: reflections stay at full strength however clear the tint.</summary>
        internal static bool GlassPremultiply = true;

        /// <summary>
        /// Tinted glass: a copy of the vanilla RV glass, so the shader variant for transparency is one the game already
        /// uses, recoloured. Without it, URP/Lit set to Transparent / Alpha as the material inspector writes it.
        /// </summary>
        private static Material Glass(Color tint, bool inside = false)
        {
            string key = "glass_" + tint + (inside ? "_in" : "");
            if (_materials.TryGetValue(key, out var mat) && mat != null) return mat;
            Material vanilla = VanillaGlass();
            if (vanilla != null)
            {
                mat = new Material(vanilla) { name = "Chassis_" + key };
                mat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                _materials[key] = mat;
            }
            else
            {
                mat = Lit(key, tint);
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", 5f);   // SrcAlpha
                mat.SetFloat("_DstBlend", 10f);  // OneMinusSrcAlpha
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = 3000;
            }
            mat.SetShaderPassEnabled("ShadowCaster", false);
            Tune(mat, tint, inside);
            if (inside) mat.SetFloat("_Cull", 1f);
            return mat;
        }

        private static void Tune(Material mat, Color tint, bool inside)
        {
            if (inside) tint.a *= GlassInside;
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Smoothness", inside ? GlassSmoothness * 0.5f : GlassSmoothness);
            mat.SetFloat("_Metallic", GlassMetallic);
            mat.SetFloat("_Blend", GlassPremultiply ? 1f : 0f);
            mat.SetFloat("_SrcBlend", GlassPremultiply ? 1f : 5f); // One : SrcAlpha
            mat.SetFloat("_DstBlend", 10f);                        // OneMinusSrcAlpha
            if (GlassPremultiply) mat.EnableKeyword("_ALPHAPREMULTIPLY_ON"); else mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.SetFloat("_EnvironmentReflections", 1f);
            mat.SetFloat("_SpecularHighlights", 1f);
            mat.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            mat.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
        }

        private static Material VanillaGlass()
        {
            if (_vanillaGlass != null) return _vanillaGlass;
            if (_vanillaGlassSought) return null;
            _vanillaGlassSought = true;
            try
            {
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                    if (m != null && m.name == VanillaGlassName) { _vanillaGlass = m; break; }
            }
            catch (Exception e) { Core.Log.Warning("[Interior] looking for the vanilla glass failed: " + e.Message); }
            if (_vanillaGlass != null) Core.Log.Msg("[Interior] tinted glass copies '" + VanillaGlassName + "' (" + _vanillaGlass.shader.name + ").");
            else Core.Log.Warning("[Interior] '" + VanillaGlassName + "' is not loaded - the tinted glass is a transparent URP/Lit of its own.");
            return _vanillaGlass;
        }

        /// <summary>Sets every tinted glass already made to these values (debug console).</summary>
        internal static int Retint(Color? tint, float smoothness, float metallic, bool premultiply, float inside)
        {
            GlassSmoothness = smoothness;
            GlassMetallic = metallic;
            GlassPremultiply = premultiply;
            GlassInside = inside;
            int n = 0;
            foreach (var kv in _materials)
            {
                if (!kv.Key.StartsWith("glass_") || kv.Value == null) continue;
                bool inner = kv.Key.EndsWith("_in");
                Color c = tint ?? kv.Value.GetColor("_BaseColor");
                if (!tint.HasValue && inner && GlassInside > 0f) c.a /= GlassInside;
                Tune(kv.Value, c, inner);
                n++;
            }
            return n;
        }

        private static IEnumerable<Vector3> Corners(Bounds b)
        {
            Vector3 mn = b.min, mx = b.max;
            yield return new Vector3(mn.x, mn.y, mn.z); yield return new Vector3(mx.x, mn.y, mn.z);
            yield return new Vector3(mn.x, mx.y, mn.z); yield return new Vector3(mx.x, mx.y, mn.z);
            yield return new Vector3(mn.x, mn.y, mx.z); yield return new Vector3(mx.x, mn.y, mx.z);
            yield return new Vector3(mn.x, mx.y, mx.z); yield return new Vector3(mx.x, mx.y, mx.z);
        }
    }
}
