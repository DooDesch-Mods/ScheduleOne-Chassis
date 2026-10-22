using System.Collections.Generic;
using Chassis.Api;

namespace Chassis.Seating
{
    /// <summary>
    /// The hands closed around the steering wheel's rim (wheel mode with a <see cref="HandProfile.RimRadius"/>).
    ///
    /// Each hand comes onto the rim from the driver's side and the outside: the knuckles run along the rim, the palm
    /// lies against it, the fingers go round the far side and the thumb lies on the inside. BipedIK places the wrist and
    /// turns the hand; the fingers are not part of the IK, so they are curled here after the animator wrote them.
    ///
    /// Nothing here depends on how the rig's bones are oriented: each hand's own frame is read from where its knuckles
    /// sit (wrist to middle knuckle, little finger to index knuckle), and every finger joint curls about the knuckle line
    /// in the direction that moves its tip towards the palm.
    /// </summary>
    internal sealed partial class SeatRig
    {
        /// <summary>
        /// Where on the rim's round section the palm lies, in degrees: 0 on the outside edge, 90 on the driver's side. In
        /// between the forearm comes from the driver and the fingers reach round the far side.
        /// </summary>
        internal static float GripWrap = 40f;

        /// <summary>Half the thickness of a hand at the palm; the hand's middle sits this far off the rim's surface.</summary>
        internal static float PalmHalf = 0.016f;

        /// <summary>
        /// Where the rim crosses the palm, from the wrist (0) to the finger roots (1). The rim lies just behind the finger
        /// roots, so the fingers can close round it.
        /// </summary>
        internal static float GripAt = 0.85f;

        /// <summary>Degrees each finger bends at its first, middle and last joint around the rim.</summary>
        internal static float Curl1 = 70f, Curl2 = 80f, Curl3 = 70f;

        /// <summary>Degrees the thumb bends at its base, middle and last joint, towards the palm.</summary>
        internal static float Thumb1 = 20f, Thumb2 = 30f, Thumb3 = 30f;

        /// <summary>
        /// Hands without a little finger: degrees from the thumb's root up to the knuckle line, about the fingers. The
        /// thumb grows out of the palm side, below the line the knuckles make.
        /// </summary>
        internal static float KnuckleRoll = 40f;

        private HandRig _handL, _handR;
        private bool _rimGrip, _handsRead;

        private sealed class Joint
        {
            internal Transform T;
            internal Quaternion Base, Written;
            internal Vector3 Axis;     // curl axis in the hand bone's space
            internal int Finger;       // 0 thumb, 1-4 index to little finger
            internal int Step;         // 1 = base joint
        }

        private sealed class HandRig
        {
            internal Transform Bone;
            internal float Side;
            /// <summary>Hand bone rotation removed (world metres): wrist to middle knuckle, little finger to index knuckle.</summary>
            internal Vector3 Fingers, Knuckles;
            internal readonly List<Joint> Joints = new List<Joint>();
            internal bool Curled;
            /// <summary>Knuckle, finger joints and tip of the finger the fist is measured on (middle, else index).</summary>
            internal readonly List<Transform> Loop = new List<Transform>();
            /// <summary>Middle of the closed fingers' loop in the hand bone's space, from the last curled frame.</summary>
            internal Vector3 Fist;
            internal bool FistSeen;
        }

        private static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

