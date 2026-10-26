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

        private const float StripThick = 0.03f, SillTuck = 0.2f, ArchGap = 0.12f, TyreClear = 0.22f, LightEvery = 0.6f;
        private const float LightDrop = 0.06f, LightIntensity = 2.2f;

        /// <summary>The wheels in the car's space: innermost centre on each side, front and rear axle, tyre radius.</summary>
        private static bool Wheels(LandVehicle v, out float left, out float right, out float front, out float rear, out float radius)
        {
            left = float.MinValue; right = float.MaxValue; front = float.MinValue; rear = float.MaxValue; radius = 0f;
            if (v.wheels == null) return false;
            float scale = Mathf.Max(0.01f, v.transform.lossyScale.y);
            foreach (var w in v.wheels)
            {
                if (w == null) continue;
                var p = v.transform.InverseTransformPoint(w.transform.position);
                if (p.x < 0f) left = Mathf.Max(left, p.x);
                else right = Mathf.Min(right, p.x);
                front = Mathf.Max(front, p.z);
                rear = Mathf.Min(rear, p.z);
                var wc = w.wheelCollider;
                if (wc != null) radius = Mathf.Max(radius, wc.radius * wc.transform.lossyScale.y / scale);
            }
            if (radius <= 0f) radius = 0.33f;
            return left > float.MinValue && right < float.MaxValue && front > rear;
        }

        /// <summary>
        /// Glowing tubes under the body on the chosen sides, and point lights spread along each tube, so the ground glows
        /// in an even band around the car. The side tubes run between the wheel arches and the front and rear tubes
        /// between the wheels, so no tube shows inside an arch; each sits 20 cm in from the body edge, so from the
        /// side only its glow shows.
        /// </summary>
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
            float y = b.min.y + StripThick;
            // Side tubes along the sills, front and rear tubes under the bumpers.
            float sx0 = b.min.x + SillTuck, sx1 = b.max.x - SillTuck;
            float sz0 = b.min.z + SillTuck, sz1 = b.max.z - SillTuck;
            float ex0 = sx0, ex1 = sx1, fz = b.max.z - SillTuck, bz = b.min.z + SillTuck;
            bool wheels = Wheels(c.V, out float left, out float right, out float front, out float rear, out float radius);
            if (wheels)
            {
                sz0 = rear + radius + ArchGap;
                sz1 = front - radius - ArchGap;
                ex0 = Mathf.Max(ex0, left + TyreClear);
                ex1 = Mathf.Min(ex1, right - TyreClear);
            }
            else Core.Log.Warning("[Tuning] " + Registry.Name(c.V) + ": no wheels to measure - neon tubes run the body's length.");
            if (sz1 - sz0 < 0.3f) { float mid = (sz0 + sz1) * 0.5f; sz0 = mid - 0.15f; sz1 = mid + 0.15f; }
            if (ex1 - ex0 < 0.3f) { ex0 = b.center.x - 0.15f; ex1 = b.center.x + 0.15f; }

            var mesh = new KitMesh();
            var lights = new List<Vector3>();
            void Tube(Vector3 a, Vector3 e)
            {
                var mid = (a + e) * 0.5f;
                var d = e - a;
                mesh.Box(mid, new Vector3(Mathf.Max(StripThick, Mathf.Abs(d.x)), StripThick, Mathf.Max(StripThick, Mathf.Abs(d.z))));
                int n = Mathf.Max(1, Mathf.RoundToInt(d.magnitude / LightEvery));
                for (int i = 0; i < n; i++) lights.Add(Vector3.Lerp(a, e, (i + 0.5f) / n) + Vector3.down * (StripThick + LightDrop));
            }
            if (sides.IndexOf('l') >= 0) Tube(new Vector3(sx0, y, sz0), new Vector3(sx0, y, sz1));
            if (sides.IndexOf('r') >= 0) Tube(new Vector3(sx1, y, sz0), new Vector3(sx1, y, sz1));
            if (sides.IndexOf('f') >= 0) Tube(new Vector3(ex0, y, fz), new Vector3(ex1, y, fz));
            if (sides.IndexOf('b') >= 0) Tube(new Vector3(ex0, y, bz), new Vector3(ex1, y, bz));
            root.AddComponent<MeshFilter>().sharedMesh = mesh.Build("Chassis_Neon");
            var r = root.AddComponent<MeshRenderer>();
            r.sharedMaterial = Emissive(colour);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // The light reaches the ground below the tube and about half a metre past the body, not across the street.
            float ground = wheels ? LowestWheel(c.V, radius) : b.min.y - 0.3f;
            float clearance = Mathf.Max(0.1f, b.min.y - ground);
            float range = Mathf.Clamp(clearance * 4f + 1.2f, 1.8f, 3f);
            for (int i = 0; i < lights.Count; i++)
            {
                var lg = new GameObject(Body.PartPrefix + "neon_light_" + i);
                lg.transform.SetParent(root.transform, false);
                lg.transform.localPosition = lights[i];
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = colour;
                l.range = range;
                l.intensity = LightIntensity;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
            }
            c.Neon = root;
            Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + ": neon " + Values.Hex(colour) + " on " + sides + ", under y " + Tune.Apply.F(b.min.y)
                         + ", " + lights.Count + " light(s) of " + Tune.Apply.F(range) + " m.");
        }

        /// <summary>The ground under the car in its space: the lowest wheel centre minus the tyre radius.</summary>
        private static float LowestWheel(LandVehicle v, float radius)
        {
            float low = float.MaxValue;
            foreach (var w in v.wheels) if (w != null) low = Mathf.Min(low, v.transform.InverseTransformPoint(w.transform.position).y);
            return low == float.MaxValue ? 0f : low - radius;
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
        /// Bounds of the body in the car's space: the painted body (a mod's paint targets, else <c>VehicleColor.BodyMeshes</c>), else every
        /// mesh without the wheels and Chassis' own objects. Lamps, light beams and props reach past the body, so the
        /// painted meshes come first.
        /// </summary>
        internal static bool Underbody(LandVehicle v, out Bounds b)
        {
            b = new Bounds();
            bool any = false;
            foreach (var t in Paint.TargetsOf(v, out _))
            {
                var bmf = t.Renderer.GetComponent<MeshFilter>();
                if (bmf == null || bmf.sharedMesh == null) continue;
                Add(v, t.Renderer.transform, bmf.sharedMesh.bounds, ref b, ref any);
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
