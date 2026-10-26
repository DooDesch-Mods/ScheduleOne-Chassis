using System;
using System.Collections.Generic;
using Chassis.Interior;
using Chassis.Vehicles;

namespace Chassis.Tune
{
    /// <summary>
    /// The underside of a car's body as a height grid in its root space: per 2.5 cm cell, the lowest body surface above
    /// it, or nothing where no body is. The underglow follows it, so its lights sit under the sills and bumpers wherever those
    /// are, instead of on one height under the lowest point of the whole body.
    /// </summary>
    internal sealed class UnderMap
    {
        private const float Cell = 0.025f;
        /// <summary>A triangle whose normal points up or down less than this (as a share of its length) is a wall.</summary>
        private const float WallUp = 0.3f;

        private readonly float _x0, _z0;
        private readonly int _nx, _nz;
        private readonly float[] _low;

        private UnderMap(Bounds area)
        {
            _x0 = area.min.x;
            _z0 = area.min.z;
            _nx = Mathf.Max(1, Mathf.CeilToInt(area.size.x / Cell));
            _nz = Mathf.Max(1, Mathf.CeilToInt(area.size.z / Cell));
            _low = new float[_nx * _nz];
            for (int i = 0; i < _low.Length; i++) _low[i] = float.NaN;
        }

        /// <summary>
        /// The map of the most detailed body meshes inside <paramref name="body"/> (a little wider), without the wheels and
        /// Chassis' own objects. Null with <paramref name="report"/> set when no body mesh can be read.
        /// </summary>
        internal static UnderMap Of(LandVehicle v, Bounds body, out string report)
        {
            var area = body;
            area.Expand(new Vector3(0.1f, 0f, 0.1f));
            var map = new UnderMap(area);
            float top = body.center.y;
            Transform root = v.transform;
            var wheels = new List<Transform>();
            if (v.wheels != null) foreach (var w in v.wheels) if (w != null) wheels.Add(w.transform);
            var lod0 = BaseInterior.Lod0(v.GetComponentInChildren<LODGroup>(true));
            var failed = new List<string>();
            int meshes = 0, tris = 0;
            foreach (var r in v.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null || !r.enabled) continue;
                if (lod0 != null && !lod0.Contains(r.GetInstanceID())) continue;
                bool skip = false;
                for (var t = r.transform; t != null && t != root; t = t.parent) if (t.name.StartsWith("Chassis_", StringComparison.Ordinal)) { skip = true; break; }
                foreach (var w in wheels) if (r.transform.IsChildOf(w)) { skip = true; break; }
                if (skip) continue;
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;
                Mesh src = Body.Uncut(mf) ?? mesh;
                Matrix4x4 toRoot = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var got = ShellCut.Triangles(src, toRoot, null, null, out string why);
                if (got == null) { failed.Add(src.name + " (" + why + ")"); continue; }
                meshes++;
                for (int i = 0; i + 2 < got.Count; i += 3)
                {
                    Vector3 a = got[i], b = got[i + 1], c = got[i + 2];
                    if (a.y > top && b.y > top && c.y > top) continue;
                    map.Add(a, b, c);
                    tris++;
                }
            }
            if (failed.Count > 0) Core.Log.Warning("[Tuning] " + Registry.Name(v) + ": the underside map skips " + string.Join(", ", failed) + ".");
            int cells = 0;
            foreach (float f in map._low) if (!float.IsNaN(f)) cells++;
            report = meshes + " mesh(es), " + tris + " triangle(s), " + cells + " of " + map._low.Length + " cell(s)";
            return cells > 0 ? map : null;
        }

        /// <summary>
        /// Writes one triangle: a flat or sloped one with its height at each cell it covers, a wall with its lowest corner
        /// along its edges, so a door's lower edge counts as the sill under it.
        /// </summary>
        private void Add(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            float len = n.magnitude;
            if (len < 1e-9f) return;
            bool wall = Mathf.Abs(n.y) < WallUp * len;
            float floor = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            if (!wall)
            {
                int ix0 = Ix(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), ix1 = Ix(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
                int iz0 = Iz(Mathf.Min(a.z, Mathf.Min(b.z, c.z))), iz1 = Iz(Mathf.Max(a.z, Mathf.Max(b.z, c.z)));
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                for (int iz = iz0; iz <= iz1; iz++)
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        float px = _x0 + (ix + 0.5f) * Cell, pz = _z0 + (iz + 0.5f) * Cell;
                        float u = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / d;
                        float w = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / d;
                        if (u < 0f || w < 0f || u + w > 1f) continue;
                        Put(ix, iz, u * a.y + w * b.y + (1f - u - w) * c.y);
                    }
            }
            Edge(a, b, wall ? floor : float.NaN);
            Edge(b, c, wall ? floor : float.NaN);
            Edge(c, a, wall ? floor : float.NaN);
        }

        /// <summary>Marks the cells along one edge, with its own height or with <paramref name="y"/> when given.</summary>
        private void Edge(Vector3 a, Vector3 b, float y)
        {
            float flat = new Vector2(b.x - a.x, b.z - a.z).magnitude;
            int steps = Mathf.Max(1, Mathf.CeilToInt(flat / (Cell * 0.5f)));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector3.Lerp(a, b, i / (float)steps);
                Put(Ix(p.x), Iz(p.z), float.IsNaN(y) ? p.y : y);
            }
        }

        private void Put(int ix, int iz, float y)
        {
            if (ix < 0 || iz < 0 || ix >= _nx || iz >= _nz) return;
            int i = iz * _nx + ix;
            if (float.IsNaN(_low[i]) || y < _low[i]) _low[i] = y;
        }

        private int Ix(float x) => Mathf.Clamp(Mathf.FloorToInt((x - _x0) / Cell), 0, _nx - 1);
        private int Iz(float z) => Mathf.Clamp(Mathf.FloorToInt((z - _z0) / Cell), 0, _nz - 1);

        /// <summary>The lowest surface over the area, NaN when no body is above any of it.</summary>
        internal float Lowest(float x0, float x1, float z0, float z1)
        {
            float low = float.NaN;
            for (int iz = Iz(Mathf.Min(z0, z1)); iz <= Iz(Mathf.Max(z0, z1)); iz++)
                for (int ix = Ix(Mathf.Min(x0, x1)); ix <= Ix(Mathf.Max(x0, x1)); ix++)
                {
                    float y = _low[iz * _nx + ix];
                    if (!float.IsNaN(y) && (float.IsNaN(low) || y < low)) low = y;
                }
            return low;
        }

        /// <summary>The body's outer edge on the left or right at <paramref name="z"/>, NaN when no body crosses it.</summary>
        internal float EdgeX(float z, bool right)
        {
            int iz = Iz(z);
            for (int k = 0; k < _nx; k++)
            {
                int ix = right ? _nx - 1 - k : k;
                if (!float.IsNaN(_low[iz * _nx + ix])) return _x0 + (right ? ix + 1 : ix) * Cell;
            }
            return float.NaN;
        }

        /// <summary>The body's outer edge at the front or the back at <paramref name="x"/>, NaN when no body crosses it.</summary>
        internal float EdgeZ(float x, bool front)
        {
            int ix = Ix(x);
            for (int k = 0; k < _nz; k++)
            {
                int iz = front ? _nz - 1 - k : k;
                if (!float.IsNaN(_low[iz * _nx + ix])) return _z0 + (front ? iz + 1 : iz) * Cell;
            }
            return float.NaN;
        }
    }
}