        /// <summary>Reads one hand's frame and finger joints, or returns null with the reason logged.</summary>
        private HandRig BuildHand(IKSolverLimb limb, float side)
        {
            Transform hand = limb?.bone3?.transform;
            string tag = side > 0f ? "right" : "left";
            if (hand == null) { Core.Log.Warning("[Seat] " + Name + ": no " + tag + " hand bone, that hand keeps the plain wheel grip."); return null; }
            var bones = new Dictionary<string, Transform>();
            foreach (var t in hand.GetComponentsInChildren<Transform>(true))
                foreach (string f in FingerNames)
                    for (int n = 1; n <= 3; n++)
                        if (t.name.EndsWith(f + n)) bones[f + n] = t;
            // The game's bodies have one index chain for all four fingers and a thumb; a full hand has every finger.
            bones.TryGetValue("Index1", out var index1);
            bones.TryGetValue("Thumb1", out var thumb1);
            bones.TryGetValue("Pinky1", out var pinky1);
            Transform knuckle = bones.TryGetValue("Middle1", out var middle1) ? middle1 : index1;
            if (knuckle == null || (pinky1 == null && thumb1 == null) || (pinky1 != null && index1 == null))
            {
                Core.Log.Warning("[Seat] " + Name + "'s " + tag + " hand has no finger and thumb bones to read it from (" + bones.Count + " finger bones) - that hand keeps the plain wheel grip.");
                return null;
            }
            Vector3 fingers = knuckle.position - hand.position;
            // Little finger to index knuckle; without a little finger, towards the thumb's root across the fingers,
            // turned by KnuckleRoll because the thumb sits below the knuckle line, on the palm's side.
            Vector3 knuckles = pinky1 != null ? index1.position - pinky1.position
                : Quaternion.AngleAxis(-side * KnuckleRoll, fingers) * Vector3.ProjectOnPlane(thumb1.position - hand.position, fingers);
            if (knuckles.sqrMagnitude < 1e-8f || fingers.sqrMagnitude < 1e-8f)
            {
                Core.Log.Warning("[Seat] " + Name + "'s " + tag + " hand bones sit on one point - that hand keeps the plain wheel grip.");
                return null;
            }

            Quaternion inv = Quaternion.Inverse(hand.rotation);
            var h = new HandRig
            {
                Bone = hand,
                Side = side,
                Fingers = inv * fingers,
                Knuckles = inv * knuckles,
            };
            // Out of the palm: fingers forward and the index finger on top, a right palm faces left, a left one right.
            Vector3 palm = (side * Vector3.Cross(fingers, knuckles)).normalized;
            Vector3 knuckleLine = Vector3.ProjectOnPlane(knuckles, fingers).normalized;

            for (int f = 0; f < FingerNames.Length; f++)
            {
                for (int n = 1; n <= 3; n++)
                {
                    if (!bones.TryGetValue(FingerNames[f] + n, out var t)) continue;
                    Transform next = bones.TryGetValue(FingerNames[f] + (n + 1), out var nx) ? nx : (t.childCount > 0 ? t.GetChild(0) : null);
                    if (next == null) continue;
                    Vector3 seg = next.position - t.position;
                    if (seg.sqrMagnitude < 1e-8f) continue;
                    // Fingers bend about the knuckle line; the thumb about the line across its own segment and the palm.
                    Vector3 axis = f == 0 ? Vector3.Cross(seg, palm) : knuckleLine;
                    if (axis.sqrMagnitude < 1e-8f) continue;
                    axis.Normalize();
                    if (Vector3.Dot(Quaternion.AngleAxis(10f, axis) * seg - seg, palm) < 0f) axis = -axis;
                    h.Joints.Add(new Joint { T = t, Base = t.localRotation, Written = t.localRotation, Axis = inv * axis, Finger = f, Step = n });
                }
            }
            // The loop the closed fingers make: the rim goes through its middle.
            string loopFinger = middle1 != null ? "Middle" : "Index";
            for (int n = 1; n <= 3; n++) if (bones.TryGetValue(loopFinger + n, out var lt)) h.Loop.Add(lt);
            if (h.Loop.Count > 0)
            {
                Transform last = h.Loop[h.Loop.Count - 1];
                if (last.childCount > 0) h.Loop.Add(last.GetChild(0));
                if (h.Loop[h.Loop.Count - 1].childCount > 0) h.Loop.Add(h.Loop[h.Loop.Count - 1].GetChild(0));
            }
            Core.Log.Msg("[Seat] " + Name + "'s " + tag + " hand: " + h.Joints.Count + " finger joints to close around the rim, knuckles "
                + h.Fingers.magnitude.ToString("F3") + " m from the wrist, hand space fingers " + h.Fingers.normalized.ToString("F2") + " knuckle line "
                + h.Knuckles.normalized.ToString("F2") + (pinky1 == null ? " (from the thumb)" : "") + ".");
            return h;
        }

        /// <summary>
        /// Both wrist targets on the rim at rest, before <see cref="TurnWithWheel"/> turns them with the wheel. False when
        /// the wheel or a hand is missing; the hands then take the plain grip at <see cref="HandProfile.GripStart"/>.
        /// </summary>
        private bool PlaceRimGrips(HandProfile h)
        {
            if (h.RimRadius <= 0f) return false;
            if (!_handsRead)
            {
                // Once per seating, also when a profile gets a rim radius while the body already sits.
                _handsRead = true;
                var s = _ik.BodyIK.solvers;
                _handL = BuildHand(s.leftHand, -1f);
                _handR = BuildHand(s.rightHand, 1f);
            }
            if (_handL == null || _handR == null) return false;
            if (!Vehicles.Parts.Wheel(_lv, out Transform pivot, out Vector3 axis, out _)) return false;
            Quaternion seatRot = _frame.rotation * Quaternion.Euler(0f, _seat.Yaw, 0f);
            Vector3 n = axis.normalized;
            // The column's axis towards the driver.
            if (Vector3.Dot(_frame.TransformPoint(_seat.Eyes) - pivot.position, n) < 0f) n = -n;
            Vector3 right = Vector3.ProjectOnPlane(seatRot * Vector3.right, n).normalized;
            Vector3 top = Vector3.Cross(n, right).normalized;
            if (Vector3.Dot(top, seatRot * Vector3.up) + Vector3.Dot(top, seatRot * Vector3.forward) < 0f) top = -top;
            Vector3 centre = pivot.position + n * h.RimOffset;
            Place(_gripR, _handR, h, centre, n, right, top);
            Place(_gripL, _handL, h, centre, n, right, top);
            return true;
        }

