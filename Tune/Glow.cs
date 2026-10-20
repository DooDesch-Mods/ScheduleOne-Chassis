using System;
using System.Collections.Generic;
using Chassis.Interior;
using Chassis.Vehicles;

namespace Chassis.Tune
{
    /// <summary>
    /// Light colours: headlights (xenon), underglow (neon) and drift smoke. All of them run on every peer. The drift
    /// smoke itself only plays on the driver's machine, as in vanilla.
    /// </summary>
    internal static class Glow
    {
        // ---- xenon -------------------------------------------------------------------------------------------------

        /// <summary>
        /// The headlight colour: a copy of the car's lit-lamp material in the new colour, which vanilla's light switch then
        /// puts on, and the colour on the light sources (the volumetric beams follow them).
        /// </summary>
        internal static void Xenon(Tune.Apply.Car c, string value)
        {
            string key = value ?? "";
            if (key == c.XenonKey) return;
            c.XenonKey = key;
            var lights = c.V.lights;
            if (lights == null) { if (value != null) Core.Log.Warning("[Tuning] " + Registry.Name(c.V) + " has no vehicle lights - xenon ignored."); return; }
            if (c.XenonStock == null) c.XenonStock = lights.headlightMat_On;
            Material on;
            if (value == null)
            {
                on = c.XenonStock;
                foreach (var kv in c.XenonLights) if (kv.Key != null) kv.Key.color = kv.Value;
                c.XenonLights.Clear();
            }
            else
            {
                Values.Rgb(value, out Color colour);
                on = new Material(c.XenonStock) { name = "Chassis_Xenon" };
                Color stockGlow = on.HasProperty("_EmissionColor") ? on.GetColor("_EmissionColor") : Color.white;
                float strength = Mathf.Max(1f, stockGlow.maxColorComponent);
                on.EnableKeyword("_EMISSION");
                on.SetColor("_EmissionColor", colour * strength);
                if (on.HasProperty("_BaseColor")) on.SetColor("_BaseColor", Color.Lerp(Color.white, colour, 0.6f));
                if (c.XenonLights.Count == 0 && lights.headLightSources != null)
                    foreach (var src in lights.headLightSources)
                    {
                        var l = src != null ? src._Light : null;
                        if (l != null) c.XenonLights.Add(new KeyValuePair<Light, Color>(l, l.color));
                    }
                foreach (var kv in c.XenonLights) if (kv.Key != null) kv.Key.color = colour;
            }
            lights.headlightMat_On = on;
            if (lights.HeadlightsOn && lights.headLightMeshes != null)
                foreach (var m in lights.headLightMeshes) if (m != null) m.material = on;
            Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + ": headlights " + (value != null ? value : "stock") + ", " + c.XenonLights.Count + " light source(s).");
        }

        // ---- neon --------------------------------------------------------------------------------------------------

        private const float StripThick = 0.025f, StripInset = 0.12f, TyreClear = 0.22f;

        /// <summary>The innermost wheel centre on each side, in the car's space.</summary>
        private static bool Track(LandVehicle v, out float left, out float right)
        {
            left = float.MinValue; right = float.MaxValue;
            if (v.wheels == null) return false;
            foreach (var w in v.wheels)
            {
                if (w == null) continue;
                float x = v.transform.InverseTransformPoint(w.transform.position).x;
                if (x < 0f) left = Mathf.Max(left, x);
                else right = Mathf.Min(right, x);
            }
            return left > float.MinValue && right < float.MaxValue;
        }

        /// <summary>Glowing strips under the body on the chosen sides, and two lights that colour the ground.</summary>
        internal static void Neon(Tune.Apply.Car c, string value)
        {
            string key = value ?? "";
            if (key == c.NeonKey) return;
            c.NeonKey = key;
            if (c.Neon != null) { c.Neon.transform.SetParent(null, false); UnityEngine.Object.Destroy(c.Neon); c.Neon = null; }
            if (value == null) { Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + ": neon off."); return; }
            Values.Neon(value, out Color colour, out string sides);
            if (!Body.Measure(c.V, out Bounds b)) { Core.Log.Warning("[Tuning] " + Registry.Name(c.V) + ": no body mesh to measure - neon not fitted."); return; }

