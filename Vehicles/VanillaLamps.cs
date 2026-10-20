using System;
using System.Collections.Generic;
using Chassis.Compat;
using UnityEngine.Rendering;

namespace Chassis.Vehicles
{
    /// <summary>
    /// Amber turn signals for vehicles whose profile names no indicator lamps, which is every vanilla car. Where the model
    /// has indicator lamps of its own (renderers or submeshes with an indicator or blinker material), those glow; a
    /// corner without one borrows the headlight at the front and the brake light at the rear. Each lamp is a copy of its
    /// triangles on one side of the car, lifted a few millimetres off the surface and shown only in the lit half of a
    /// flash, so the vanilla materials are never touched and the headlights and brake lights keep working.
    /// </summary>
    internal static class VanillaLamps
    {
        /// <summary>How far a lamp copy sits in front of the surface it covers, in metres.</summary>
        private const float Lift = 0.004f;
        /// <summary>Triangles closer than this to the car's centre line belong to neither side.</summary>
        private const float CentreGap = 0.05f;

        private static readonly Color Amber = new Color(1f, 0.42f, 0f);

        private sealed class Set
        {
            internal LandVehicle V;
            internal readonly List<GameObject> Left = new List<GameObject>(), Right = new List<GameObject>();
            internal bool Lit;
            internal int Shown;
        }

        private sealed class Source
        {
            internal MeshRenderer R;
            internal int Sub;           // submesh index, -1 for every submesh
            internal int End;           // 0 both ends, 1 front only, -1 rear only
            internal string What;
        }

        private static readonly Dictionary<int, Set> _sets = new Dictionary<int, Set>();
        /// <summary>The lamp copies of one source: its triangles on each side, and which ends of the car they lie at.</summary>
        private sealed class Half
        {
            internal Mesh Left, Right;
            internal bool Front, Rear;
        }

        private static readonly Dictionary<string, Half> _meshes = new Dictionary<string, Half>();
        private static Material _glow;

        /// <summary>Builds the lamps of a signalling vehicle the first time its signal shows.</summary>
        internal static void Ensure(LandVehicle lv)
        {
            if (lv == null) return;
            int key = lv.GetInstanceID();
            if (_sets.ContainsKey(key)) return;
            var set = new Set { V = lv };
            _sets[key] = set;
            try { Build(set); }
            catch (Exception e) { Core.Log.Error("[Indicators] " + Registry.Name(lv) + ": building the turn signal lamps failed, the signal stays dark outside: " + e); }
        }

        /// <summary>Shows the lamps of every built vehicle for the current flash phase.</summary>
        internal static void Paint(bool phase)
        {
            List<int> gone = null;
            foreach (var kv in _sets)
            {
                var set = kv.Value;
                if (set.V == null) { (gone ??= new List<int>()).Add(kv.Key); continue; }
                int side = Indicators.SideOf(set.V);
                bool lit = side != 0 && phase;
                if (lit == set.Lit && side == set.Shown) continue;
                set.Lit = lit; set.Shown = side;
                Show(set.Left, lit && (side < 0 || side == Indicators.Hazard));
                Show(set.Right, lit && (side == 1 || side == Indicators.Hazard));
            }
            if (gone != null) foreach (int k in gone) _sets.Remove(k);
        }

        private static void Show(List<GameObject> lamps, bool on)
        {
            foreach (var go in lamps) if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        private static void Build(Set set)
        {
            var lv = set.V;
            HashSet<int> lower = HigherLods(lv);
            var sources = new List<Source>();
            foreach (var r in lv.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.name.StartsWith("Chassis_", StringComparison.Ordinal)) continue;
                if (lower.Contains(r.GetInstanceID())) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && IsIndicator(mats[i].name))
                        sources.Add(new Source { R = r, Sub = mats.Length == 1 ? -1 : i, What = "indicator " + r.name });
            }

            int left = 0, right = 0;
            bool front = false, rear = false;
            var used = new List<string>();
            AddLamps(set, sources, used, ref left, ref right, ref front, ref rear);
            // A corner the model has no indicator for borrows the headlight at the front and the brake light at the rear.
            var vl = lv.GetComponent<VehicleLights>();
            var borrowed = new List<Source>();
            if (!front && vl != null) AddLights(borrowed, vl.headLightMeshes, 1, "headlight");
            if (!rear && vl != null) AddLights(borrowed, vl.brakeLightMeshes, -1, "brake light");
            AddLamps(set, borrowed, used, ref left, ref right, ref front, ref rear);
            if (left + right == 0) Core.Log.Warning("[Indicators] " + Registry.Name(lv) + " has no lamp a turn signal could light - it flashes on the dashboard only.");
            else Core.Log.Msg("[Indicators] " + Registry.Name(lv) + ": " + left + " left and " + right + " right lamp(s) from " + string.Join(", ", used) + ".");
        }

