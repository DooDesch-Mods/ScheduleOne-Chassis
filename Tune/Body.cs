using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chassis.Api;
using Chassis.Compat;
using Chassis.Interior;
using Chassis.Vehicles;

namespace Chassis.Tune
{
    /// <summary>
    /// The visual parts. The vanilla body is one baked mesh per level of detail, so a part sits on top of it; the
    /// part's cut boxes take the stock bumper or wing out of the body meshes (a copy per mesh, read back from the GPU
    /// like the cabin opening), and its hide list switches off stock objects of modded cars. Rims replace the stock rim
    /// on every wheel.
    /// </summary>
    internal static class Body
    {
        internal const string PartPrefix = "Chassis_Tune_";

        internal sealed class CutMesh
        {
            internal MeshFilter F;
            internal Mesh Base;
            internal Mesh Cut;
        }

        internal sealed class WheelSwap
        {
            internal LODGroup Group;
            internal readonly List<Renderer> FirstLod = new List<Renderer>();
            internal readonly List<Renderer> Rims = new List<Renderer>();
            internal readonly List<KeyValuePair<Renderer, Material[]>> Recoloured = new List<KeyValuePair<Renderer, Material[]>>();
            internal GameObject Made;
        }

        // ---- parts -------------------------------------------------------------------------------------------------

        internal static void ApplyParts(Tune.Apply.Car c, Dictionary<string, string> s, int epoch)
        {
            var parts = new List<TuningPart>();
            foreach (var p in Tune.Apply.Parts(c, s)) if (p.Build != null && !IsWheels(p)) parts.Add(p);
            var key = new StringBuilder().Append(epoch).Append('|').Append(Get(s, "paint")).Append('|').Append(Get(s, "paint2")).Append('|').Append(Get(s, "rims"));
            foreach (var p in parts) key.Append('|').Append(p.Id);
            if (key.ToString() == c.PartsKey) return;
            c.PartsKey = key.ToString();

            foreach (var go in c.Built) if (go != null) { go.transform.SetParent(null, false); UnityEngine.Object.Destroy(go); }
            c.Built.Clear();
            foreach (var go in c.Hidden) if (go != null) go.SetActive(true);
            c.Hidden.Clear();

            var boxes = new List<Bounds>();
            foreach (var p in parts)
            {
                var fit = p.FitFor(c.Code);
                if (fit == null) continue;
                GameObject made = null;
                try { made = p.Build(c.V); }
                catch (Exception e) { Core.Log.Error("[Tuning] part '" + p.Id + "' failed to build its model for " + Registry.Name(c.V) + ": " + e); }
                if (made == null) { Core.Log.Warning("[Tuning] part '" + p.Id + "' gave no model for " + Registry.Name(c.V) + " - the slot shows stock."); continue; }
                made.name = PartPrefix + p.Slot;
                made.transform.SetParent(c.V.transform, false);
                made.transform.localPosition = fit.Position;
                made.transform.localRotation = Quaternion.Euler(fit.Rotation);
                made.transform.localScale = fit.Scale;
                made.SetActive(true);
                Recolour(c.V, made, s);
                c.Built.Add(made);
                if (fit.Cut != null) boxes.AddRange(fit.Cut);
                if (fit.Hide != null)
                    foreach (string name in fit.Hide)
                    {
                        var t = Frames.Deep(c.V.transform, name);
                        if (t == null) { Core.Log.Warning("[Tuning] part '" + p.Id + "': nothing named '" + name + "' to hide on " + Registry.Name(c.V) + "."); continue; }
                        if (!t.gameObject.activeSelf) continue;
                        t.gameObject.SetActive(false);
                        c.Hidden.Add(t.gameObject);
                    }
            }
            c.Boxes = boxes;
            ApplyCuts(c);
            Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + ": " + c.Built.Count + " part model(s), " + c.Hidden.Count + " stock object(s) hidden, " + boxes.Count + " cut box(es).");
        }

        private static bool IsWheels(TuningPart p) => string.Equals(p.Slot, "wheels", StringComparison.OrdinalIgnoreCase);

        private static string Get(Dictionary<string, string> s, string key) => s.TryGetValue(key, out var v) ? v : "";