            var root = new GameObject(Body.PartPrefix + "neon");
            root.transform.SetParent(c.V.transform, false);
            // Inside the wheel track, so the strips hide under the body and only their glow shows.
            float y = b.min.y + StripThick;
            float x0 = b.min.x + StripInset, x1 = b.max.x - StripInset, z0 = b.min.z + StripInset * 3f, z1 = b.max.z - StripInset * 3f;
            if (Track(c.V, out float left, out float right))
            {
                x0 = Mathf.Max(x0, left + TyreClear);
                x1 = Mathf.Min(x1, right - TyreClear);
                if (x1 - x0 < 0.3f) { x0 = b.center.x - 0.15f; x1 = b.center.x + 0.15f; }
            }
            var mesh = new KitMesh();
            if (sides.IndexOf('l') >= 0) mesh.Box(new Vector3(x0, y, (z0 + z1) * 0.5f), new Vector3(StripThick, StripThick, z1 - z0));
            if (sides.IndexOf('r') >= 0) mesh.Box(new Vector3(x1, y, (z0 + z1) * 0.5f), new Vector3(StripThick, StripThick, z1 - z0));
            if (sides.IndexOf('f') >= 0) mesh.Box(new Vector3((x0 + x1) * 0.5f, y, z1), new Vector3(x1 - x0, StripThick, StripThick));
            if (sides.IndexOf('b') >= 0) mesh.Box(new Vector3((x0 + x1) * 0.5f, y, z0), new Vector3(x1 - x0, StripThick, StripThick));
            root.AddComponent<MeshFilter>().sharedMesh = mesh.Build("Chassis_Neon");
            var r = root.AddComponent<MeshRenderer>();
            r.sharedMaterial = Emissive(colour);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            float len = b.max.z - b.min.z;
            for (int i = 0; i < 2; i++)
            {
                var lg = new GameObject(Body.PartPrefix + "neon_light_" + i);
                lg.transform.SetParent(root.transform, false);
                lg.transform.localPosition = new Vector3(b.center.x, b.min.y - 0.05f, b.center.z + (i == 0 ? 0.25f : -0.25f) * len);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = colour;
                l.range = Mathf.Clamp(len * 0.6f, 2f, 4f);
                l.intensity = 2.5f;
                l.shadows = LightShadows.None;
            }
            c.Neon = root;
            Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + ": neon " + Values.Hex(colour) + " on " + sides + ", under y " + Tune.Apply.F(b.min.y) + ".");
        }

        private static readonly Dictionary<string, Material> _emissive = new Dictionary<string, Material>();

        private static Material Emissive(Color c)
        {
            string key = Values.Hex(c);
            if (_emissive.TryGetValue(key, out var m) && m != null) return m;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) { Core.Log.Error("[Tuning] URP/Lit shader not found - the neon strips use the default shader."); shader = Shader.Find("Standard"); }
            m = new Material(shader) { name = "Chassis_Neon_" + key };
            m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            m.SetColor("_BaseColor", c);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 4f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            _emissive[key] = m;
            return m;
        }

        /// <summary>
        /// Bounds of the body in the car's space: the painted body meshes (<c>VehicleColor.BodyMeshes</c>), else every
        /// mesh without the wheels and Chassis' own objects. Lamps, light beams and props reach past the body, so the
        /// painted meshes come first.
        /// </summary>
        internal static bool Underbody(LandVehicle v, out Bounds b)
        {
            b = new Bounds();
            bool any = false;
            var vc = v.Color;
            if (vc != null && vc.BodyMeshes != null)
                foreach (var bm in vc.BodyMeshes)
                {
                    if (bm == null || bm.Renderer == null) continue;
                    var bmf = bm.Renderer.GetComponent<MeshFilter>();
                    if (bmf == null || bmf.sharedMesh == null) continue;
                    Add(v, bm.Renderer.transform, bmf.sharedMesh.bounds, ref b, ref any);
                }
            if (any) return true;
            Core.Log.Warning("[Tuning] " + Registry.Name(v) + " has no painted body mesh to measure - measuring every mesh of the car.");

            var wheels = new List<Transform>();
            if (v.wheels != null) foreach (var w in v.wheels) if (w != null) wheels.Add(w.transform);
            foreach (var r in v.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null || !r.enabled) continue;
                bool skip = false;
                for (var t = r.transform; t != null && t != v.transform; t = t.parent) if (t.name.StartsWith("Chassis_", StringComparison.Ordinal)) { skip = true; break; }
                foreach (var w in wheels) if (r.transform.IsChildOf(w)) { skip = true; break; }
                if (skip) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Add(v, r.transform, mf.sharedMesh.bounds, ref b, ref any);
            }
            return any;
        }

        private static void Add(LandVehicle v, Transform t, Bounds mb, ref Bounds b, ref bool any)
        {
            Vector3 mn = mb.min, mx = mb.max;
            for (int i = 0; i < 8; i++)
            {
                var p = v.transform.InverseTransformPoint(t.TransformPoint(new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z)));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }

        // ---- tyre smoke --------------------------------------------------------------------------------------------

        /// <summary>
        /// The drift smoke's start colour. Vanilla greys it over its lifetime, which multiplies with the start colour, so
        /// the colour shows darker towards the end.
        /// </summary>
        internal static void Smoke(Tune.Apply.Car c, string value)
        {
            string key = value ?? "";
            if (key == c.SmokeKey) return;
            c.SmokeKey = key;
            if (c.Smoke.Count == 0 && c.V.wheels != null)
                foreach (var w in c.V.wheels)
                {
                    var ps = w != null ? w.DriftParticles : null;
                    if (ps == null) continue;
                    c.Smoke.Add(new KeyValuePair<ParticleSystem, Color>(ps, ps.main.startColor.color));
                }
            if (c.Smoke.Count == 0) { if (value != null) Core.Log.Warning("[Tuning] " + Registry.Name(c.V) + " has no drift smoke - tyre smoke ignored."); return; }
            Color colour = Color.white;
            bool custom = value != null && Values.Rgb(value, out colour);
            foreach (var kv in c.Smoke)
            {
                if (kv.Key == null) continue;
                var main = kv.Key.main;
                main.startColor = new ParticleSystem.MinMaxGradient(custom ? colour : kv.Value);
            }
            Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + ": tyre smoke " + (custom ? value : "stock") + " on " + c.Smoke.Count + " wheel(s).");
        }

        internal static void Reset() { }
    }
}