        private static void AddLamps(Set set, List<Source> sources, List<string> used, ref int left, ref int right, ref bool front, ref bool rear)
        {
            var lv = set.V;
            Transform root = lv.transform;
            foreach (var s in sources)
            {
                var mf = s.R.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) { Core.Log.Warning("[Indicators] " + Registry.Name(lv) + ": " + s.What + " has no mesh, skipped."); continue; }
                Matrix4x4 toRoot = root.worldToLocalMatrix * s.R.transform.localToWorldMatrix;
                var halves = Halves(mf.sharedMesh, s, toRoot, lv.vehicleCode, out string report);
                if (halves == null) { Core.Log.Warning("[Indicators] " + Registry.Name(lv) + ": " + s.What + " could not be copied (" + report + "), skipped."); continue; }
                if (halves.Left != null) { set.Left.Add(Lamp(s.R.transform, halves.Left, "Chassis_Indicator_L")); left++; }
                if (halves.Right != null) { set.Right.Add(Lamp(s.R.transform, halves.Right, "Chassis_Indicator_R")); right++; }
                front |= halves.Front; rear |= halves.Rear;
                used.Add(s.What + " (" + report + ")");
            }
        }

        private static bool IsIndicator(string material)
            => material.IndexOf("indicator", StringComparison.OrdinalIgnoreCase) >= 0 || material.IndexOf("blinker", StringComparison.OrdinalIgnoreCase) >= 0;

        private static void AddLights(List<Source> into, MeshRenderer[] lights, int end, string what)
        {
            if (lights == null) return;
            foreach (var r in lights) if (r != null) into.Add(new Source { R = r, Sub = -1, End = end, What = what + " " + r.name });
        }

        /// <summary>The renderers that only lower LODs use; their lamps would double the ones of LOD 0.</summary>
        private static HashSet<int> HigherLods(LandVehicle lv)
        {
            var set = new HashSet<int>();
            var lod = lv.GetComponentInChildren<LODGroup>(true);
            if (lod == null) return set;
            try
            {
                var lods = lod.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers) if (r != null) set.Add(r.GetInstanceID());
                // A renderer in LOD 0 as well (small lamps often hang in every LOD) stays.
                if (lods.Length > 0) foreach (var r in lods[0].renderers) if (r != null) set.Remove(r.GetInstanceID());
            }
            catch (Exception e) { Core.Log.Warning("[Indicators] reading the LOD group of " + Registry.Name(lv) + " failed - lower LODs may get lamps too: " + e.Message); }
            return set;
        }

        private static GameObject Lamp(Transform parent, Mesh mesh, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Glow();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return go;
        }

        /// <summary>The glowing amber material of every lamp, made once from the URP Lit shader.</summary>
        private static Material Glow()
        {
            if (_glow != null) return _glow;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) { Core.Log.Warning("[Indicators] the URP Lit shader was not found - the lamps use the default shader."); shader = Shader.Find("Standard"); }
            _glow = new Material(shader) { name = "Chassis_IndicatorGlow" };
            _glow.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            _glow.SetColor("_BaseColor", Amber);
            _glow.EnableKeyword("_EMISSION");
            _glow.SetColor("_EmissionColor", Amber * 4f);
            _glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return _glow;
        }

        /// <summary>
        /// The triangles of the source on the car's left and right, each lifted along its normal; null for a side without
        /// any. The mesh is read back from the GPU, as the vanilla meshes are not readable on the CPU.
        /// </summary>
        private static Half Halves(Mesh src, Source s, Matrix4x4 toRoot, string code, out string report)
        {
            // The same mesh can sit mirrored on both sides of a car, so its place in the car is part of the key.
            string key = code + "|" + src.GetInstanceID() + "|" + s.Sub + "|" + s.End + "|" + toRoot.ToString("F3");
            if (_meshes.TryGetValue(key, out var done)) { report = "copy reused"; return done; }
            Half made = null;
            try { made = Split(src, s, toRoot, out report); }
            catch (Exception e) { report = "read-back threw: " + e.Message; }
            _meshes[key] = made;
            return made;
        }

        private static Half Split(Mesh src, Source s, Matrix4x4 toRoot, out string report)
        {
            int count = src.vertexCount;
            if (count <= 0) { report = "empty mesh"; return null; }
            if (src.vertexBufferCount != 1) { report = src.vertexBufferCount + " vertex streams"; return null; }
            if (src.GetVertexAttributeFormat(VertexAttribute.Position) != VertexAttributeFormat.Float32) { report = "positions are not Float32"; return null; }
            int stride = src.GetVertexBufferStride(0);
            if (stride <= 0 || stride % 4 != 0) { report = "vertex stride " + stride; return null; }
            int step = stride / 4, pAt = src.GetVertexAttributeOffset(VertexAttribute.Position) / 4;
            int nAt = src.HasVertexAttribute(VertexAttribute.Normal) && src.GetVertexAttributeFormat(VertexAttribute.Normal) == VertexAttributeFormat.Float32
                ? src.GetVertexAttributeOffset(VertexAttribute.Normal) / 4 : -1;

            float[] vdata;
            var vbuf = src.GetVertexBuffer(0);
            if (vbuf == null) { report = "no GPU vertex buffer"; return null; }
            try { vdata = Interop.ReadFloats(vbuf, count * step); }
            finally { vbuf.Release(); }
            int[] indices;
            var ibuf = src.GetIndexBuffer();
            if (ibuf == null) { report = "no GPU index buffer"; return null; }
            try { indices = Interop.ReadIndices(ibuf, ibuf.count, src.indexFormat == IndexFormat.UInt32); }
            finally { ibuf.Release(); }

            var pos = new Vector3[count];
            for (int v = 0; v < count; v++) { int i = v * step + pAt; pos[v] = new Vector3(vdata[i], vdata[i + 1], vdata[i + 2]); }

            bool front = false, rear = false;
            var left = new List<int>();
            var right = new List<int>();
            int first = s.Sub < 0 ? 0 : s.Sub, last = s.Sub < 0 ? src.subMeshCount - 1 : s.Sub;
            for (int sub = first; sub <= last; sub++)
            {
                var d = src.GetSubMesh(sub);
                if (d.topology != MeshTopology.Triangles) continue;
                for (int t = d.indexStart; t + 2 < d.indexStart + d.indexCount; t += 3)
                {
                    int a = indices[t] + d.baseVertex, b = indices[t + 1] + d.baseVertex, c = indices[t + 2] + d.baseVertex;
                    Vector3 centre = toRoot.MultiplyPoint3x4((pos[a] + pos[b] + pos[c]) / 3f);
                    if (s.End > 0 && centre.z < 0f || s.End < 0 && centre.z > 0f) continue;
                    if (centre.x < -CentreGap) { left.Add(a); left.Add(b); left.Add(c); }
                    else if (centre.x > CentreGap) { right.Add(a); right.Add(b); right.Add(c); }
                    else continue;
                    if (centre.z > 0f) front = true; else rear = true;
                }
            }
            if (left.Count + right.Count == 0) { report = "no triangles off the centre line"; return null; }
            report = left.Count / 3 + "+" + right.Count / 3 + " triangles";
            return new Half { Left = Copy(src.name, "L", pos, vdata, step, nAt, left), Right = Copy(src.name, "R", pos, vdata, step, nAt, right), Front = front, Rear = rear };
        }

        /// <summary>A mesh of just these triangles, every vertex moved <see cref="Lift"/> along its normal.</summary>
        private static Mesh Copy(string name, string side, Vector3[] pos, float[] vdata, int step, int nAt, List<int> tris)
        {
            if (tris.Count == 0) return null;
            var map = new Dictionary<int, int>();
            var verts = new List<Vector3>();
            var idx = new int[tris.Count];
            var normals = new List<Vector3>();
            for (int i = 0; i < tris.Count; i++)
            {
                int v = tris[i];
                if (!map.TryGetValue(v, out int n))
                {
                    n = verts.Count;
                    map[v] = n;
                    Vector3 normal = nAt >= 0 ? new Vector3(vdata[v * step + nAt], vdata[v * step + nAt + 1], vdata[v * step + nAt + 2]) : Vector3.zero;
                    verts.Add(pos[v] + normal.normalized * Lift);
                    normals.Add(normal);
                }
                idx[i] = n;
            }
            var m = new Mesh { name = name + "_Chassis_Indicator_" + side };
            if (verts.Count > 65535) m.indexFormat = IndexFormat.UInt32;
            m.vertices = verts.ToArray();
            m.SetTriangles(idx, 0);
            if (nAt >= 0) m.normals = normals.ToArray(); else m.RecalculateNormals();
            m.RecalculateBounds();
            m.UploadMeshData(true);
            m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return m;
        }

        internal static IEnumerable<string> Describe(LandVehicle lv)
        {
            if (lv == null || !_sets.TryGetValue(lv.GetInstanceID(), out var set)) { yield return "  turn signal lamps: not built (no signal yet)"; yield break; }
            foreach (var go in set.Left) if (go != null) yield return "  lamp L under " + go.transform.parent.name + " active=" + go.activeSelf;
            foreach (var go in set.Right) if (go != null) yield return "  lamp R under " + go.transform.parent.name + " active=" + go.activeSelf;
        }

        internal static void Reset() => _sets.Clear();
    }
}
