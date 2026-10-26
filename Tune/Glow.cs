using System;
using System.Collections.Generic;
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

        private const float SillTuck = 0.2f, ArchGap = 0.06f, TyreClear = 0.22f, LightEvery = 0.6f;
        private const float LightDrop = 0.08f, LightIntensity = 2.2f;
        /// <summary>
        /// Fitted strips: how far in from the body's edge they sit, how deep a band behind the edge counts as its lower
        /// rim, the step along the edge, and the height change between two steps that ends a strip (a wheel arch).
        /// </summary>
        private const float EdgeTuck = 0.1f, EdgeBand = 0.15f, Step = 0.1f, Jump = 0.08f;
        /// <summary>The sideways change between two steps that ends a strip: the body's rim turning round a corner.</summary>
        private const float Bend = 0.05f;

        private static readonly Dictionary<int, UnderMap> _maps = new Dictionary<int, UnderMap>();

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
        /// Underglow on the chosen sides: point lights in a row under the body, so the ground glows in an even band around
        /// the car. Like a real kit in its shrouded housing, the light source itself is never seen, only its glow. Each
        /// row follows the body's lower rim ten centimetres in from its edge, just under the rim: the sides from wheel
        /// arch to wheel arch, the front and rear across the bumpers, around the tyres. A car whose body cannot be read
        /// gets straight rows under its lowest point.
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
            bool wheels = Wheels(c.V, out float left, out float right, out float front, out float rear, out float radius);
            if (!wheels) Core.Log.Warning("[Tuning] " + Registry.Name(c.V) + ": no wheels to measure - neon runs the body's length.");
            var map = Map(c.V, b);

            var rows = new List<KeyValuePair<char, List<Vector3>>>();
            foreach (char side in "lrfb")
            {
                if (sides.IndexOf(side) < 0) continue;
                var line = map != null ? Fitted(map, side, b, wheels, left, right, front, rear, radius) : null;
                if (line == null || line.Count < 2)
                {
                    if (map != null) Core.Log.Warning("[Tuning] " + Registry.Name(c.V) + ": no body rim found for the '" + side + "' neon - it runs straight.");
                    line = Straight(side, b, wheels, left, right, front, rear, radius);
                }
                rows.Add(new KeyValuePair<char, List<Vector3>>(side, line));
            }

            var root = new GameObject(Body.PartPrefix + "neon");
            root.transform.SetParent(c.V.transform, false);
            var lights = new List<Vector3>();
            var report = new List<string>();
            foreach (var t in rows) report.Add(t.Key + " " + Tune.Apply.F(Row(t.Value, lights)) + " m");

            // The light reaches the ground below the row and about half a metre past the body, not across the street.
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
            Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + ": neon " + Values.Hex(colour) + " on " + sides + (map != null ? ", fitted to the underside" : ", straight")
                         + " (" + string.Join(", ", report) + "), " + lights.Count + " light(s) of " + Tune.Apply.F(range) + " m.");
        }

        /// <summary>The car's underside map, read once per car; null when its body cannot be read.</summary>
        private static UnderMap Map(LandVehicle v, Bounds b)
        {
            int id = v.GetInstanceID();
            if (_maps.TryGetValue(id, out var map)) return map;
            try
            {
                map = UnderMap.Of(v, b, out string report);
                if (map == null) Core.Log.Warning("[Tuning] " + Registry.Name(v) + ": underside not readable (" + report + ") - neon runs straight.");
                else Core.Log.Msg("[Tuning] " + Registry.Name(v) + ": underside mapped from " + report + ".");
            }
            catch (Exception e)
            {
                Core.Log.Error("[Tuning] mapping the underside of " + Registry.Name(v) + " failed - neon runs straight: " + e);
                map = null;
            }
            if (_maps.Count > 64) _maps.Clear();
            _maps[id] = map;
            return map;
        }

        /// <summary>
        /// A row along the body's rim on one side: points a step apart, walked from the middle outwards until the body
        /// ends, a tyre is in the way or the rim jumps up into a wheel arch.
        /// </summary>
        private static List<Vector3> Fitted(UnderMap map, char side, Bounds b, bool wheels, float left, float right, float front, float rear, float radius)
        {
            bool along = side == 'l' || side == 'r';
            float lo, hi;
            if (along)
            {
                lo = wheels ? rear + radius + ArchGap : b.min.z + EdgeTuck;
                hi = wheels ? front - radius - ArchGap : b.max.z - EdgeTuck;
            }
            else { lo = b.min.x + EdgeTuck; hi = b.max.x - EdgeTuck; }
            if (hi - lo < Step) return null;

            Vector3? At(float s)
            {
                if (along)
                {
                    bool r = side == 'r';
                    float edge = map.EdgeX(s, r);
                    if (float.IsNaN(edge)) return null;
                    float x = r ? edge - EdgeTuck : edge + EdgeTuck;
                    float y = map.Lowest(edge, r ? edge - EdgeBand : edge + EdgeBand, s - Step * 0.5f, s + Step * 0.5f);
                    return float.IsNaN(y) ? (Vector3?)null : new Vector3(x, y, s);
                }
                bool f = side == 'f';
                float ez = map.EdgeZ(s, f);
                if (float.IsNaN(ez)) return null;
                float z = f ? ez - EdgeTuck : ez + EdgeTuck;
                if (wheels)
                {
                    float axle = f ? front : rear;
                    bool besideTyre = s < left + TyreClear || s > right - TyreClear;
                    if (besideTyre && Mathf.Abs(z - axle) < radius + ArchGap) return null;
                }
                float yz = map.Lowest(s - Step * 0.5f, s + Step * 0.5f, ez, f ? ez - EdgeBand : ez + EdgeBand);
                return float.IsNaN(yz) ? (Vector3?)null : new Vector3(s, yz, z);
            }

            // The middle can sit over a gap (an exhaust cut-out); start from the nearest point that has body.
            float mid = (lo + hi) * 0.5f;
            Vector3? start = null;
            float s0 = mid;
            for (int k = 0; k <= 6 && start == null; k++)
                foreach (float t in new[] { mid + k * Step, mid - k * Step })
                    if (t >= lo && t <= hi && (start = At(t)) != null) { s0 = t; break; }
            if (start == null) return null;

            var up = new List<Vector3>();
            var down = new List<Vector3>();
            Walk(At, s0, hi, Step, along, start.Value, up);
            Walk(At, s0, lo, -Step, along, start.Value, down);
            down.Reverse();
            down.Add(start.Value);
            down.AddRange(up);
            return down;
        }

        /// <summary>
        /// Adds the points from <paramref name="from"/> towards <paramref name="to"/> until one is missing, jumps up or
        /// down, or turns sideways round a corner.
        /// </summary>
        private static void Walk(Func<float, Vector3?> at, float from, float to, float step, bool along, Vector3 first, List<Vector3> into)
        {
            var prev = first;
            for (float s = from + step; step > 0f ? s <= to + 1e-4f : s >= to - 1e-4f; s += step)
            {
                var p = at(step > 0f ? Mathf.Min(s, to) : Mathf.Max(s, to));
                if (p == null || Mathf.Abs(p.Value.y - prev.y) > Jump) break;
                if (Mathf.Abs(along ? p.Value.x - prev.x : p.Value.z - prev.z) > Bend) break;
                into.Add(p.Value);
                prev = p.Value;
            }
        }

        /// <summary>A straight row under the body's lowest point, for a car whose underside cannot be read.</summary>
        private static List<Vector3> Straight(char side, Bounds b, bool wheels, float left, float right, float front, float rear, float radius)
        {
            float y = b.min.y;
            float sx0 = b.min.x + SillTuck, sx1 = b.max.x - SillTuck;
            float sz0 = b.min.z + SillTuck, sz1 = b.max.z - SillTuck;
            float ex0 = sx0, ex1 = sx1, fz = b.max.z - SillTuck, bz = b.min.z + SillTuck;
            if (wheels)
            {
                sz0 = rear + radius + ArchGap;
                sz1 = front - radius - ArchGap;
                ex0 = Mathf.Max(ex0, left + TyreClear);
                ex1 = Mathf.Min(ex1, right - TyreClear);
            }
            if (sz1 - sz0 < 0.3f) { float mid = (sz0 + sz1) * 0.5f; sz0 = mid - 0.15f; sz1 = mid + 0.15f; }
            if (ex1 - ex0 < 0.3f) { ex0 = b.center.x - 0.15f; ex1 = b.center.x + 0.15f; }
            switch (side)
            {
                case 'l': return new List<Vector3> { new Vector3(sx0, y, sz0), new Vector3(sx0, y, sz1) };
                case 'r': return new List<Vector3> { new Vector3(sx1, y, sz0), new Vector3(sx1, y, sz1) };
                case 'f': return new List<Vector3> { new Vector3(ex0, y, fz), new Vector3(ex1, y, fz) };
                default: return new List<Vector3> { new Vector3(ex0, y, bz), new Vector3(ex1, y, bz) };
            }
        }

        /// <summary>The lights of one row, spread evenly over its length just below the rim. Returns the length.</summary>
        private static float Row(List<Vector3> line, List<Vector3> lights)
        {
            float length = 0f;
            for (int i = 0; i + 1 < line.Count; i++) length += Vector3.Distance(line[i], line[i + 1]);
            int n = Mathf.Max(1, Mathf.RoundToInt(length / LightEvery));
            for (int k = 0; k < n; k++) lights.Add(Along(line, length * (k + 0.5f) / n) + Vector3.down * LightDrop);
            return length;
        }

        /// <summary>The point <paramref name="at"/> metres along the line.</summary>
        private static Vector3 Along(List<Vector3> line, float at)
        {
            for (int i = 0; i + 1 < line.Count; i++)
            {
                float len = Vector3.Distance(line[i], line[i + 1]);
                if (at <= len || i + 2 == line.Count) return len < 1e-4f ? line[i] : Vector3.Lerp(line[i], line[i + 1], Mathf.Clamp01(at / len));
                at -= len;
            }
            return line[0];
        }

        /// <summary>The ground under the car in its space: the lowest wheel centre minus the tyre radius.</summary>
        private static float LowestWheel(LandVehicle v, float radius)
        {
            float low = float.MaxValue;
            foreach (var w in v.wheels) if (w != null) low = Mathf.Min(low, v.transform.InverseTransformPoint(w.transform.position).y);
            return low == float.MaxValue ? 0f : low - radius;
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

        internal static void Reset() => _maps.Clear();
    }
}
