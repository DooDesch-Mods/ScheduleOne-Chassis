using System.Collections.Generic;

namespace Chassis.Interior
{
    /// <summary>
    /// Flat-shaded meshes for the base interior, in the low-poly look of the vanilla cars: every face has its own
    /// vertices, so the lighting breaks at each edge.
    /// </summary>
    internal sealed class KitMesh
    {
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<int> _t = new List<int>();
        private readonly List<int> _accent = new List<int>();
        private bool _toAccent;

        /// <summary>The next shapes go into the second submesh, which the kit draws in the accent colour.</summary>
        internal KitMesh Accent(bool on = true) { _toAccent = on; return this; }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            // Unity's front faces wind clockwise; the corners come counter-clockwise as seen from outside.
            var t = _toAccent ? _accent : _t;
            t.Add(i); t.Add(i + 2); t.Add(i + 1);
            t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }

        private void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c);
            var t = _toAccent ? _accent : _t;
            t.Add(i); t.Add(i + 2); t.Add(i + 1);
        }

        /// <summary>A flat disc in the XY plane at <paramref name="z"/> around (x, y), facing -Z.</summary>
        internal KitMesh Disc(float x, float y, float z, float radius, int sides = 12)
        {
            var c = new Vector3(x, y, z);
            for (int j = 0; j < sides; j++)
            {
                float b0 = j * Mathf.PI * 2f / sides, b1 = (j + 1) * Mathf.PI * 2f / sides;
                Tri(c, c + new Vector3(Mathf.Cos(b0), Mathf.Sin(b0), 0f) * radius, c + new Vector3(Mathf.Cos(b1), Mathf.Sin(b1), 0f) * radius);
            }
            return this;
        }

        /// <summary>A box of this size around <paramref name="centre"/>, turned by <paramref name="rot"/> about <paramref name="pivot"/>.</summary>
        internal KitMesh Box(Vector3 centre, Vector3 size, Quaternion rot, Vector3 pivot)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => pivot + rot * (centre + new Vector3(x * h.x, y * h.y, z * h.z) - pivot);
            Vector3 p000 = P(-1, -1, -1), p100 = P(1, -1, -1), p010 = P(-1, 1, -1), p110 = P(1, 1, -1);
            Vector3 p001 = P(-1, -1, 1), p101 = P(1, -1, 1), p011 = P(-1, 1, 1), p111 = P(1, 1, 1);
            Quad(p001, p011, p111, p101); // front (+z)
            Quad(p100, p110, p010, p000); // back (-z)
            Quad(p010, p110, p111, p011); // top
            Quad(p000, p001, p101, p100); // bottom
            Quad(p101, p111, p110, p100); // right
            Quad(p000, p010, p011, p001); // left
            return this;
        }

        internal KitMesh Box(Vector3 centre, Vector3 size) => Box(centre, size, Quaternion.identity, Vector3.zero);

        /// <summary>A ring in the XY plane around the origin, as a torus of few sides.</summary>
        internal KitMesh Ring(float radius, float tube, int segments = 18, int sides = 6)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                for (int j = 0; j < sides; j++)
                {
                    float b0 = j * Mathf.PI * 2f / sides, b1 = (j + 1) * Mathf.PI * 2f / sides;
                    Quad(T(a0, b0), T(a0, b1), T(a1, b1), T(a1, b0));
                }
            }
            return this;

            Vector3 T(float a, float b)
            {
                float r = radius + tube * Mathf.Cos(b);
                return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), tube * Mathf.Sin(b));
            }
        }

        /// <summary>A prism of few sides along +Z from <paramref name="z0"/> to <paramref name="z1"/>.</summary>
        internal KitMesh Column(float radius, float z0, float z1, int sides = 6)
        {
            for (int j = 0; j < sides; j++)
            {
                float b0 = j * Mathf.PI * 2f / sides, b1 = (j + 1) * Mathf.PI * 2f / sides;
                Vector3 c0 = new Vector3(Mathf.Cos(b0) * radius, Mathf.Sin(b0) * radius, 0f);
                Vector3 c1 = new Vector3(Mathf.Cos(b1) * radius, Mathf.Sin(b1) * radius, 0f);
                Quad(c1 + Vector3.forward * z0, c0 + Vector3.forward * z0, c0 + Vector3.forward * z1, c1 + Vector3.forward * z1);
            }
            return this;
        }

        internal Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = _v.ToArray();
            if (_accent.Count == 0) mesh.triangles = _t.ToArray();
            else
            {
                mesh.subMeshCount = 2;
                mesh.SetTriangles(_t.ToArray(), 0);
                mesh.SetTriangles(_accent.ToArray(), 1);
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return mesh;
        }

        // ---- kit pieces --------------------------------------------------------------------------------------------

        /// <summary>
        /// A seat whose origin is the middle of the cushion top, facing +Z: cushion, a pedestal down to the floor, a
        /// backrest leaning back and one headrest per 0.5 m of width.
        /// </summary>
        internal static Mesh Seat(Vector3 size, float floorDrop, string name)
        {
            float w = size.x, back = size.y, d = size.z;
            var m = new KitMesh();
            m.Box(new Vector3(0f, -0.06f, 0f), new Vector3(w, 0.12f, d));
            if (floorDrop > 0.14f) m.Box(new Vector3(0f, -0.12f - (floorDrop - 0.12f) * 0.5f, 0.02f), new Vector3(w * 0.8f, floorDrop - 0.12f, d * 0.75f));
            Vector3 hinge = new Vector3(0f, 0f, -d * 0.5f);
            Quaternion lean = Quaternion.Euler(-14f, 0f, 0f);
            m.Box(new Vector3(0f, back * 0.5f, -d * 0.5f + 0.06f), new Vector3(w, back, 0.12f), lean, hinge);
            int heads = Mathf.Max(1, Mathf.RoundToInt(w / 0.5f));
            for (int i = 0; i < heads; i++)
            {
                float x = (i + 0.5f) * w / heads - w * 0.5f;
                m.Box(new Vector3(x, back + 0.14f, -d * 0.5f + 0.05f), new Vector3(Mathf.Min(0.26f, w / heads * 0.6f), 0.18f, 0.1f), lean, hinge);
                m.Box(new Vector3(x, back + 0.03f, -d * 0.5f + 0.05f), new Vector3(0.05f, 0.06f, 0.04f), lean, hinge);
            }
            return m.Build(name);
        }

        /// <summary>
        /// A dashboard whose origin is the middle of its top front edge, reaching back along -Z: a top that drops a little
        /// towards the windscreen, the face below its back edge, and a centre stack running down into a console between
        /// the front seats. Vents and the radio are accent.
        /// </summary>
        internal static Mesh Dash(Vector3 size, string name)
        {
            float w = size.x, h = size.y, d = size.z;
            var m = new KitMesh();
            m.Box(new Vector3(0f, -0.035f, -d * 0.5f), new Vector3(w, 0.07f, d), Quaternion.Euler(-4f, 0f, 0f), new Vector3(0f, 0f, -d));
            m.Box(new Vector3(0f, -h * 0.5f, -d * 0.5f), new Vector3(w * 0.98f, h - 0.04f, d));
            // Centre stack against the face, and the console behind it between the front seats.
            float stackH = Mathf.Max(0.12f, h - 0.08f);
            m.Box(new Vector3(0f, -0.08f - stackH * 0.5f, -d - 0.04f), new Vector3(0.26f, stackH, 0.08f));
            m.Box(new Vector3(0f, -h + 0.1f, -d - 0.3f), new Vector3(0.22f, 0.2f, 0.45f));
            m.Accent();
            foreach (float x in new[] { -w * 0.36f, w * 0.36f })
                m.Box(new Vector3(x, -0.1f, -d - 0.004f), new Vector3(0.15f, 0.05f, 0.01f));
            foreach (float x in new[] { -0.065f, 0.065f })
                m.Box(new Vector3(x, -0.12f, -d - 0.084f), new Vector3(0.09f, 0.045f, 0.01f));
            if (stackH > 0.2f) m.Box(new Vector3(0f, -0.21f, -d - 0.084f), new Vector3(0.18f, 0.05f, 0.01f));
            m.Box(new Vector3(0f, -h + 0.205f, -d - 0.2f), new Vector3(0.05f, 0.01f, 0.08f));
            return m.Build(name);
        }

        /// <summary>
        /// The gauge cluster ahead of the driver, origin in the middle of the top edge where it meets the dashboard face:
        /// a hood reaching back towards the driver over two dials (accent) on the face.
        /// </summary>
        internal static Mesh Gauges(Vector3 size, string name)
        {
            float w = size.x, h = size.y, d = size.z;
            var m = new KitMesh();
            m.Box(new Vector3(0f, -0.012f, -d * 0.5f), new Vector3(w, 0.024f, d));
            m.Box(new Vector3(-w * 0.5f + 0.01f, -h * 0.5f, -d * 0.5f), new Vector3(0.02f, h, d));
            m.Box(new Vector3(w * 0.5f - 0.01f, -h * 0.5f, -d * 0.5f), new Vector3(0.02f, h, d));
            m.Box(new Vector3(0f, -h * 0.5f, -0.005f), new Vector3(w, h, 0.01f));
            m.Accent();
            float r = Mathf.Min(h * 0.36f, w * 0.18f);
            foreach (float x in new[] { -w * 0.22f, w * 0.22f }) m.Disc(x, -h * 0.55f, -0.012f, r);
            // Dark ticks on the pale dials every 45 degrees from -135 to 135, the sweep of the needles.
            m.Accent(false);
            foreach (float x in new[] { -w * 0.22f, w * 0.22f })
            {
                var c = new Vector3(x, -h * 0.55f, -0.013f);
                for (int a = -135; a <= 135; a += 45)
                    m.Box(c + new Vector3(0f, r * 0.82f, 0f), new Vector3(0.0018f, r * 0.24f, 0.001f), Quaternion.Euler(0f, 0f, -a), c);
            }
            return m.Build(name);
        }

        /// <summary>A needle pointing up from its pivot at the origin, a small hub around the pivot, in front of a dial facing -Z.</summary>
        internal static Mesh Needle(Vector3 size, string name)
        {
            float w = size.x, len = size.y, t = Mathf.Max(size.z, 0.001f);
            var m = new KitMesh();
            m.Box(new Vector3(0f, len * 0.4f, 0f), new Vector3(w, len * 1.2f, t));
            m.Box(new Vector3(0f, 0f, -t), new Vector3(w * 2.2f, w * 2.2f, t));
            return m.Build(name);
        }

        /// <summary>A rocker switch around the origin that tips about its x axis, with a pale mark on the upper half of its face.</summary>
        internal static Mesh Rocker(Vector3 size, string name)
        {
            var m = new KitMesh();
            m.Box(Vector3.zero, size);
            m.Accent();
            m.Box(new Vector3(0f, size.y * 0.25f, -size.z * 0.5f - 0.0005f), new Vector3(size.x * 0.5f, size.y * 0.12f, 0.001f));
            return m.Build(name);
        }

        /// <summary>A round push button whose back is at the origin, reaching out along -Z, with a lighter cap.</summary>
        internal static Mesh Button(Vector3 size, string name)
        {
            float r = size.x, d = Mathf.Max(size.z, 0.004f);
            var m = new KitMesh();
            m.Column(r, -d, 0f, 12);
            m.Accent();
            m.Disc(0f, 0f, -d, r, 12);
            return m.Build(name);
        }

        /// <summary>A lever from the origin out along -X with a pale tip.</summary>
        internal static Mesh Stalk(Vector3 size, string name)
        {
            float len = size.x, t = Mathf.Max(size.y, 0.006f);
            var m = new KitMesh();
            m.Box(new Vector3(-len * 0.5f, 0f, 0f), new Vector3(len, t, t));
            m.Accent();
            m.Box(new Vector3(-len - 0.01f, 0f, 0f), new Vector3(0.02f, t * 1.3f, t * 1.3f));
            return m.Build(name);
        }

        /// <summary>
        /// A steering wheel around the origin in the XY plane, the column running along +Z: rim, hub with an accent horn
        /// pad, three flat spokes and the column into a shroud.
        /// </summary>
        internal static Mesh Wheel(float radius, float column, string name)
        {
            var m = new KitMesh();
            m.Ring(radius, 0.024f, 24, 6);
            m.Column(0.06f, -0.005f, 0.045f, 10);
            float spoke = radius - 0.06f;
            m.Box(new Vector3(-(0.06f + radius) * 0.5f, -0.012f, 0.02f), new Vector3(spoke, 0.045f, 0.016f), Quaternion.Euler(0f, 0f, -8f), new Vector3(-0.06f, 0f, 0f));
            m.Box(new Vector3((0.06f + radius) * 0.5f, -0.012f, 0.02f), new Vector3(spoke, 0.045f, 0.016f), Quaternion.Euler(0f, 0f, 8f), new Vector3(0.06f, 0f, 0f));
            m.Box(new Vector3(0f, -(0.06f + radius) * 0.5f, 0.02f), new Vector3(0.05f, spoke, 0.016f));
            m.Column(0.025f, 0.045f, column);
            m.Box(new Vector3(0f, 0f, column - 0.06f), new Vector3(0.09f, 0.09f, 0.14f));
            m.Disc(0f, 0f, -0.005f, 0.06f, 10);
            m.Accent();
            m.Disc(0f, 0f, -0.008f, 0.042f, 10);
            return m.Build(name);
        }
    }
}