        private static void Place(Transform grip, HandRig hand, HandProfile h, Vector3 centre, Vector3 n, Vector3 right, Vector3 top)
        {
            float a = h.GripAngle * Mathf.Deg2Rad, b = GripWrap * Mathf.Deg2Rad;
            Vector3 radial = right * (hand.Side * Mathf.Cos(a)) + top * Mathf.Sin(a);
            Vector3 along = -right * (hand.Side * Mathf.Sin(a)) + top * Mathf.Cos(a);     // along the rim, towards the top
            Vector3 handSide = (radial * Mathf.Cos(b) + n * Mathf.Sin(b)).normalized;      // where the palm lies on the rim
            Vector3 fingers = (radial * Mathf.Sin(b) - n * Mathf.Cos(b)).normalized;       // round the far side
            Quaternion rot = Quaternion.LookRotation(fingers, along) * Quaternion.Inverse(Quaternion.LookRotation(hand.Fingers, hand.Knuckles));
            if (hand.FistSeen)
            {
                // The rim's middle line runs through the middle of the closed fingers.
                grip.position = centre + radial * h.RimRadius - rot * hand.Fist;
            }
            else
            {
                Vector3 contact = centre + radial * h.RimRadius + handSide * (h.RimThickness * 0.5f + PalmHalf);
                grip.position = contact - rot * (hand.Fingers * GripAt);
            }
            grip.rotation = rot;
        }

        /// <summary>Every peer, after the animator: the fingers close around the rim, or open again when the grip ends.</summary>
        internal void PoseFingers()
        {
            bool on = _rimGrip && _ikOn && Alive;
            Curl(_handR, on);
            Curl(_handL, on);
        }

        private static void Curl(HandRig h, bool on)
        {
            if (h == null || (!on && !h.Curled)) return;
            foreach (var j in h.Joints)
            {
                if (j.T == null || h.Bone == null) continue;
                Quaternion now = j.T.localRotation;
                // A joint the animator wrote this frame starts from the animation again; one it leaves alone from the pose before the curl.
                if (!h.Curled || Quaternion.Angle(now, j.Written) > 0.01f) j.Base = now;
                if (on)
                {
                    // The axis is read in the hand's space, so it follows the joints before it that already bent.
                    Quaternion parent = j.T.parent != null ? j.T.parent.rotation : Quaternion.identity;
                    Vector3 local = Quaternion.Inverse(parent * j.Base) * (h.Bone.rotation * j.Axis);
                    j.Written = j.Base * Quaternion.AngleAxis(Bend(j), local);
                    j.T.localRotation = j.Written;
                }
                else j.T.localRotation = j.Base;
            }
            h.Curled = on;
            if (on && h.Loop.Count > 1 && h.Bone != null)
            {
                Vector3 sum = Vector3.zero;
                foreach (var t in h.Loop) sum += t != null ? t.position : h.Bone.position;
                h.Fist = Quaternion.Inverse(h.Bone.rotation) * (sum / h.Loop.Count - h.Bone.position);
                h.FistSeen = true;
            }
        }

        private static float Bend(Joint j)
        {
            if (j.Finger == 0) return j.Step == 1 ? Thumb1 : j.Step == 2 ? Thumb2 : Thumb3;
            return j.Step == 1 ? Curl1 : j.Step == 2 ? Curl2 : Curl3;
        }

        /// <summary>From <see cref="Core"/>'s late update: every seated body's fingers.</summary>
        internal static void LateTick()
        {
            foreach (var rig in Occupants.All) rig.LateFrame();
            foreach (var rig in NpcRiders.All) rig.LateFrame();
        }

        private void LateFrame()
        {
            PoseFingers();
        }

