using System.Globalization;

namespace Chassis.Seating
{
    /// <summary>
    /// One hand of a seated body leaves its grip, reaches for a switch and comes back (<see cref="HandReach"/>).
    ///
    /// The reach runs on top of the normal grip: the wrist target moves from the grip to a point a hand's length in
    /// front of the switch, along the line from the shoulder, and back. The hand nearer the switch reaches. A switch
    /// further than the arm is long (the glovebox from the driver's seat) leans the upper body towards it. The head looks
    /// at the switch while the hand is out; the ignition key turns at the end of the reach.
    /// </summary>
    internal sealed partial class SeatRig
    {
        /// <summary>Speed of the hand towards the switch in m/s; the time out is clamped to <see cref="ReachOutMin"/>-<see cref="ReachOutMax"/>.</summary>
        internal static float ReachSpeed = 2.4f;
        internal const float ReachOutMin = 0.16f, ReachOutMax = 0.42f;
        /// <summary>Seconds the hand stays on a switch, on the ignition key, and on the way back.</summary>
        internal static float ReachHold = 0.15f, KeyHold = 0.35f, ReachBack = 0.32f;
        /// <summary>Fingertips sit this many wrist-to-knuckle lengths in front of the wrist.</summary>
        internal static float TipFactor = 1.8f;
        /// <summary>Degrees the ignition key turns at the end of the reach.</summary>
        internal static float KeyTurn = 45f;
        /// <summary>How far the upper body leans towards a switch out of the arm's reach, at most, and how hard.</summary>
        internal static float LeanMax = 0.35f, LeanWeight = 0.8f;
        /// <summary>Debug: extra seconds a hand stays on the switch, for screenshots.</summary>
        internal static float DebugHold;

        private Instruments.Controls.Bound _reachB;
        private float _reachT, _reachOut, _reachHoldS, _reachSide, _reachArm;
        private bool _reachKey;
        private Vector3 _reachShoulder;      // in the frame's space, read when the reach starts
        private Vector3 _reachTop;           // the top spine bone in the frame's space, read when the reach starts
        private Vector3 _reachWrist;         // last frame's wrist target, world
        private bool _reachMeasure;
        private float _reachTipMin, _reachWristMin, _reachLean, _reachPeak;
        private string _reachLast = "none yet";

        private bool Reaching(float side) => _reachB != null && _reachSide == side;

        /// <summary>
        /// Starts a reach for a switch. <paramref name="outSeconds"/> &lt; 0 takes the time from the distance. Returns the
        /// seconds until the hand arrives, or -1 with the reason.
        /// </summary>
        internal float ReachFor(Instruments.Controls.Bound b, float outSeconds, out string why)
        {
            why = null;
            if (!Alive || Player == null || IsPusher) { why = "nobody seated there"; return -1f; }
            if (!_ikOn || _ik?.BodyIK == null || _seat?.Hands == null) { why = Name + "'s body has no hand targets"; return -1f; }
            if (b?.T == null) { why = "the switch is gone"; return -1f; }
            var s = _ik.BodyIK.solvers;
            Vector3 point = b.T.TransformPoint(b.BoxCenter);
            Quaternion seatRot = _frame.rotation * Quaternion.Euler(0f, _seat.Yaw, 0f);
            float x = Vector3.Dot(point - _frame.TransformPoint(_seat.Eyes), seatRot * Vector3.right);
            float side = x >= 0f ? 1f : -1f;
            var limb = side > 0f ? s.rightHand : s.leftHand;
            Transform upper = limb?.bone1?.transform, fore = limb?.bone2?.transform, hand = limb?.bone3?.transform;
            if (upper == null || fore == null || hand == null) { why = Name + " has no " + (side > 0f ? "right" : "left") + " arm bones"; return -1f; }
            EnsureHands();
            if (_reachB != null) EndReach("a new reach");
            float dist = (point - hand.position).magnitude;
            _reachOut = outSeconds > 0f ? Mathf.Clamp(outSeconds, ReachOutMin, ReachOutMax) : Mathf.Clamp(dist / ReachSpeed, ReachOutMin, ReachOutMax);
            _reachKey = string.Equals(b.P.Action, Vehicles.Ignition.ControlName, System.StringComparison.OrdinalIgnoreCase);
            _reachHoldS = (_reachKey ? KeyHold : ReachHold) + DebugHold;
            _reachB = b;
            _reachSide = side;
            _reachT = 0f;
            _reachShoulder = _frame.InverseTransformPoint(upper.position);
            var spineRefs = _ik.BodyIK.references.spine;
            Transform topBone = spineRefs != null && spineRefs.Length > 0 ? spineRefs[spineRefs.Length - 1] : null;
            _reachTop = topBone != null ? _frame.InverseTransformPoint(topBone.position) : _reachShoulder;
            _reachArm = (fore.position - upper.position).magnitude + (hand.position - fore.position).magnitude;
            _reachMeasure = false;
            _reachTipMin = _reachWristMin = float.MaxValue;
            _reachLean = 0f; _reachPeak = 0f;
            Core.Log.Msg("[Reach] " + Name + " reaches for '" + b.P.Name + "' with the " + (side > 0f ? "right" : "left") + " hand, "
                + dist.ToString("0.00", CultureInfo.InvariantCulture) + " m, there in " + _reachOut.ToString("0.00", CultureInfo.InvariantCulture) + " s.");
            return _reachOut;
        }

