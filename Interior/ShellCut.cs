using System;
using System.Collections.Generic;
using Chassis.Compat;
using UnityEngine.Rendering;

namespace Chassis.Interior
{
    /// <summary>
    /// Opens the cabin of a vanilla body that closes it with a flat lid at the belt line. The body meshes are not
    /// readable on the CPU, so the vertex and index buffers are read back from the GPU into a readable copy, and the
    /// copy leaves out the near-horizontal triangles that lie inside the window band's footprint at belt height, under
    /// the roof or the glass.
    /// One copy per source mesh and car code; a mesh with no such triangles keeps its original.
    /// </summary>
    internal static class ShellCut
    {
        /// <summary>How far above and below the belt line a lid triangle may lie.</summary>
        private const float Above = 0.10f, Below = 0.12f;

        /// <summary>How far a lid corner may stick out of the window band's footprint.</summary>
        private const float Margin = 0.06f;

        private static readonly Dictionary<string, Mesh> _cut = new Dictionary<string, Mesh>();

        /// <summary>
        /// The open copy of <paramref name="src"/>, or null when it has no lid or could not be read. <paramref name="toRoot"/>
        /// maps mesh space into the car's root space, where <paramref name="band"/> lies; <paramref name="window"/> marks the
        /// submeshes that are glass and stay whole.
        /// </summary>
        internal static Mesh Open(Mesh src, string code, Matrix4x4 toRoot, Bounds band, bool[] window, out string report)
        {
            string key = code + "|" + src.GetInstanceID();
            if (_cut.TryGetValue(key, out var done)) { report = done != null ? "open copy reused" : "no lid"; return done; }
            float belt = band.min.y;
            float x0 = band.min.x - Margin, x1 = band.max.x + Margin, z0 = band.min.z - Margin, z1 = band.max.z + Margin;
            var cover = new Cover(belt + Above);
            int open = 0;
            var kept = new Bounds();
            Mesh made = null;
            try
            {
                made = Copy(src, toRoot, (int s, Vector3 a, Vector3 b, Vector3 c, out float ar) =>
                {
                    ar = 0f;
                    bool glass = window != null && s < window.Length && window[s];
                    if (glass || !IsLid(a, b, c, belt, x0, x1, z0, z1, out ar)) return false;
                    Vector3 mid = (a + b + c) / 3f;
                    if (cover.Over(mid)) return true;
                    if (open++ == 0) kept = new Bounds(mid, Vector3.zero); else kept.Encapsulate(mid);
                    return false;
                }, "_Chassis_open", out int removed, out float area, out report, scan: cover.Add);
                if (made != null) report = "lid of " + removed + " triangle(s), " + area.ToString("F2") + " m2 removed";
                else if (report == null) report = "no lid";
                if (open > 0)
                    report += "; " + open + " flat triangle(s) at belt height kept, nothing above them (x " + kept.min.x.ToString("F2") + ".." + kept.max.x.ToString("F2")
                        + ", z " + kept.min.z.ToString("F2") + ".." + kept.max.z.ToString("F2") + ")";
            }
            catch (Exception e) { report = "readback threw: " + e.Message; Core.Log.Error("[Interior] opening " + src.name + " failed: " + e); }
            _cut[key] = made;
            return made;
        }

        /// <summary>
        /// A copy of <paramref name="src"/> without the triangles whose centre lies inside one of <paramref name="boxes"/>
        /// (in the car's root space, which <paramref name="toRoot"/> maps to), or null when no triangle lies inside or the
        /// mesh could not be read. Tuning parts cut the stock bumper, wing or hood out of the baked body this way.
        /// </summary>
        internal static Mesh Cut(Mesh src, string boxesKey, Matrix4x4 toRoot, List<Bounds> boxes, out string report)
        {
            string key = "cut|" + boxesKey + "|" + src.GetInstanceID();
            if (_cut.TryGetValue(key, out var done)) { report = done != null ? "cut copy reused" : "nothing inside the boxes"; return done; }
            Mesh made = null;
            try
            {
                made = Copy(src, toRoot, (int s, Vector3 a, Vector3 b, Vector3 c, out float ar) =>
                {
                    ar = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                    Vector3 mid = (a + b + c) / 3f;
                    foreach (var box in boxes) if (box.Contains(mid)) return true;
                    return false;
                }, "_Chassis_cut", out int removed, out float area, out report);
                if (made != null) report = removed + " triangle(s), " + area.ToString("F2") + " m2 cut";
                else if (report == null) report = "nothing inside the boxes";
            }
            catch (Exception e) { report = "readback threw: " + e.Message; Core.Log.Error("[Tuning] cutting " + src.name + " failed: " + e); }
            _cut[key] = made;
            return made;
        }