        /// <summary>On leaving the seat: the fingers back to the animation, debug markers gone.</summary>
        private void ReleaseGrip()
        {
            _rimGrip = false;
            Curl(_handR, false);
            Curl(_handL, false);
#if DEBUG
            DestroyMarkers();
#endif
        }

#if DEBUG
        /// <summary>Where the local body's hands are against their targets, for the console.</summary>
        internal string DescribeGrip()
        {
            string Hand(HandRig h, Transform grip, string tag)
            {
                if (h == null || h.Bone == null) return tag + " none";
                return tag + " wrist off target " + (grip != null ? (h.Bone.position - grip.position).magnitude.ToString("F3") : "-") + " m, turn off "
                    + (grip != null ? Quaternion.Angle(h.Bone.rotation, grip.rotation).ToString("F0") : "-") + " deg, curled " + h.Curled;
            }
            return Name + " rim grip " + _rimGrip + " | " + Hand(_handR, _gripR, "R") + " | " + Hand(_handL, _gripL, "L");
        }

        /// <summary>
        /// Where the hands were after last frame's IK, for the look camera: -1 left, 1 right, 0 between them. Null without
        /// hands. Read in <see cref="Pose"/>, before the animator moves them back for this frame.
        /// </summary>
        internal Vector3? HandAt(int which) => !_handSeen ? (Vector3?)null : which < 0 ? _seenL : which > 0 ? _seenR : (_seenL + _seenR) * 0.5f;

        private Vector3 _seenL, _seenR;
        private bool _handSeen;

        /// <summary>Start of <see cref="Pose"/>: the bones still hold last frame's IK pose.</summary>
        private void SeeHands()
        {
            _handSeen = _handL?.Bone != null && _handR?.Bone != null;
            if (_handSeen) { _seenL = _handL.Bone.position; _seenR = _handR.Bone.position; }
            DrawMarkers();
        }

        // ---- colour markers (chassis grip markers) -------------------------------------------------------------------

        /// <summary>Dev console: draws the rim and the hands' targets in colour on every seated body.</summary>
        internal static bool Markers;

        private readonly List<Transform> _markers = new List<Transform>();
        private static Material _green, _blue, _yellow;

        private static Material Mat(ref Material m, Color c)
        {
            if (m != null) return m;
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) { Core.Log.Error("[Seat] grip markers: neither URP shader found, markers stay grey."); return null; }
            m = new Material(sh);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Cull", 0f);   // both faces, whatever the winding
            m.color = c;
            return m;
        }

