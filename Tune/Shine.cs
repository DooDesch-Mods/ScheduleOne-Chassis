using System;
using System.Collections.Generic;
using Chassis.Api;
using Chassis.Vehicles;
using UnityEngine.Rendering;

namespace Chassis.Tune
{
    /// <summary>
    /// Chrome needs something sharp to reflect. The game lights every car from one 16-pixel sky map
    /// (<c>ReflectionProbeManager</c>), which turns a mirror finish into flat grey. A car with a chrome paint gets its own
    /// small realtime reflection probe above its roof that sees the street around it, without the car's own layers. It renders
    /// a few times a second while the car moves near the camera, rarely while it stands, and never far away.
    /// </summary>
    internal static class Shine
    {
        private const string ProbeName = "Chassis_ChromeProbe";
        private const int Resolution = 128;
        private const float MovingEvery = 0.2f, StandingEvery = 2f, FarAway = 60f, RoofGap = 0.5f;

        private sealed class Entry
        {
            internal LandVehicle V;
            internal ReflectionProbe P;
            internal float Next;
            internal Vector3 Last;
        }

        private static readonly Dictionary<int, Entry> _probes = new Dictionary<int, Entry>();
        private static readonly List<int> _gone = new List<int>();

        /// <summary>A chrome paint on the car (first or second): give it a probe; none: take it away.</summary>
        internal static void Set(Tune.Apply.Car c, bool chrome)
        {
            int key = c.V.GetInstanceID();
            bool has = _probes.TryGetValue(key, out var e) && e.P != null;
            if (chrome == has) return;
            if (!chrome)
            {
                UnityEngine.Object.Destroy(e.P.gameObject);
                _probes.Remove(key);
                Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + ": chrome reflections off.");
                return;
            }
            var p = Make(c.V, out string layers);
            if (p == null) return;
            _probes[key] = new Entry { V = c.V, P = p, Next = 0f, Last = c.V.transform.position };
            Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + ": chrome reflections on (probe " + Resolution + " px, " + p.size.x.ToString("F1") + " m, the car's layers " + layers + " left out).");
        }

        private static ReflectionProbe Make(LandVehicle v, out string layers)
        {
            layers = "none";
            Bounds world = default;
            bool any = false;
            // Only what the camera draws: the "Invisible" layer holds objects the camera never shows, and in the probe
            // they render with the error shader, as magenta streaks across the chrome.
            var cam = Camera.main;
            int mask = cam != null ? cam.cullingMask : ~0;
            int invisible = LayerMask.NameToLayer("Invisible");
            if (invisible >= 0) mask &= ~(1 << invisible);
            var seen = new List<int>();
            foreach (var r in v.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null) continue;
                if (!any) { world = r.bounds; any = true; } else world.Encapsulate(r.bounds);
                int layer = r.gameObject.layer;
                // The default layer also holds the street; leaving it out would leave an empty mirror.
                if (layer != 0 && !seen.Contains(layer)) { seen.Add(layer); mask &= ~(1 << layer); }
            }
            if (!any) { Core.Log.Warning("[Tuning] " + Registry.Name(v) + " has no mesh renderers - no chrome reflections."); return null; }
            if (seen.Count > 0)
            {
                var names = new List<string>();
                foreach (int l in seen) names.Add(LayerMask.LayerToName(l) + " (" + l + ")");
                layers = string.Join(", ", names);
            }
            var go = new GameObject(ProbeName);
            go.transform.SetParent(v.transform, false);
            // Above the roof, not in the middle: Chassis's own interior (lining, seats, dash) sits on the default layer,
            // and from inside the car the mirror showed nothing but the dark lining.
            go.transform.position = new Vector3(world.center.x, world.max.y + RoofGap, world.center.z);
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Realtime;
            p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            p.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            p.resolution = Resolution;
            p.hdr = true;
            p.boxProjection = false;
            // Probe boxes are axis-aligned in the world: a cube around the whole car covers it at any heading.
            float side = Mathf.Max(world.size.x, Mathf.Max(world.size.y, world.size.z)) + 2f;
            p.size = new Vector3(side, side, side);
            // The box stays around the car; its centre is measured from the probe, which sits above it.
            p.center = new Vector3(0f, world.center.y - go.transform.position.y, 0f);
            p.importance = 100;
            p.cullingMask = mask;
            p.nearClipPlane = 0.3f;
            p.farClipPlane = 150f;
            // The game draws no skybox the probe could see; left alone the sky in the mirror is black and chrome
            // turns dark. The fog colour is what the horizon shows, so it stands in for the sky (set again per render).
            p.clearFlags = ReflectionProbeClearFlags.SolidColor;
            p.backgroundColor = RenderSettings.fogColor;
            foreach (var r in v.GetComponentsInChildren<MeshRenderer>(true))
                if (r != null && r.reflectionProbeUsage == ReflectionProbeUsage.Off) r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            return p;
        }

        /// <summary>Every frame: renders at most one probe, the one whose turn it is.</summary>
        internal static void Tick()
        {
            if (_probes.Count == 0) return;
            var cam = Camera.main;
            float now = Time.unscaledTime;
            Entry due = null;
            _gone.Clear();
            foreach (var kv in _probes)
            {
                var e = kv.Value;
                if (e.V == null || e.P == null) { _gone.Add(kv.Key); continue; }
                if (now < e.Next) continue;
                if (cam != null && (cam.transform.position - e.V.transform.position).sqrMagnitude > FarAway * FarAway) { e.Next = now + StandingEvery; continue; }
                if (due == null || e.Next < due.Next) due = e;
            }
            foreach (int k in _gone) _probes.Remove(k);
            if (due == null) return;
            var pos = due.V.transform.position;
            bool moving = (pos - due.Last).sqrMagnitude > 0.01f;
            due.Last = pos;
            due.Next = now + (moving ? MovingEvery : StandingEvery);
            try
            {
                due.P.backgroundColor = RenderSettings.fogColor;
                due.P.RenderProbe();
            }
            catch (Exception ex)
            {
                Core.Log.Error("[Tuning] rendering the chrome reflections of " + Registry.Name(due.V) + " failed - they stay as they are: " + ex.Message);
                due.Next = now + 30f;
            }
        }