        /// <summary>
        /// Materials named paint, paint2 or rim take the car's colours: paint follows the body (custom or vanilla), paint2
        /// the second paint (else the body), rim the rim colour (else unchanged).
        /// </summary>
        private static void Recolour(LandVehicle v, GameObject made, Dictionary<string, string> s)
        {
            Color body = Paint.BodyColour(v, out float bm, out float bs);
            foreach (var r in made.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    string n = m.name.ToLowerInvariant();
                    Color colour; float metallic, smooth;
                    if (n.StartsWith("paint2"))
                    {
                        if (!Values.Paint(Get(s, "paint2"), out colour, out string f2)) { colour = body; metallic = bm; smooth = bs; }
                        else Values.Finish(f2, out metallic, out smooth);
                    }
                    else if (n.StartsWith("paint")) { colour = body; metallic = bm; smooth = bs; }
                    else if (n.StartsWith("rim") && Values.Paint(Get(s, "rims"), out colour, out string fr)) Values.Finish(fr, out metallic, out smooth);
                    else continue;
                    var copy = new Material(m) { name = m.name };
                    Paint.Colour(copy, colour, metallic, smooth);
                    mats[i] = copy;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        // ---- cut boxes ---------------------------------------------------------------------------------------------

        private static string BoxesKey(List<Bounds> boxes)
        {
            var sb = new StringBuilder();
            foreach (var b in boxes)
                sb.Append(F(b.center.x)).Append(',').Append(F(b.center.y)).Append(',').Append(F(b.center.z)).Append(',')
                  .Append(F(b.size.x)).Append(',').Append(F(b.size.y)).Append(',').Append(F(b.size.z)).Append(';');
            return sb.ToString();
        }

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>
        /// The body renderers a cut applies to: every drawn mesh of the car except wheels and anything Chassis added.
        /// Switched-off renderers are collider and helper meshes.
        /// </summary>
        private static IEnumerable<MeshFilter> BodyMeshes(LandVehicle v)
        {
            var wheels = new List<Transform>();
            if (v.wheels != null) foreach (var w in v.wheels) if (w != null) wheels.Add(w.transform);
            foreach (var r in v.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null || !r.enabled || Added(r.transform, v.transform)) continue;
                bool onWheel = false;
                foreach (var w in wheels) if (r.transform.IsChildOf(w)) { onWheel = true; break; }
                if (onWheel) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) yield return mf;
            }
        }

