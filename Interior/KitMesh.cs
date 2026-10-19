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

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            // Unity's front faces wind clockwise; the corners come counter-clockwise as seen from outside.
            _t.Add(i); _t.Add(i + 2); _t.Add(i + 1);
            _t.Add(i); _t.Add(i + 3); _t.Add(i + 2);
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
            mesh.triangles = _t.ToArray();
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

        /// <summary>A dashboard whose origin is the middle of its top front edge, reaching back along -Z.</summary>
        internal static Mesh Dash(Vector3 size, string name)
        {
            float w = size.x, h = size.y, d = size.z;
            var m = new KitMesh();
            m.Box(new Vector3(0f, -0.04f, -d * 0.5f), new Vector3(w, 0.08f, d));
            m.Box(new Vector3(0f, -h * 0.5f, -d * 0.35f), new Vector3(w * 0.98f, h - 0.08f, d * 0.7f));
            return m.Build(name);
        }

        /// <summary>A steering wheel around the origin in the XY plane, the column running along +Z.</summary>
        internal static Mesh Wheel(float radius, float column, string name)
        {
            var m = new KitMesh();
            m.Ring(radius, 0.022f);
            m.Box(new Vector3(0f, 0f, 0.01f), new Vector3(0.11f, 0.09f, 0.05f));
            float spoke = radius - 0.05f;
            m.Box(new Vector3(-(0.05f + spoke) * 0.5f, 0f, 0.01f), new Vector3(spoke - 0.02f, 0.035f, 0.02f));
            m.Box(new Vector3((0.05f + spoke) * 0.5f, 0f, 0.01f), new Vector3(spoke - 0.02f, 0.035f, 0.02f));
            m.Box(new Vector3(0f, -(0.04f + spoke) * 0.5f, 0.01f), new Vector3(0.035f, spoke - 0.02f, 0.02f));
            m.Column(0.03f, 0.03f, column);
            return m.Build(name);
        }
    }
}