#if DEBUG
        /// <summary>
        /// Debug console: with a mask, every probe renders those layers at once; without one, the layers of the renderers
        /// within 60 m of the first probe, to find which one draws wrong in the mirror.
        /// </summary>
        internal static string Debug(int? mask)
        {
            Entry first = null;
            foreach (var e in _probes.Values) if (e.P != null) { first = e; break; }
            if (first == null) return "no chrome probe - paint a car chrome first";
            if (mask.HasValue)
            {
                foreach (var e in _probes.Values)
                {
                    if (e.P == null) continue;
                    e.P.cullingMask = mask.Value;
                    e.P.RenderProbe();
                }
                return "probe mask set to 0x" + mask.Value.ToString("X8") + " on " + _probes.Count + " probe(s)";
            }
            var counts = new SortedDictionary<int, int>();
            Vector3 at = first.P.transform.position;
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || !r.enabled || (r.bounds.center - at).sqrMagnitude > FarAway * FarAway) continue;
                int l = r.gameObject.layer;
                counts[l] = counts.TryGetValue(l, out int n) ? n + 1 : 1;
            }
            var parts = new List<string>();
            foreach (var kv in counts) parts.Add(kv.Key + " " + LayerMask.LayerToName(kv.Key) + " x" + kv.Value);
            return "probe mask 0x" + first.P.cullingMask.ToString("X8") + "; layers within " + FarAway + " m: " + string.Join(", ", parts);
        }
#endif

        internal static void Reset()
        {
            foreach (var e in _probes.Values) if (e.P != null) UnityEngine.Object.Destroy(e.P.gameObject);
            _probes.Clear();
        }
    }
}