        /// <summary>True for objects Chassis added (interior kit, linings, tuning parts).</summary>
        private static bool Added(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent) if (t.name.StartsWith("Chassis_", StringComparison.Ordinal)) return true;
            return false;
        }

        private static void ApplyCuts(Tune.Apply.Car c)
        {
            string key = BoxesKey(c.Boxes);
            c.BoxesKey = key;
            if (c.Boxes.Count == 0)
            {
                int back = 0;
                foreach (var cm in c.Cuts)
                    if (cm.F != null && cm.Cut != null && Interop.Same(cm.F.sharedMesh, cm.Cut)) { cm.F.sharedMesh = cm.Base; back++; }
                if (c.Cuts.Count > 0) Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + ": " + back + " body mesh(es) back to whole.");
                c.Cuts.Clear();
                return;
            }
            var root = c.V.transform;
            int cut = 0;
            var seen = new List<CutMesh>();
            foreach (var mf in BodyMeshes(c.V))
            {
                CutMesh cm = null;
                foreach (var x in c.Cuts) if (Interop.Same(x.F, mf)) { cm = x; break; }
                Mesh current = mf.sharedMesh;
                Mesh baseMesh = cm != null && cm.Cut != null && Interop.Same(current, cm.Cut) ? cm.Base : current;
                Matrix4x4 toRoot = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Mesh made = ShellCut.Cut(baseMesh, key, toRoot, c.Boxes, out string report);
                if (cm == null) cm = new CutMesh { F = mf };
                cm.Base = baseMesh;
                cm.Cut = made;
                seen.Add(cm);
                if (made != null) { mf.sharedMesh = made; cut++; Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + " " + baseMesh.name + ": " + report + "."); }
                else if (!Interop.Same(mf.sharedMesh, baseMesh)) mf.sharedMesh = baseMesh;
            }
            c.Cuts.Clear();
            c.Cuts.AddRange(seen);
            if (cut == 0) Core.Log.Warning("[Tuning] " + Registry.Name(c.V) + ": the cut boxes hold no body triangle - check the part's Cut boxes.");
        }

        /// <summary>
        /// Another system swapped a cut body mesh (the base interior opens or closes the cabin): cut the new mesh again.
        /// </summary>
        internal static void VerifyCuts(Tune.Apply.Car c)
        {
            if (c.Boxes.Count == 0) return;
            foreach (var cm in c.Cuts)
            {
                if (cm.F == null || cm.Cut == null || Interop.Same(cm.F.sharedMesh, cm.Cut)) continue;
                Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + ": " + cm.F.name + " got a new mesh - cutting it again.");
                ApplyCuts(c);
                return;
            }
        }

        /// <summary>The uncut mesh under a tuning cut on this filter, for the base interior to open the cabin from.</summary>
        internal static Mesh Uncut(MeshFilter mf)
        {
            if (mf == null) return null;
            foreach (var c in Tune.Apply.Cars)
                foreach (var cm in c.Cuts)
                    if (cm.Cut != null && Interop.Same(cm.F, mf) && Interop.Same(mf.sharedMesh, cm.Cut)) return cm.Base;
            return null;
        }

        // ---- wheels ------------------------------------------------------------------------------------------------

        internal static void ApplyWheels(Tune.Apply.Car c, Dictionary<string, string> s, int epoch)
        {
            TuningPart rim = null;
            foreach (var p in Tune.Apply.Parts(c, s)) if (p.Build != null && IsWheels(p)) rim = p;
            string rims = Get(s, "rims");
            string key = epoch + "|" + (rim != null ? rim.Id : "") + "|" + rims;
            if (key == c.WheelsKey) return;
            c.WheelsKey = key;
            RestoreWheels(c);
            if (rim == null && rims.Length == 0) return;
            if (c.V.wheels == null || c.V.wheels.Count == 0) { Core.Log.Warning("[Tuning] " + Registry.Name(c.V) + " has no wheels - rims ignored."); return; }

            bool hasColour = Values.Paint(rims, out Color colour, out string finish);
            Values.Finish(hasColour ? finish : TuningFinish.Classic, out float metallic, out float smooth);
            int done = 0;
            foreach (var w in c.V.wheels)
            {
                if (w == null || w.wheelModel == null) continue;
                var group = w.wheelModel.GetComponentInChildren<LODGroup>(true);
                if (group == null) { Core.Log.Warning("[Tuning] " + Registry.Name(c.V) + " " + w.name + ": no LOD group under the wheel model - skipped."); continue; }
                var lods = group.GetLODs();
                if (lods == null || lods.Length == 0) continue;
                var swap = new WheelSwap { Group = group };
                foreach (var r in lods[0].renderers)
                {
                    if (r == null) continue;
                    swap.FirstLod.Add(r);
                    if (r.name.StartsWith("Rim", StringComparison.OrdinalIgnoreCase)) swap.Rims.Add(r);
                }
                if (swap.Rims.Count == 0) { Core.Log.Warning("[Tuning] " + Registry.Name(c.V) + " " + w.name + ": no renderer named Rim in the wheel's first LOD - skipped."); continue; }

                if (rim == null)
                {
                    // Only a colour: the stock rim gets it.
                    foreach (var r in swap.Rims)
                    {
                        var mats = r.sharedMaterials;
                        var copy = new Material[mats.Length];
                        for (int i = 0; i < mats.Length; i++)
                        {
                            copy[i] = mats[i] != null ? new Material(mats[i]) { name = "Chassis_Rim" } : null;
                            if (copy[i] != null) Paint.Colour(copy[i], colour, metallic, smooth);
                        }
                        swap.Recoloured.Add(new KeyValuePair<Renderer, Material[]>(r, mats));
                        r.sharedMaterials = copy;
                    }
                    c.Wheels.Add(swap);
                    done++;
                    continue;
                }

                GameObject made = null;
                try { made = rim.Build(c.V); }
                catch (Exception e) { Core.Log.Error("[Tuning] rim '" + rim.Id + "' failed to build for " + Registry.Name(c.V) + ": " + e); }
                if (made == null) { Core.Log.Warning("[Tuning] rim '" + rim.Id + "' gave no model - the stock rims stay."); RestoreWheels(c); return; }
                made.name = PartPrefix + "rim";
                Place(c.V, w, group.transform, swap.Rims, made);
                if (hasColour) Recolour(c.V, made, s);
                foreach (var r in swap.Rims) r.forceRenderingOff = true;
                swap.Made = made;
                // The new rim fades out with the stock wheel's first LOD.
                var list = new List<Renderer>(swap.FirstLod);
                foreach (var r in made.GetComponentsInChildren<Renderer>(true)) list.Add(r);
                SetFirstLod(group, list);
                c.Wheels.Add(swap);
                done++;
            }
            Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + ": " + (rim != null ? "rim '" + rim.Id + "'" : "stock rims") + (hasColour ? " in " + rims : "") + " on " + done + " wheel(s).");
        }

        /// <summary>
        /// Puts a rim model (face along its +X) where the stock rim is: centred on it, its face turned outwards along the
        /// axle, scaled so its diameter matches. The axle is the stock rim's thinnest direction.
        /// </summary>
        private static void Place(LandVehicle v, Wheel w, Transform at, List<Renderer> rims, GameObject made)
        {
            Bounds stock = LocalBounds(at, rims);
            Vector3 size = stock.size;
            int axis = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            float diameter = Mathf.Max(axis == 0 ? 0f : size.x, axis == 1 ? 0f : size.y, axis == 2 ? 0f : size.z);

            float side = Mathf.Sign(v.transform.InverseTransformPoint(w.transform.position).x);
            if (side == 0f) side = 1f;
            Vector3 outward = at.InverseTransformDirection(v.transform.TransformDirection(Vector3.right * side));
            Vector3 axle = Vector3.zero;
            axle[axis] = Mathf.Sign(outward[axis]) == 0f ? 1f : Mathf.Sign(outward[axis]);

            made.transform.SetParent(at, false);
            made.transform.localRotation = Quaternion.FromToRotation(Vector3.right, axle);
            made.transform.localScale = Vector3.one;
            made.transform.localPosition = Vector3.zero;
            var own = LocalBounds(made.transform, new List<Renderer>(made.GetComponentsInChildren<Renderer>(true)));
            float partDiameter = Mathf.Max(own.size.y, own.size.z);
            float scale = partDiameter > 0.001f ? diameter / partDiameter : 1f;
            made.transform.localScale = Vector3.one * scale;
            made.transform.localPosition = stock.center - made.transform.localRotation * (own.center * scale);
        }

        /// <summary>Bounds of these renderers' meshes in the space of <paramref name="space"/>.</summary>
        private static Bounds LocalBounds(Transform space, List<Renderer> rs)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in rs)
            {
                var mf = r != null ? r.GetComponent<MeshFilter>() : null;
                if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                Vector3 mn = mb.min, mx = mb.max;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z);
                    var p = space.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        private static void SetFirstLod(LODGroup group, List<Renderer> renderers)
        {
            var lods = group.GetLODs();
            if (lods == null || lods.Length == 0) return;
            var first = lods[0];
            first.renderers = renderers.ToArray();
            lods[0] = first;
            group.SetLODs(lods);
        }

        private static void RestoreWheels(Tune.Apply.Car c)
        {
            foreach (var sw in c.Wheels)
            {
                try
                {
                    foreach (var kv in sw.Recoloured) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
                    foreach (var r in sw.Rims) if (r != null) r.forceRenderingOff = false;
                    if (sw.Made != null)
                    {
                        if (sw.Group != null) SetFirstLod(sw.Group, sw.FirstLod);
                        sw.Made.transform.SetParent(null, false);
                        UnityEngine.Object.Destroy(sw.Made);
                    }
                }
                catch (Exception e) { Core.Log.Error("[Tuning] putting the stock rim back on " + Registry.Name(c.V) + " failed: " + e.Message); }
            }
            if (c.Wheels.Count > 0) Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + ": stock rims back on " + c.Wheels.Count + " wheel(s).");
            c.Wheels.Clear();
        }

        /// <summary>Bounds of the body in the car's space, without the wheels and Chassis' own objects.</summary>
        internal static bool Measure(LandVehicle v, out Bounds b) => Glow.Underbody(v, out b);

        internal static void Reset() { }
    }
}
