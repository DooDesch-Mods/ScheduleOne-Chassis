using System;
using System.Collections.Generic;
using Chassis.Vehicles;

namespace Chassis.Interior
{
    /// <summary>
    /// The body shell of a vanilla car as triangles in its root space, to tell whether a kit part stays inside it.
    /// A corner is outside when the segment from the cabin centre to it leaves the shell more often than it enters:
    /// leaving crosses a triangle whose front faces along the segment, entering one whose front faces back. A closed
    /// vanilla piece inside the cabin, crossed in and out again, does not count.
    /// </summary>
    internal sealed class ShellFit
    {
        private readonly float[] _t;
        private readonly float[] _o;

        private ShellFit(List<Vector3> tris, Vector3 centre)
        {
            _t = new float[tris.Count * 3];
            for (int i = 0; i < tris.Count; i++) { _t[i * 3] = tris[i].x; _t[i * 3 + 1] = tris[i].y; _t[i * 3 + 2] = tris[i].z; }
            _o = new[] { centre.x, centre.y, centre.z };
        }

        internal Vector3 Centre => new Vector3(_o[0], _o[1], _o[2]);

        /// <summary>
        /// The shell of the most detailed LOD, without wheels, Chassis objects and the cabin lid of
        /// <paramref name="lidBand"/>; null with <paramref name="report"/> set when no body mesh can be read.
        /// </summary>
        internal static ShellFit Of(LandVehicle lv, HashSet<int> lod0, Vector3 centre, Bounds? lidBand, Func<Material, bool> isWindow, out string report)
        {
            Transform root = lv.transform;
            var wheels = new List<Transform>();
            if (lv.wheels != null) foreach (var w in lv.wheels) if (w != null) wheels.Add(w.transform);
            var tris = new List<Vector3>();
            var failed = new List<string>();
            int meshes = 0;
            foreach (var r in lv.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null || r.gameObject.name.StartsWith("Chassis_")) continue;
                if (lod0 != null && !lod0.Contains(r.GetInstanceID())) continue;
                bool onWheel = false;
                foreach (var w in wheels) if (r.transform.IsChildOf(w)) { onWheel = true; break; }
                if (onWheel) continue;
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;
                var mats = r.sharedMaterials;
                var window = new bool[mats.Length];
                for (int i = 0; i < mats.Length; i++) window[i] = isWindow(mats[i]);
                Mesh src = Chassis.Tune.Body.Uncut(mf) ?? mesh;
                Matrix4x4 toRoot = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var got = ShellCut.Triangles(src, toRoot, lidBand, window, out string why);
                if (got == null) { failed.Add(src.name + " (" + why + ")"); continue; }
                tris.AddRange(got);
                meshes++;
            }
            if (failed.Count > 0) Core.Log.Warning("[Interior] the shell test skips " + string.Join(", ", failed) + ".");
            if (tris.Count == 0) { report = "no readable body mesh"; return null; }
            report = meshes + " mesh(es), " + tris.Count / 3 + " triangle(s)";
            return new ShellFit(tris, centre);
        }

        /// <summary>How many corners of <paramref name="mesh"/>, placed at <paramref name="at"/> turned by <paramref name="rot"/>, lie outside.</summary>
        internal int Outside(Mesh mesh, Vector3 at, Quaternion rot) => OutsidePoints(mesh, Matrix4x4.TRS(at, rot, Vector3.one), null);

        /// <summary>
        /// How many distinct corners of <paramref name="mesh"/>, mapped into root space by <paramref name="toRoot"/>, lie
        /// outside; adds them to <paramref name="found"/> when given.
        /// </summary>
        internal int OutsidePoints(Mesh mesh, Matrix4x4 toRoot, List<Vector3> found)
        {
            var seen = new HashSet<Vector3>();
            int n = 0;
            foreach (var v in mesh.vertices)
            {
                if (!seen.Add(v)) continue;
                Vector3 p = toRoot.MultiplyPoint3x4(v);
                if (!IsOutside(p.x, p.y, p.z)) continue;
                n++;
                found?.Add(p);
            }
            return n;
        }

        // The test runs on plain floats: every Vector3 operator is a call into the game under IL2CPP, and a fit makes
        // millions of them.
        private bool IsOutside(float px, float py, float pz)
        {
            float ox = _o[0], oy = _o[1], oz = _o[2];
            float dx = px - ox, dy = py - oy, dz = pz - oz;
            float[] t = _t;
            int net = 0;
            for (int i = 0; i + 8 < t.Length; i += 9)
            {
                float ax = t[i], ay = t[i + 1], az = t[i + 2];
                float e1x = t[i + 3] - ax, e1y = t[i + 4] - ay, e1z = t[i + 5] - az;
                float e2x = t[i + 6] - ax, e2y = t[i + 7] - ay, e2z = t[i + 8] - az;
                // Möller-Trumbore: does the segment from the centre to the corner (0..1) cross the triangle?
                float hx = dy * e2z - dz * e2y, hy = dz * e2x - dx * e2z, hz = dx * e2y - dy * e2x;
                float det = e1x * hx + e1y * hy + e1z * hz;
                if (det > -1e-9f && det < 1e-9f) continue;
                float inv = 1f / det;
                float sx = ox - ax, sy = oy - ay, sz = oz - az;
                float u = inv * (sx * hx + sy * hy + sz * hz);
                if (u < 0f || u > 1f) continue;
                float qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
                float v = inv * (dx * qx + dy * qy + dz * qz);
                if (v < 0f || u + v > 1f) continue;
                float hit = inv * (e2x * qx + e2y * qy + e2z * qz);
                if (hit <= 0f || hit >= 1f) continue;
                // Unity's front face winds clockwise; Cross(e1, e2) points out of it.
                float nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
                net += nx * dx + ny * dy + nz * dz > 0f ? 1 : -1;
            }
            return net > 0;
        }
    }
}