        /// <summary>
        /// Every triangle of <paramref name="src"/> in root space, three corners each, without the cabin lid when
        /// <paramref name="band"/> is given; null with <paramref name="report"/> set when the mesh cannot be read.
        /// </summary>
        internal static List<Vector3> Triangles(Mesh src, Matrix4x4 toRoot, Bounds? band, bool[] window, out string report)
        {
            var tris = new List<Vector3>();
            float belt = band?.min.y ?? 0f;
            float x0 = 0f, x1 = 0f, z0 = 0f, z1 = 0f;
            if (band.HasValue) { var b = band.Value; x0 = b.min.x - Margin; x1 = b.max.x + Margin; z0 = b.min.z - Margin; z1 = b.max.z + Margin; }
            var cover = new Cover(belt + Above);
            try
            {
                Copy(src, toRoot, (int s, Vector3 a, Vector3 b, Vector3 c, out float ar) =>
                {
                    ar = 0f;
                    bool glass = window != null && s < window.Length && window[s];
                    if (band.HasValue && !glass && IsLid(a, b, c, belt, x0, x1, z0, z1, out _) && cover.Over((a + b + c) / 3f)) return false;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    return false;
                }, "_Chassis_probe", out _, out _, out report, scan: band.HasValue ? cover.Add : (Action<Vector3, Vector3, Vector3>)null);
            }
            catch (Exception e) { report = "readback threw: " + e.Message; Core.Log.Warning("[Interior] reading " + src.name + " for the shell test failed: " + e.Message); return null; }
            return report == null ? tris : null;
        }

        /// <summary>A normal whose up component (in root space) is below this belongs to a side wall.</summary>
        private const float WallUp = 0.85f;

        /// <summary>
        /// A copy of <paramref name="src"/> for the lining that draws the shell's back faces, lit with the outward normals.
        /// A side surface that slopes up outside, such as the shoulder under the side windows, would catch the sky and show
        /// as a bright bar along the cabin; in the copy every side wall gets a level normal, so it shades like the door
        /// below it. Roof and floor keep theirs, the glass submeshes in <paramref name="window"/> too. Null with
        /// <paramref name="report"/> set when the mesh cannot be read.
        /// </summary>
        internal static Mesh Inside(Mesh src, Matrix4x4 toRoot, bool[] window, out string report)
        {
            string key = "in|" + src.GetInstanceID();
            if (_cut.TryGetValue(key, out var done)) { report = done != null ? "inside copy reused" : "could not be read before"; return done; }
            Mesh made = null;
            try
            {
                made = Copy(src, toRoot, (int s, Vector3 a, Vector3 b, Vector3 c, out float ar) => { ar = 0f; return false; },
                    "_Chassis_inside", out _, out _, out report, window ?? new bool[0]);
                if (made != null) { report = "side walls levelled"; Core.Log.Msg("[Interior] " + src.name + ": lining copy with levelled side walls made."); }
            }
            catch (Exception e) { report = "readback threw: " + e.Message; Core.Log.Error("[Interior] levelling the side walls of " + src.name + " failed: " + e); }
            _cut[key] = made;
            return made;
        }

        internal static void Reset() => _cut.Clear();

        private delegate bool DropTriangle(int submesh, Vector3 a, Vector3 b, Vector3 c, out float area);