        /// <summary>Reads the hand bones once, for a seat whose hands do not hold a wheel rim.</summary>
        private void EnsureHands()
        {
            if (_handsRead) return;
            _handsRead = true;
            var s = _ik.BodyIK.solvers;
            _handL = BuildHand(s.leftHand, -1f);
            _handR = BuildHand(s.rightHand, 1f);
        }

        /// <summary>In <see cref="Pose"/>, after the grips are placed: moves the reaching hand's target towards the switch.</summary>
        private void ApplyReach(BipedIKSolvers s, float dt)
        {
            if (_reachB == null) return;
            if (_reachB.T == null) { EndReach("the switch is gone"); return; }
            var limb = _reachSide > 0f ? s.rightHand : s.leftHand;
            Transform grip = _reachSide > 0f ? _gripR : _gripL;
            HandRig h = _reachSide > 0f ? _handR : _handL;
            Transform handBone = limb?.bone3?.transform;
            if (limb == null || grip == null || handBone == null) { EndReach("the arm is gone"); return; }
            Vector3 point = _reachB.T.TransformPoint(_reachB.BoxCenter);
            Vector3 shoulder = _frame.TransformPoint(_reachShoulder);
            Vector3 dir = point - shoulder;
            if (dir.sqrMagnitude < 1e-6f) { EndReach("the switch sits at the shoulder"); return; }
            dir.Normalize();
            float tip = h != null ? h.Fingers.magnitude * TipFactor : 0.17f;

            // The bones still hold last frame's IK pose: how close the hand came.
            if (_reachMeasure)
            {
                _reachWristMin = Mathf.Min(_reachWristMin, (handBone.position - _reachWrist).magnitude);
                Vector3 along = h != null ? handBone.rotation * h.Fingers.normalized : dir;
                _reachTipMin = Mathf.Min(_reachTipMin, (handBone.position + along * tip - point).magnitude);
            }

            _reachT += dt;
            float total = _reachOut + _reachHoldS + ReachBack;
            if (_reachT >= total) { EndReach("back on the grip"); return; }
            float k = _reachT < _reachOut ? Mathf.SmoothStep(0f, 1f, _reachT / _reachOut)
                : _reachT < _reachOut + _reachHoldS ? 1f
                : 1f - Mathf.SmoothStep(0f, 1f, (_reachT - _reachOut - _reachHoldS) / ReachBack);
            _reachPeak = Mathf.Max(_reachPeak, k);
            _reachMeasure = k > 0.99f;

            Vector3 wrist = point - dir * tip;
            // Out of the arm's reach: the upper body leans towards the switch.
            float excess = (wrist - shoulder).magnitude - _reachArm * 0.97f;
            if (excess > 0f)
            {
                float lean = Mathf.Min(excess, LeanMax);
                // From where the spine sat when the reach began: last frame's leaned pose would push it further each frame.
                _ik.BodyIK.SetSpinePosition(_frame.TransformPoint(_reachTop) + dir * lean);
                s.spine.IKPositionWeight = Mathf.Max(s.spine.IKPositionWeight, k * LeanWeight);
                _reachLean = Mathf.Max(_reachLean, lean * k);
            }

            Quaternion rot = grip.rotation;
            if (h != null)
            {
                Quaternion seatRot = _frame.rotation * Quaternion.Euler(0f, _seat.Yaw, 0f);
                // Palm down, fingers towards the switch: the knuckle line runs from the little finger towards the thumb's side.
                Vector3 knuckles = Vector3.ProjectOnPlane(-_reachSide * (seatRot * Vector3.right), dir);
                if (knuckles.sqrMagnitude < 1e-6f) knuckles = Vector3.Cross(dir, seatRot * Vector3.up);
                rot = Quaternion.LookRotation(dir, knuckles.normalized) * Quaternion.Inverse(Quaternion.LookRotation(h.Fingers, h.Knuckles));
                if (_reachKey)
                {
                    // The key turns clockwise once the fingers hold it.
                    float turn = _reachT < _reachOut ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, (_reachT - _reachOut) / Mathf.Max(0.05f, _reachHoldS * 0.6f)));
                    rot = Quaternion.AngleAxis(-KeyTurn * turn * k, dir) * rot;
                }
            }
            grip.position = Vector3.Lerp(grip.position, wrist, k);
            grip.rotation = Quaternion.Slerp(grip.rotation, rot, k);
            _reachWrist = wrist;
            limb.IKPositionWeight = Mathf.Lerp(limb.IKPositionWeight, 1f, k);
            if (h != null) limb.IKRotationWeight = Mathf.Lerp(limb.IKRotationWeight, 1f, k);
            if (k > 0.05f && Player != null) HeadLook.Request(Player, point, HandReach.LookPriority);
        }

        private void EndReach(string why)
        {
            if (_reachB == null) return;
            var c = CultureInfo.InvariantCulture;
            string name = _reachB.P != null ? _reachB.P.Name : "?";
            bool measured = _reachTipMin < float.MaxValue;
            _reachLast = "'" + name + "' hand " + (_reachSide > 0f ? "R" : "L")
                + " tip " + (measured ? _reachTipMin.ToString("0.000", c) : "-")
                + " wrist " + (measured ? _reachWristMin.ToString("0.000", c) : "-")
                + " lean " + _reachLean.ToString("0.00", c) + " peak " + _reachPeak.ToString("0.00", c);
            Core.Log.Msg("[Reach] " + Name + "'s " + (_reachSide > 0f ? "right" : "left") + " hand leaves '" + name + "' (" + why + "): "
                + (measured ? "fingertips " + _reachTipMin.ToString("0.000", c) + " m from it, wrist " + _reachWristMin.ToString("0.000", c) + " m off its target at the closest"
                    : "the hand never got all the way there") + ", lean " + _reachLean.ToString("0.00", c) + " m.");
            _reachB = null;
            _reachMeasure = false;
        }

        /// <summary>For `chassis reach`: the reach running now, and the last one.</summary>
        internal string DescribeReach()
        {
            var c = CultureInfo.InvariantCulture;
            string now = _reachB == null ? "idle"
                : "reaching '" + _reachB.P.Name + "' hand " + (_reachSide > 0f ? "R" : "L") + " t " + _reachT.ToString("0.00", c) + "/" + (_reachOut + _reachHoldS + ReachBack).ToString("0.00", c);
            return Name + " seat " + SeatIndex + " " + now + " | last " + _reachLast;
        }
    }
}
