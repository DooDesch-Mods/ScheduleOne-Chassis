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

        /// <summary>A point in root space lies inside the shell.</summary>
        internal bool Inside(Vector3 p) => !IsOutside(p.x, p.y, p.z);

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
                // A part placed with a negative scale (the right mirror as a copy of the left) is drawn with its winding
                // flipped; swap two corners so every front face in the shell points the way it is seen.
                if (toRoot.determinant < 0f)
                    for (int i = 0; i + 2 < got.Count; i += 3) { var b = got[i + 1]; got[i + 1] = got[i + 2]; got[i + 2] = b; }
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

        /// <summary>
        /// The body's outside mirror on one side: the shell points in the height band <paramref name="yMin"/> to
        /// <paramref name="yMax"/> and the length band <paramref name="zMin"/> to <paramref name="zMax"/> that stand more
        /// than 3 cm further out than the door. <paramref name="door"/> is how far the door reaches out from the cabin
        /// centre, measured in the same height band behind the length band. False when no point stands out.
        /// </summary>
        internal bool SideExtent(float zMin, float zMax, float yMin, float yMax, bool left, out float door, out Vector3 min, out Vector3 max, out int count)
            => SideExtent(zMin, zMax, yMin, yMax, yMin, left, out door, out min, out max, out count);

        /// <summary>
        /// As above, with the door measured only from <paramref name="doorYMin"/> up: a body that bulges out below the
        /// window line is wider there than the mirror on the door above it.
        /// </summary>
        internal bool SideExtent(float zMin, float zMax, float yMin, float yMax, float doorYMin, bool left, out float door, out Vector3 min, out Vector3 max, out int count)
        {
            float cx = _o[0];
            float[] t = _t;
            door = 0f;
            int doorPoints = 0;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                float y = t[i + 1], z = t[i + 2];
                if (y < doorYMin || y > yMax || z < zMin - 0.9f || z >= zMin - 0.1f) continue;
                float dx = left ? cx - t[i] : t[i] - cx;
                if (dx <= 0f) continue;
                doorPoints++;
                if (dx > door) door = dx;
            }
            min = max = Vector3.zero;
            count = 0;
            if (doorPoints == 0) return false;
            float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
            float outer = door + 0.03f;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                float x = t[i], y = t[i + 1], z = t[i + 2];
                if (y < yMin || y > yMax || z < zMin || z > zMax) continue;
                float dx = left ? cx - x : x - cx;
                if (dx <= outer) continue;
                count++;
                if (x < x0) x0 = x; if (x > x1) x1 = x;
                if (y < y0) y0 = y; if (y > y1) y1 = y;
                if (z < z0) z0 = z; if (z > z1) z1 = z;
            }
            if (count == 0) return false;
            min = new Vector3(x0, y0, z0);
            max = new Vector3(x1, y1, z1);
            return true;
        }

        /// <summary>
        /// The face of the body's outside mirror: the triangles more than 3 cm further out than <paramref name="door"/>
        /// in the same bands as <see cref="SideExtent"/> whose fronts face back, narrowed to those that lie in the
        /// plane of their area-weighted mean, then grown by the face triangles in that plane they touch
        /// (<see cref="Grow"/>). <paramref name="normal"/> points out of the face, <paramref name="centre"/>
        /// is the middle of the face at its front-most point, <paramref name="size"/> its width and height across the
        /// normal. <paramref name="outline"/> fills the convex hull of the face flat in that plane with triangles, three
        /// corners each, as x and y from <paramref name="centre"/> along the axes of a part turned by
        /// LookRotation(-normal, up): the shape of the vanilla mirror, rounded corners and all. False when no triangle faces back there.
        /// </summary>
        internal bool MirrorFace(float zMin, float zMax, float yMin, float yMax, bool left, float door, out Vector3 centre, out Vector3 normal, out Vector2 size, out int count, out Vector2[] outline)
        {
            float cx = _o[0];
            float[] t = _t;
            float outer = door + 0.03f;
            centre = normal = Vector3.zero;
            size = Vector2.zero;
            count = 0;
            outline = null;
            var picked = new List<int>();
            float sx = 0f, sy = 0f, sz = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                float mx = sx, my = sy, mz = sz;
                float ml = (float)Math.Sqrt(mx * mx + my * my + mz * mz);
                if (pass == 1)
                {
                    if (ml < 1e-9f) return false;
                    mx /= ml; my /= ml; mz /= ml;
                }
                picked.Clear();
                sx = sy = sz = 0f;
                for (int i = 0; i + 8 < t.Length; i += 9)
                {
                    float px = (t[i] + t[i + 3] + t[i + 6]) / 3f, py = (t[i + 1] + t[i + 4] + t[i + 7]) / 3f, pz = (t[i + 2] + t[i + 5] + t[i + 8]) / 3f;
                    if (py < yMin || py > yMax || pz < zMin || pz > zMax) continue;
                    if ((left ? cx - px : px - cx) <= outer) continue;
                    float e1x = t[i + 3] - t[i], e1y = t[i + 4] - t[i + 1], e1z = t[i + 5] - t[i + 2];
                    float e2x = t[i + 6] - t[i], e2y = t[i + 7] - t[i + 1], e2z = t[i + 8] - t[i + 2];
                    // Unity's front face winds clockwise; Cross(e1, e2) points out of it, its length is twice the area.
                    float nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
                    float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (nl < 1e-9f) continue;
                    // The face looks back towards the driver; the rim, the arm and the outer shell do not.
                    if (nz / nl > -0.4f) continue;
                    if (pass == 1 && (nx * mx + ny * my + nz * mz) / nl < 0.9f) continue;
                    picked.Add(i);
                    sx += nx; sy += ny; sz += nz;
                }
                if (picked.Count == 0) return false;
            }
            float l = (float)Math.Sqrt(sx * sx + sy * sy + sz * sz);
            if (l < 1e-9f) return false;
            normal = new Vector3(sx / l, sy / l, sz / l);
            Grow(picked, zMin, zMax, yMin, yMax, left, normal);
            // The axes of a part turned by LookRotation(-normal, up): its local x and y span the face.
            Vector3 ax = Vector3.Cross(Vector3.up, -normal).normalized;
            Vector3 ay = Vector3.Cross(-normal, ax).normalized;
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue, d1 = float.MinValue;
            foreach (int i in picked)
                for (int k = 0; k < 9; k += 3)
                {
                    float px = t[i + k], py = t[i + k + 1], pz = t[i + k + 2];
                    float u = px * ax.x + py * ax.y + pz * ax.z, v = px * ay.x + py * ay.y + pz * ay.z, d = px * normal.x + py * normal.y + pz * normal.z;
                    if (u < x0) x0 = u; if (u > x1) x1 = u;
                    if (v < y0) y0 = v; if (v > y1) y1 = v;
                    if (d > d1) d1 = d;
                }
            count = picked.Count;
            size = new Vector2(x1 - x0, y1 - y0);
            centre = ax * ((x0 + x1) * 0.5f) + ay * ((y0 + y1) * 0.5f) + normal * d1;
            float u0 = (x0 + x1) * 0.5f, v0 = (y0 + y1) * 0.5f;
            var flat = new List<Vector2>(picked.Count * 3);
            foreach (int p in picked)
                for (int k = 0; k < 9; k += 3)
                {
                    int i = p + k;
                    flat.Add(new Vector2(t[i] * ax.x + t[i + 1] * ax.y + t[i + 2] * ax.z - u0, t[i] * ay.x + t[i + 1] * ay.y + t[i + 2] * ay.z - v0));
                }
            // The hull, not the triangles themselves: some faces are a rim around a glass set deeper into the
            // housing (Hounddog), and the triangles alone would leave that middle open.
            List<Vector2> hull = Hull(flat);
            if (hull.Count >= 3)
            {
                outline = new Vector2[(hull.Count - 2) * 3];
                for (int k = 1; k + 1 < hull.Count; k++)
                {
                    outline[(k - 1) * 3] = hull[0];
                    outline[(k - 1) * 3 + 1] = hull[k];
                    outline[(k - 1) * 3 + 2] = hull[k + 1];
                }
            }
            return true;
        }

        /// <summary>The convex hull of <paramref name="points"/>, counter-clockwise, collinear points dropped (monotone chain).</summary>
        private static List<Vector2> Hull(List<Vector2> points)
        {
            var p = new List<Vector2>(points);
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new List<Vector2>(p.Count + 1);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int n = 0; n < p.Count; n++)
                {
                    Vector2 c = pass == 0 ? p[n] : p[p.Count - 1 - n];
                    while (h.Count >= start + 2)
                    {
                        Vector2 a = h[h.Count - 2], b = h[h.Count - 1];
                        if ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) > 1e-9f) break;
                        h.RemoveAt(h.Count - 1);
                    }
                    h.Add(c);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;
        }

        /// <summary>How far a face triangle may lie in front of or behind the face's mean plane, in metres.</summary>
        private const float FacePlane = 0.015f;
        /// <summary>Corners closer than this, in metres, count as shared: a face triangle touching another one.</summary>
        private const float FaceTouch = 0.004f;

        /// <summary>
        /// Adds to <paramref name="picked"/> the rest of the mirror face: triangles in the same bands and on the same side
        /// that face back like it, lie in its plane and touch it, also those not standing out past the door. A face that
        /// the body meshes cut into many small triangles otherwise keeps only its outer edge (Cheetah, Hounddog), and the
        /// glass on it shrinks to a sliver.
        /// </summary>
        private void Grow(List<int> picked, float zMin, float zMax, float yMin, float yMax, bool left, Vector3 normal)
        {
            float cx = _o[0];
            float[] t = _t;
            double plane = 0.0, weight = 0.0;
            foreach (int i in picked)
            {
                Vector3 c = Mid(t, i);
                float a = Area(t, i);
                plane += Vector3.Dot(c, normal) * a;
                weight += a;
            }
            if (weight <= 0.0) return;
            float d = (float)(plane / weight);

            var taken = new HashSet<int>(picked);
            var open = new List<int>();
            for (int i = 0; i + 8 < t.Length; i += 9)
            {
                if (taken.Contains(i)) continue;
                Vector3 c = Mid(t, i);
                if (c.y < yMin || c.y > yMax || c.z < zMin || c.z > zMax) continue;
                if ((left ? cx - c.x : c.x - cx) <= 0f) continue;
                if (Mathf.Abs(Vector3.Dot(c, normal) - d) > FacePlane) continue;
                Vector3 n = Vector3.Cross(new Vector3(t[i + 3] - t[i], t[i + 4] - t[i + 1], t[i + 5] - t[i + 2]),
                                          new Vector3(t[i + 6] - t[i], t[i + 7] - t[i + 1], t[i + 8] - t[i + 2]));
                if (n.sqrMagnitude < 1e-18f || Vector3.Dot(n.normalized, normal) < 0.9f) continue;
                open.Add(i);
            }
            bool more = true;
            while (more && open.Count > 0)
            {
                more = false;
                for (int k = open.Count - 1; k >= 0; k--)
                {
                    int i = open[k];
                    bool touches = false;
                    foreach (int j in picked) if (Touch(t, i, j)) { touches = true; break; }
                    if (!touches) continue;
                    picked.Add(i);
                    open.RemoveAt(k);
                    more = true;
                }
            }
        }

        private static Vector3 Mid(float[] t, int i) =>
            new Vector3((t[i] + t[i + 3] + t[i + 6]) / 3f, (t[i + 1] + t[i + 4] + t[i + 7]) / 3f, (t[i + 2] + t[i + 5] + t[i + 8]) / 3f);

        private static float Area(float[] t, int i) =>
            Vector3.Cross(new Vector3(t[i + 3] - t[i], t[i + 4] - t[i + 1], t[i + 5] - t[i + 2]),
                          new Vector3(t[i + 6] - t[i], t[i + 7] - t[i + 1], t[i + 8] - t[i + 2])).magnitude * 0.5f;

        /// <summary>Whether triangles <paramref name="i"/> and <paramref name="j"/> share a corner.</summary>
        private static bool Touch(float[] t, int i, int j)
        {
            for (int a = 0; a < 9; a += 3)
                for (int b = 0; b < 9; b += 3)
                {
                    float dx = t[i + a] - t[j + b], dy = t[i + a + 1] - t[j + b + 1], dz = t[i + a + 2] - t[j + b + 2];
                    if (dx * dx + dy * dy + dz * dz < FaceTouch * FaceTouch) return true;
                }
            return false;
        }

        /// <summary>
        /// How far along the segment from <paramref name="from"/> to <paramref name="from"/> + <paramref name="along"/> it
        /// first leaves the shell, counted from <paramref name="from"/> as inside: the first crossing where the leaving
        /// crossings outnumber the entering ones. A closed piece in between, or a wall behind the start, does not count.
        /// NaN when the segment never leaves.
        /// </summary>
        internal float FirstExit(Vector3 from, Vector3 along)
        {
            float ox = from.x, oy = from.y, oz = from.z, dx = along.x, dy = along.y, dz = along.z;
            float[] t = _t;
            var hits = new List<KeyValuePair<float, int>>();
            for (int i = 0; i + 8 < t.Length; i += 9)
            {
                float ax = t[i], ay = t[i + 1], az = t[i + 2];
                float e1x = t[i + 3] - ax, e1y = t[i + 4] - ay, e1z = t[i + 5] - az;
                float e2x = t[i + 6] - ax, e2y = t[i + 7] - ay, e2z = t[i + 8] - az;
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
                float nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
                hits.Add(new KeyValuePair<float, int>(hit, nx * dx + ny * dy + nz * dz > 0f ? 1 : -1));
            }
            hits.Sort((a, b) => a.Key.CompareTo(b.Key));
            int net = 0;
            foreach (var h in hits)
            {
                net += h.Value;
                if (net > 0) return h.Key;
            }
            return float.NaN;
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

#if DEBUG
        /// <summary>Writes the shell as a Wavefront OBJ in root space, to read a body's geometry outside the game.</summary>
        internal void WriteObj(string path)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            using (var w = new System.IO.StreamWriter(path))
            {
                w.WriteLine("# cabin centre " + _o[0].ToString("F4", inv) + " " + _o[1].ToString("F4", inv) + " " + _o[2].ToString("F4", inv));
                for (int i = 0; i + 2 < _t.Length; i += 3)
                    w.WriteLine("v " + _t[i].ToString("F4", inv) + " " + _t[i + 1].ToString("F4", inv) + " " + _t[i + 2].ToString("F4", inv));
                for (int i = 0; i < _t.Length / 3; i += 3)
                    w.WriteLine("f " + (i + 1) + " " + (i + 2) + " " + (i + 3));
            }
        }
#endif
    }
}