        /// <summary>
        /// Reads <paramref name="src"/> back from the GPU and copies it without the triangles <paramref name="drop"/> picks
        /// (corners in root space). Null with <paramref name="report"/> set when the mesh cannot be read, null with no
        /// report when nothing was dropped. With <paramref name="level"/>, the copy is always made and the side walls
        /// outside the submeshes it marks get level normals. <paramref name="scan"/> sees every triangle before
        /// <paramref name="drop"/> sees the first one.
        /// </summary>
        private static Mesh Copy(Mesh src, Matrix4x4 toRoot, DropTriangle drop, string suffix, out int removed, out float area, out string report, bool[] level = null, Action<Vector3, Vector3, Vector3> scan = null)
        {
            removed = 0;
            area = 0f;
            int count = src.vertexCount, subs = src.subMeshCount;
            if (count <= 0 || subs <= 0) { report = "empty mesh"; return null; }
            if (src.vertexBufferCount != 1) { report = src.vertexBufferCount + " vertex streams, only one is read"; return null; }
            int stride = src.GetVertexBufferStride(0);
            if (stride <= 0 || stride % 4 != 0) { report = "vertex stride " + stride; return null; }

            // Every channel has to be plain floats in stream 0; anything else would come out garbled.
            var attrs = src.GetVertexAttributes();
            var channels = new List<(VertexAttribute attr, int at, int dim)>();
            for (int i = 0; i < attrs.Length; i++)
            {
                var a = attrs[i];
                if (a.format != VertexAttributeFormat.Float32) { report = a.attribute + " is " + a.format + ", not Float32"; return null; }
                bool known = a.attribute == VertexAttribute.Position || a.attribute == VertexAttribute.Normal || a.attribute == VertexAttribute.Tangent
                    || a.attribute == VertexAttribute.Color || (a.attribute >= VertexAttribute.TexCoord0 && a.attribute <= VertexAttribute.TexCoord3 && a.dimension == 2);
                if (!known) { report = "channel " + a.attribute + " (" + a.dimension + "D) is not copied"; return null; }
                channels.Add((a.attribute, src.GetVertexAttributeOffset(a.attribute) / 4, a.dimension));
            }

            var vbuf = src.GetVertexBuffer(0);
            if (vbuf == null) { report = "no GPU vertex buffer"; return null; }
            float[] vdata;
            try { vdata = Interop.ReadFloats(vbuf, count * stride / 4); }
            finally { vbuf.Release(); }

            var ibuf = src.GetIndexBuffer();
            if (ibuf == null) { report = "no GPU index buffer"; return null; }
            bool wide = src.indexFormat == IndexFormat.UInt32;
            int icount = ibuf.count;
            int[] indices;
            try { indices = Interop.ReadIndices(ibuf, icount, wide); }
            finally { ibuf.Release(); }

            int step = stride / 4;
            var pos = new Vector3[count];
            int pAt = -1;
            foreach (var c in channels) if (c.attr == VertexAttribute.Position) pAt = c.at;
            if (pAt < 0) { report = "no position channel"; return null; }
            for (int v = 0; v < count; v++) { int i = v * step + pAt; pos[v] = new Vector3(vdata[i], vdata[i + 1], vdata[i + 2]); }

            // A buffer read back as zeros or in another layout does not span the authored bounds.
            var srcBounds = src.bounds;
            var got = new Bounds(pos[0], Vector3.zero);
            for (int v = 1; v < count; v++) got.Encapsulate(pos[v]);
            if ((got.size - srcBounds.size).magnitude > 0.05f * srcBounds.size.magnitude + 0.01f)
            {
                report = "read-back vertices span " + got.size.ToString("F2") + ", the mesh says " + srcBounds.size.ToString("F2");
                return null;
            }

            var root = new Vector3[count];
            for (int v = 0; v < count; v++) root[v] = toRoot.MultiplyPoint3x4(pos[v]);

            for (int s = 0; s < subs; s++)
            {
                var d = src.GetSubMesh(s);
                if (d.topology != MeshTopology.Triangles) { report = "submesh " + s + " is " + d.topology; return null; }
                if (scan == null) continue;
                for (int t = d.indexStart; t + 2 < d.indexStart + d.indexCount; t += 3)
                    scan(root[indices[t] + d.baseVertex], root[indices[t + 1] + d.baseVertex], root[indices[t + 2] + d.baseVertex]);
            }

            var tris = new List<int[]>();
            for (int s = 0; s < subs; s++)
            {
                var d = src.GetSubMesh(s);
                var keep = new List<int>(d.indexCount);
                for (int t = d.indexStart; t + 2 < d.indexStart + d.indexCount; t += 3)
                {
                    int a = indices[t] + d.baseVertex, b = indices[t + 1] + d.baseVertex, c = indices[t + 2] + d.baseVertex;
                    if (drop(s, root[a], root[b], root[c], out float ar)) { removed++; area += ar; continue; }
                    keep.Add(a); keep.Add(b); keep.Add(c);
                }
                tris.Add(keep.ToArray());
            }
            if (removed == 0 && level == null) { report = null; return null; }

            var m = new Mesh { name = src.name + suffix };
            if (count > 65535) m.indexFormat = IndexFormat.UInt32;
            m.vertices = pos;
            foreach (var c in channels)
            {
                if (c.attr == VertexAttribute.Position) continue;
                if (c.attr == VertexAttribute.Normal)
                {
                    var n = Read3(vdata, count, step, c.at);
                    if (level != null) Level(n, tris, level, toRoot);
                    m.normals = n;
                }
                else if (c.attr == VertexAttribute.Tangent) m.tangents = Read4(vdata, count, step, c.at, c.dim);
                else if (c.attr == VertexAttribute.Color) m.colors = Colors(Read4(vdata, count, step, c.at, c.dim));
                else
                {
                    var uv = new Vector2[count];
                    for (int v = 0; v < count; v++) { int i = v * step + c.at; uv[v] = new Vector2(vdata[i], vdata[i + 1]); }
                    int ch = c.attr - VertexAttribute.TexCoord0;
                    if (ch == 0) m.uv = uv; else if (ch == 1) m.uv2 = uv; else if (ch == 2) m.uv3 = uv; else m.uv4 = uv;
                }
            }
            m.subMeshCount = subs;
            for (int s = 0; s < subs; s++) m.SetTriangles(tris[s], s, false);
            m.bounds = srcBounds;
            m.UploadMeshData(true);
            m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            report = null;
            return m;
        }