        private Transform Marker(int i, Material mat, float size)
        {
            while (_markers.Count <= i)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "ChassisGripMarker";
                var col = go.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);
                _markers.Add(go.transform);
            }
            var t = _markers[i];
            if (t == null) return null;
            t.gameObject.SetActive(true);
            t.localScale = Vector3.one * size;
            var r = t.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            return t;
        }

        /// <summary>
        /// From last frame, after its IK: white ring = the rim the hands should hold, yellow = where the rim should cross each palm, green =
        /// the wrist targets, blue = where the wrists really are.
        /// </summary>
        internal void DrawMarkers()
        {
            if (!Markers || !_rimGrip || !Alive || _seat?.Hands == null) { HideMarkers(); return; }
            if (!Vehicles.Parts.Wheel(_lv, out Transform pivot, out Vector3 axis, out _)) { HideMarkers(); return; }
            var h = _seat.Hands;
            Vector3 n = axis.normalized;
            if (Vector3.Dot(_frame.TransformPoint(_seat.Eyes) - pivot.position, n) < 0f) n = -n;
            Ring(pivot.position + n * h.RimOffset, n, h.RimRadius, h.RimThickness * 0.5f + 0.003f);
            int i = 0;
            foreach (var (hand, grip) in new[] { (_handR, _gripR), (_handL, _gripL) })
            {
                if (hand?.Bone == null || grip == null) continue;
                var y = Marker(i++, Mat(ref _yellow, Color.yellow), 0.02f);
                if (y != null) y.position = grip.position + grip.rotation * (hand.FistSeen ? hand.Fist : hand.Fingers * GripAt);
                var g = Marker(i++, Mat(ref _green, Color.green), 0.025f);
                if (g != null) g.position = grip.position;
                var bl = Marker(i++, Mat(ref _blue, Color.blue), 0.022f);
                if (bl != null) bl.position = hand.Bone.position;
            }
            for (; i < _markers.Count; i++) if (_markers[i] != null) _markers[i].gameObject.SetActive(false);
        }

        private Transform _ring;
        private float _ringR, _ringT;
        private static Material _white;

        /// <summary>A white tube over the rim the hands should hold, a little thicker than the rim so it covers it.</summary>
        private void Ring(Vector3 centre, Vector3 axis, float radius, float tube)
        {
            if (_ring == null || _ringR != radius || _ringT != tube)
            {
                if (_ring != null) UnityEngine.Object.Destroy(_ring.gameObject);
                var go = new GameObject("ChassisGripMarker");
                go.AddComponent<MeshFilter>().sharedMesh = Torus(radius, tube, 48, 10);
                var mr = go.AddComponent<MeshRenderer>();
                var mat = Mat(ref _white, Color.white);
                if (mat != null) mr.sharedMaterial = mat;
                _ring = go.transform;
                _ringR = radius; _ringT = tube;
            }
            _ring.gameObject.SetActive(true);
            _ring.position = centre;
            _ring.rotation = Quaternion.LookRotation(axis, Vector3.ProjectOnPlane(_frame.up, axis));
        }

        /// <summary>A torus about the local z axis.</summary>
        private static Mesh Torus(float radius, float tube, int seg, int sides)
        {
            var v = new Vector3[seg * sides];
            var tri = new int[seg * sides * 6];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 r = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                for (int j = 0; j < sides; j++)
                {
                    float b = j * Mathf.PI * 2f / sides;
                    v[i * sides + j] = r * (radius + Mathf.Cos(b) * tube) + Vector3.forward * (Mathf.Sin(b) * tube);
                    int i2 = (i + 1) % seg, j2 = (j + 1) % sides, k = (i * sides + j) * 6;
                    tri[k] = i * sides + j; tri[k + 1] = i2 * sides + j; tri[k + 2] = i2 * sides + j2;
                    tri[k + 3] = i * sides + j; tri[k + 4] = i2 * sides + j2; tri[k + 5] = i * sides + j2;
                }
            }
            var m = new Mesh { name = "ChassisGripRing" };
            m.vertices = v;
            m.triangles = tri;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private void HideMarkers()
        {
            if (_ring != null && _ring.gameObject.activeSelf) _ring.gameObject.SetActive(false);
            foreach (var m in _markers) if (m != null && m.gameObject.activeSelf) m.gameObject.SetActive(false);
        }

        private void DestroyMarkers()
        {
            if (_ring != null) UnityEngine.Object.Destroy(_ring.gameObject);
            _ring = null;
            foreach (var m in _markers) if (m != null) UnityEngine.Object.Destroy(m.gameObject);
            _markers.Clear();
        }

        /// <summary>Opens the hands and reads their bones again on the next frame, after the knuckle roll changed.</summary>
        private void ReadHandsAgain()
        {
            Curl(_handR, false);
            Curl(_handL, false);
            _handL = _handR = null;
            _handsRead = false;
        }

        /// <summary>chassis grip [markers on|off] | [wrap palm f1 f2 f3 t1 t2 t3 roll at]: the grip's live values.</summary>
        internal static string Tune(string[] p)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (p.Length > 3 && p[2] == "markers")
            {
                Markers = p[3] != "off";
                return "grip markers " + (Markers ? "on (white rim, yellow rim on the palm, green wrist target, blue wrist)" : "off");
            }
            float[] v = { GripWrap, PalmHalf, Curl1, Curl2, Curl3, Thumb1, Thumb2, Thumb3, KnuckleRoll, GripAt };
            for (int k = 2; k < p.Length && k - 2 < v.Length; k++)
                if (!float.TryParse(p[k], System.Globalization.NumberStyles.Float, ci, out v[k - 2])) return "grip: '" + p[k] + "' is not a number";
            GripAt = v[9];
            GripWrap = v[0]; PalmHalf = v[1]; Curl1 = v[2]; Curl2 = v[3]; Curl3 = v[4]; Thumb1 = v[5]; Thumb2 = v[6]; Thumb3 = v[7];
            if (v[8] != KnuckleRoll)
            {
                KnuckleRoll = v[8];
                foreach (var r in Occupants.All) r.ReadHandsAgain();
                foreach (var r in NpcRiders.All) r.ReadHandsAgain();
            }
            string Desc(SeatRig r) => r.DescribeGrip();
            var sb = new System.Text.StringBuilder("grip wrap " + GripWrap.ToString(ci) + " palm " + PalmHalf.ToString(ci) + " fingers " + Curl1.ToString(ci) + "/" + Curl2.ToString(ci) + "/" + Curl3.ToString(ci)
                + " thumb " + Thumb1.ToString(ci) + "/" + Thumb2.ToString(ci) + "/" + Thumb3.ToString(ci) + " roll " + KnuckleRoll.ToString(ci) + " at " + GripAt.ToString(ci));
            foreach (var r in Occupants.All) sb.Append("\n").Append(Desc(r));
            foreach (var r in NpcRiders.All) sb.Append("\n").Append(Desc(r));
            return sb.ToString();
        }
#endif
    }
}
