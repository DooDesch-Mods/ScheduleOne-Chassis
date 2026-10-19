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

        /// <summary>Hips above the cushion top for the fitted seats.</summary>
        private const float HipHeight = 0.08f;

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
        }

        private static readonly Dictionary<int, Built> _built = new Dictionary<int, Built>();
        private static readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private static readonly HashSet<string> _fitFailed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<int> _gone = new List<int>();

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
                // stays under the roof (head bone 0.78 m above the cushion, its top 0.35 m above the bone). First
                // person looks from the natural eye height, under the roof and at least just above the belt line.
                float cushion = Mathf.Min(belt + (roof - belt) * 0.45f - 0.70f, roof - 1.14f);
                float eyes = Mathf.Max(Mathf.Min(cushion + 0.86f, roof - 0.08f), belt + 0.19f);
                // These bodies close the cabin with a lid at the belt line; dashboard, wheel and eyes sit above it.
                bool lid = LiddedCodes.Contains(code);
                float lift = lid ? 0.16f : 0f;
                if (lid) eyes = Mathf.Max(eyes, Mathf.Min(belt + 0.30f, roof - 0.08f));
                float floor = cushion - 0.32f;

                var profile = new ChassisProfile { Code = code, Interior = new InteriorProfile { Glass = glass } };
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
                parts.Add(new KitPart
                {
                    Kind = "dash",
                    Position = new Vector3(band.center.x, belt + 0.02f + lift, dashFront),
                    Size = new Vector3(Mathf.Max(0.6f, band.size.x - 0.12f), lid ? 0.2f : Mathf.Clamp(belt + 0.02f - floor - 0.3f, 0.2f, 0.45f), Mathf.Max(0.08f, dashFront - dashBack)),
                    Color = new Color(0.18f, 0.18f, 0.19f),
                });

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

                profile.Steering = new SteeringProfile { Path = WheelName, Axis = Vector3.forward, Ratio = 8f, Max = 300f };
                for (int i = 0; i < seats.Length; i++)
                {
                    if (seats[i] == null) continue;
                    Vector3 point = root.TransformPoint(new Vector3(local[i].x, cushion, local[i].z));
                    var seat = ChassisApi.FitSeat(lv, i, point, root, null, HipHeight);
                    seat.Eyes = new Vector3(seat.Eyes.x, eyes, seat.Eyes.z);
                    profile.Seats.Add(seat);
                }
                // Hands on the wheel need the wheel's transform; the kit builds it, so the driver's grip is set here.
                var ds = profile.Seat(driver);
                if (ds != null)
                {
                    ds.Hands.Mode = "wheel";
                    ds.Hands.GripStart = wheelAt + Vector3.right * 0.17f;
                }

                bool used = ChassisApi.Register(profile);
                Core.Log.Msg("[Interior] fitted " + code + ": window band " + ProfileJson.F(band.min) + " .. " + ProfileJson.F(band.max) + ", cushion "
                    + cushion.ToString("F2") + ", driver z " + seatZ.ToString("F2") + (shift < 0f ? " (moved back " + (-shift).ToString("F2") + ")" : "") + (lid ? ", above the cabin lid" : "") + ", wheel z " + wheelAt.z.ToString("F2") + ", dash " + (dashFront - dashBack).ToString("F2") + " m deep, " + parts.Count + " kit part(s), " + profile.Seats.Count + " seat(s)" + (used ? "." : " - a JSON file wins."));
            }
            catch (Exception e)
            {
                _fitFailed.Add(code);
                Core.Log.Error("[Interior] fitting " + code + " failed - it keeps the vanilla cabin: " + e);
            }
        }

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
                if (old != null) UnityEngine.Object.Destroy(old.gameObject);
                b.Root = new GameObject(RootName);
                b.Root.transform.SetParent(frame, false);

                int n = 0;
                foreach (var part in ip.Parts)
                {
                    var r = Piece(b.Root.transform, part, profile.Code + "_" + n++);
                    if (r != null) kitRenderers.Add(r);
                }

                int glazed = ip.Glass != null ? Glaze(lv, ip.Glass, b) : 0;
                int lined = ip.Lining ? Line(lv, ip, b, kitRenderers, lod) : 0;
                AddToLod(lod, kitRenderers, Registry.Name(lv));
                // The steering wheel only exists from now on; the parts rig looks it up again.
                Vehicles.Parts.Forget(lv);
                Core.Log.Msg("[Interior] " + Registry.Name(lv) + ": " + kitRenderers.Count + " kit renderer(s), " + glazed + " window slot(s) tinted, " + lined + " shell(s) lined.");
            }
            catch (Exception e) { Core.Log.Error("[Interior] building the interior of " + Registry.Name(lv) + " failed: " + e); }
        }

        private static Renderer Piece(Transform parent, KitPart part, string name)
        {
            Mesh mesh;
            string kind = (part.Kind ?? "").ToLowerInvariant();
            string key = kind + "|" + part.Size + "|" + part.Position.y;
            switch (kind)
            {
                case "seat":
                case "bench":
                    mesh = KitMesh.Seat(part.Size, 0.32f, "Chassis_" + kind);
                    break;
                case "dash":
                    mesh = KitMesh.Dash(part.Size, "Chassis_dash");
                    break;
                case "wheel":
                    mesh = KitMesh.Wheel(part.Size.x, part.Size.z, "Chassis_wheel");
                    break;
                default:
                    Core.Log.Warning("[Interior] unknown kit part '" + part.Kind + "' - skipped.");
                    return null;
            }
            var go = new GameObject(kind == "wheel" ? WheelName : "Chassis_" + kind + "_" + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = part.Position;
            go.transform.localRotation = Quaternion.Euler(part.Rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Flat(part.Color);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        /// <summary>Puts the tinted glass into every window slot of the car's renderers.</summary>
        private static int Glaze(LandVehicle lv, GlassProfile glass, Built b)
        {
            int slots = 0;
            Material tinted = Glass(glass.Tint);
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
            Material glassInside = ip.Glass != null ? Glass(ip.Glass.Tint, true) : null;
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
            foreach (var b in _built.Values)
            {
                try
                {
                    for (int i = 0; i < b.Glazed.Count; i++) if (b.Glazed[i] != null) b.Glazed[i].sharedMaterials = b.Original[i];
                    foreach (var go in b.Linings) if (go != null) UnityEngine.Object.Destroy(go);
                    if (b.Root != null) UnityEngine.Object.Destroy(b.Root);
                    Vehicles.Parts.Forget(b.V);
                    n++;
                }
                catch (Exception e) { Core.Log.Error("[Interior] removing the interior of " + Registry.Name(b.V) + " failed: " + e.Message); }
            }
            _built.Clear();
            Core.Log.Msg("[Interior] removed " + n + " interior(s).");
            return n;
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

        /// <summary>The lining shows the back faces of the body shell.</summary>
        private static Material Lining(Color c)
        {
            string key = "lining_" + c;
            if (_materials.TryGetValue(key, out var mat) && mat != null) return mat;
            mat = Lit(key, c);
            mat.SetFloat("_Cull", 1f); // front
            return mat;
        }

        /// <summary>URP/Lit set to Transparent / Alpha, the state the material inspector writes, without a shadow.</summary>
        private static Material Glass(Color tint, bool inside = false)
        {
            string key = "glass_" + tint + (inside ? "_in" : "");
            if (_materials.TryGetValue(key, out var mat) && mat != null) return mat;
            mat = Lit(key, tint);
            mat.SetFloat("_Smoothness", 0.9f);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", 5f);   // SrcAlpha
            mat.SetFloat("_DstBlend", 10f);  // OneMinusSrcAlpha
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = 3000;
            mat.SetShaderPassEnabled("ShadowCaster", false);
            if (inside) mat.SetFloat("_Cull", 1f);
            return mat;
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