        /// <summary>A lid triangle faces up or down, sits at belt height and lies inside the cabin's footprint.</summary>
        private static bool IsLid(Vector3 a, Vector3 b, Vector3 c, float belt, float x0, float x1, float z0, float z1, out float area)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            area = n.magnitude * 0.5f;
            if (area < 1e-6f || Mathf.Abs(n.y) < 0.85f * n.magnitude) return false;
            foreach (var p in new[] { a, b, c })
            {
                if (p.y < belt - Below || p.y > belt + Above) return false;
                if (p.x < x0 || p.x > x1 || p.z < z0 || p.z > z1) return false;
            }
            return true;
        }

        /// <summary>How far above a lid triangle the roof or the glass has to be.</summary>
        private const float Clear = 0.05f;

        /// <summary>
        /// The body above the belt zone, in root space: roof, pillars and glass. The cabin lid lies under them. A flat
        /// shoulder beside the rear window or a rear deck at belt height has open sky above it; it is part of the outside
        /// of the car and stays.
        /// </summary>
        private sealed class Cover
        {
            private readonly List<Vector3> _tris = new List<Vector3>();
            private readonly float _from;

            internal Cover(float from) { _from = from; }

            internal void Add(Vector3 a, Vector3 b, Vector3 c)
            {
                if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) <= _from) return;
                _tris.Add(a); _tris.Add(b); _tris.Add(c);
            }

            /// <summary>Whether a triangle of the body lies straight above <paramref name="p"/>.</summary>
            internal bool Over(Vector3 p)
            {
                for (int i = 0; i + 2 < _tris.Count; i += 3)
                {
                    Vector3 a = _tris[i], b = _tris[i + 1], c = _tris[i + 2];
                    float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(d) < 1e-8f) continue;
                    float u = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / d;
                    float v = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / d;
                    float w = 1f - u - v;
                    if (u < 0f || v < 0f || w < 0f) continue;
                    if (u * a.y + v * b.y + w * c.y > p.y + Clear) return true;
                }
                return false;
            }
        }

        /// <summary>Levels the side-wall normals of the vertices that a submesh outside <paramref name="keep"/> uses.</summary>
        private static void Level(Vector3[] normals, List<int[]> tris, bool[] keep, Matrix4x4 toRoot)
        {
            var fromRoot = toRoot.inverse;
            var done = new bool[normals.Length];
            for (int s = 0; s < tris.Count; s++)
            {
                if (s < keep.Length && keep[s]) continue;
                foreach (int v in tris[s])
                {
                    if (done[v]) continue;
                    done[v] = true;
                    Vector3 n = toRoot.MultiplyVector(normals[v]).normalized;
                    if (Mathf.Abs(n.y) >= WallUp) continue;
                    n.y = 0f;
                    normals[v] = fromRoot.MultiplyVector(n).normalized;
                }
            }
        }

        private static Vector3[] Read3(float[] d, int count, int step, int at)
        {
            var r = new Vector3[count];
            for (int v = 0; v < count; v++) { int i = v * step + at; r[v] = new Vector3(d[i], d[i + 1], d[i + 2]); }
            return r;
        }

        private static Vector4[] Read4(float[] d, int count, int step, int at, int dim)
        {
            var r = new Vector4[count];
            for (int v = 0; v < count; v++)
            {
                int i = v * step + at;
                r[v] = new Vector4(d[i], dim > 1 ? d[i + 1] : 0f, dim > 2 ? d[i + 2] : 0f, dim > 3 ? d[i + 3] : 1f);
            }
            return r;
        }

        private static Color[] Colors(Vector4[] v)
        {
            var r = new Color[v.Length];
            for (int i = 0; i < v.Length; i++) r[i] = new Color(v[i].x, v[i].y, v[i].z, v[i].w);
            return r;
        }
    }
}
