using System;
using System.Collections.Generic;
using Chassis.Api;

namespace Chassis.Seating
{
    /// <summary>
    /// One seated body: a player's avatar hanging under an anchor on a seat, its hands and feet held by BipedIK.
    /// The same rig runs on every peer for every seated player, local or remote; nothing in it is networked except
    /// the pose bool the local player sends.
    ///
    /// Why the avatar hangs under the seat: AvatarLookController turns the avatar towards its parent's facing every
    /// LateUpdate. Under the player that parent faces the camera; under the anchor it faces the seat. The anchor
    /// cancels the frame's scale so the avatar keeps its own size.
    /// </summary>
    internal sealed class SeatRig
    {
        internal const float GlideSeconds = 0.35f;

        internal readonly Player Player;
        internal readonly Transform Vehicle;
        internal readonly string Code;
        internal readonly int SeatIndex;

        private readonly Transform _frame;
        private readonly GameAvatar _avatar;
        private readonly AvatarIKController _ik;
        private Transform _anchor;
        private Transform _avatarParent0;
        private Vector3 _avatarPos0, _avatarScale0;
        private Quaternion _avatarRot0;

        private Transform _gripL, _gripR, _footL, _footR, _kneeL, _kneeR, _elbowL, _elbowR;
        private Transform _rimL, _rimR;
        private readonly Stroke _strokeL = new Stroke(), _strokeR = new Stroke();

        private readonly Dictionary<string, float> _weights = new Dictionary<string, float>();
        private readonly List<KeyValuePair<IKSolverLimb, (Transform goal, IKSolverLimb.BendModifier mod, float w, Transform target)>> _limbs =
            new List<KeyValuePair<IKSolverLimb, (Transform, IKSolverLimb.BendModifier, float, Transform)>>();
        private readonly List<Collider> _ignored = new List<Collider>();

        private SeatProfile _seat;
        private Vector3 _glideFrom;
        private Quaternion _glideFromRot;
        private float _glideT;
        private bool _ikOn;
        private bool _shownLogged;

        /// <summary>Extra input on top of the driver's for the push stroke: >0 forward, <0 back. Set by Push.</summary>
        internal float StrokeDrive;

        internal bool Alive => Player != null && Vehicle != null && _avatar != null && _anchor != null;

        /// <summary>Seat index used for the one who pushes from behind; that pose comes from the push profile.</summary>
        internal const int PushSeat = -1;

        internal bool IsPusher => SeatIndex == PushSeat;
        internal Transform Frame => _frame;
        internal GameAvatar Avatar => _avatar;
        internal SeatProfile Profile => _seat;
        internal Transform Head => _ik != null && _ik.BodyIK != null ? _ik.BodyIK.references.head : null;

        private SeatRig(Player player, Transform vehicle, Transform frame, string code, int seat, GameAvatar avatar, AvatarIKController ik)
        {
            Player = player; Vehicle = vehicle; _frame = frame; Code = code; SeatIndex = seat; _avatar = avatar; _ik = ik;
        }

        /// <summary>Builds the rig and seats the avatar, or returns null with the reason logged.</summary>
        internal static SeatRig Create(Player player, Transform vehicle, string code, int seatIndex, bool glide)
        {
            var profile = ChassisApi.Get(code);
            var seat = seatIndex == PushSeat ? FromPush(profile?.Push) : profile?.Seat(seatIndex);
            if (seat == null) { Core.Log.Msg("[Seat] '" + code + "' seat " + seatIndex + " has no profile, vanilla keeps the body."); return null; }

            Transform frame = string.IsNullOrEmpty(profile.Frame) ? vehicle : vehicle.Find(profile.Frame);
            if (frame == null) { Core.Log.Error("[Seat] '" + code + "': frame '" + profile.Frame + "' not found under " + vehicle.name + ", body stays hidden."); return null; }

            var avatar = player.Avatar;
            if (avatar == null) { Core.Log.Error("[Seat] " + Who(player) + " has no avatar."); return null; }
            var ik = avatar.GetComponentInChildren<AvatarIKController>(true);
            if (ik == null || ik.BodyIK == null) { Core.Log.Error("[Seat] " + Who(player) + "'s avatar has no BipedIK, body sits without hand targets."); }

            var rig = new SeatRig(player, vehicle, frame, code, seatIndex, avatar, ik);
            rig.Attach(seat, glide);
            return rig;
        }

        /// <summary>The pusher stands behind the vehicle with both hands on the handles; no feet, no lean.</summary>
        private static SeatProfile FromPush(PushProfile push)
        {
            if (push == null) return null;
            return new SeatProfile
            {
                Index = PushSeat,
                Root = push.Stand,
                Yaw = 0f,
                Pose = "Standing",
                Hands = new HandProfile { Mode = "fixed", GripStart = push.Handle, GripEnd = push.Handle, Elbow = push.Elbow, LeanStartWeight = 0f, LeanEndWeight = 0f },
                Feet = null,
            };
        }

        internal static string Who(Player p) => p == null ? "nobody" : (p.IsLocalPlayer ? "local " : "remote ") + p.PlayerName;

        private void Attach(SeatProfile seat, bool glide)
        {
            _seat = seat;
            var root = _avatar.transform;
            _avatarParent0 = root.parent;
            _avatarPos0 = root.localPosition;
            _avatarRot0 = root.localRotation;
            _avatarScale0 = root.localScale;

            _anchor = new GameObject("ChassisSeatAnchor_" + SeatIndex).transform;
            _anchor.SetParent(_frame, false);
            PlaceAnchor();

            _glideFrom = _frame.InverseTransformPoint(root.position);
            _glideFromRot = Quaternion.Inverse(_frame.rotation) * root.rotation;
            _glideT = glide ? 0f : 1f;

            root.SetParent(_anchor, true);
            root.localScale = _avatarScale0;
            ApplyGlide();

            // The capsule rides inside the vehicle and must not push against it.
            _ignored.Clear();
            if (Player.CapCol != null)
            {
                foreach (var c in Vehicle.GetComponentsInChildren<Collider>(true))
                {
                    if (c == null || c.isTrigger) continue;
                    Physics.IgnoreCollision(Player.CapCol, c, true);
                    _ignored.Add(c);
                }
            }

            _rimL = FindRim(seat.Hands?.LeftWheel);
            _rimR = FindRim(seat.Hands?.RightWheel);
            if (_ik != null && _ik.BodyIK != null) SetIK();

            Core.Log.Msg("[Seat] " + Who(Player) + (IsPusher ? " pushing " + Code : " seated in " + Code + " seat " + SeatIndex) + " (" + Vehicle.name + "), glide " + (glide ? GlideSeconds + " s" : "off")
                + ", hands " + (seat.Hands != null ? seat.Hands.Mode : "none") + (seat.Hands?.Mode == "rim" ? (_rimL != null && _rimR != null ? " on both rims" : " but a rim is missing") : "")
                + ", " + _ignored.Count + " vehicle collider(s) ignored by the capsule.");
        }

        private Transform FindRim(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var t in Vehicle.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            Core.Log.Warning("[Seat] '" + Code + "': rim '" + name + "' not found, that hand stays at the stroke start.");
            return null;
        }

        private static Vector3 Inverse(Vector3 v) =>
            new Vector3(Mathf.Approximately(v.x, 0f) ? 1f : 1f / v.x, Mathf.Approximately(v.y, 0f) ? 1f : 1f / v.y, Mathf.Approximately(v.z, 0f) ? 1f : 1f / v.z);

        private void PlaceAnchor()
        {
            _anchor.localPosition = _seat.Root;
            _anchor.localRotation = Quaternion.Euler(0f, _seat.Yaw, 0f);
            _anchor.localScale = Inverse(_frame.lossyScale);
        }

        private void ApplyGlide()
        {
            var root = _avatar.transform;
            if (_glideT >= 1f)
            {
                root.localPosition = Vector3.zero;
                root.localRotation = Quaternion.identity;
                return;
            }
            float k = Mathf.SmoothStep(0f, 1f, _glideT);
            root.position = _frame.TransformPoint(Vector3.Lerp(_glideFrom, _seat.Root, k));
            root.rotation = _frame.rotation * Quaternion.Slerp(_glideFromRot, Quaternion.Euler(0f, _seat.Yaw, 0f), k);
        }

        /// <summary>Profile changed in place: re-read seat values.</summary>
        internal void Refresh()
        {
            var profile = ChassisApi.Get(Code);
            var seat = IsPusher ? FromPush(profile?.Push) : profile?.Seat(SeatIndex);
            if (seat == null) return;
            _seat = seat;
            PlaceAnchor();
        }

        // ---- IK ----------------------------------------------------------------------------------------------------

        private Transform Child(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(_frame, false);
            return t;
        }

        private void SetIK()
        {
            var s = _ik.BodyIK.solvers;
            Save("lh.p", s.leftHand.IKPositionWeight); Save("lh.r", s.leftHand.IKRotationWeight);
            Save("rh.p", s.rightHand.IKPositionWeight); Save("rh.r", s.rightHand.IKRotationWeight);
            Save("lf.p", s.leftFoot.IKPositionWeight); Save("lf.r", s.leftFoot.IKRotationWeight);
            Save("rf.p", s.rightFoot.IKPositionWeight); Save("rf.r", s.rightFoot.IKRotationWeight);
            Save("spine", s.spine.IKPositionWeight); Save("look", s.lookAt.IKPositionWeight); Save("aim", s.aim.IKPositionWeight);
            Save("pelvis.p", s.pelvis.positionWeight); Save("pelvis.r", s.pelvis.rotationWeight);
            foreach (var limb in new IKSolverLimb[] { s.leftHand, s.rightHand, s.leftFoot, s.rightFoot })
                _limbs.Add(new KeyValuePair<IKSolverLimb, (Transform, IKSolverLimb.BendModifier, float, Transform)>(limb, (limb.bendGoal, limb.bendModifier, limb.bendModifierWeight, limb.target)));

            _gripL = Child("ChassisGrip_L"); _gripR = Child("ChassisGrip_R");
            _elbowL = Child("ChassisElbow_L"); _elbowR = Child("ChassisElbow_R");
            _footL = Child("ChassisFoot_L"); _footR = Child("ChassisFoot_R");
            _kneeL = Child("ChassisKnee_L"); _kneeR = Child("ChassisKnee_R");

            s.spine.IKPositionWeight = 0f;
            s.lookAt.IKPositionWeight = 0f;
            s.aim.IKPositionWeight = 0f;
            s.pelvis.positionWeight = 0f; s.pelvis.rotationWeight = 0f;
            if (_seat.Hands != null)
            {
                s.leftHand.target = _gripL; s.rightHand.target = _gripR;
                Bend(s.leftHand, _elbowL); Bend(s.rightHand, _elbowR);
            }
            if (_seat.Feet != null)
            {
                s.leftFoot.target = _footL; s.rightFoot.target = _footR;
                Bend(s.leftFoot, _kneeL); Bend(s.rightFoot, _kneeR);
            }
            _ik.SetIKActive(true);
            _ikOn = true;
            Pose(0f);
        }

        private static void Bend(IKSolverLimb limb, Transform goal)
        {
            limb.bendGoal = goal;
            limb.bendModifier = IKSolverLimb.BendModifier.Goal;
            limb.bendModifierWeight = 1f;
        }

        private void Save(string k, float v) => _weights[k] = v;
        private float Load(string k) => _weights.TryGetValue(k, out float v) ? v : 0f;

        private void Mirror(Transform left, Transform right, Vector3 v)
        {
            left.position = _frame.TransformPoint(new Vector3(-v.x, v.y, v.z));
            right.position = _frame.TransformPoint(v);
        }

        /// <summary>
        /// Every frame, after the player's own update and before the animator and Final IK run: glide, grips, feet.
        /// </summary>
        internal void Pose(float dt)
        {
            if (!Alive) return;
            // Vanilla hides a body in a vehicle on every peer (Player.EnterVehicle, CurrentVehicleChanged and the
            // networked SetVisible); a seated body stays visible here.
            if (_avatar.BodyContainer != null && !_avatar.BodyContainer.gameObject.activeSelf)
            {
                _avatar.SetVisible(true);
                if (!_shownLogged) { _shownLogged = true; Core.Log.Msg("[Seat] " + Who(Player) + "'s body was hidden by vanilla, shown again."); }
            }
            if (_glideT < 1f) { _glideT = Mathf.Min(1f, _glideT + dt / GlideSeconds); }
            ApplyGlide();
            if (!_ikOn) return;

            var s = _ik.BodyIK.solvers;
            var hands = _seat.Hands;
            if (hands != null)
            {
                float lean;
                if (hands.Mode == "rim")
                {
                    float fl = _strokeL.Step(this, _rimL, hands, dt);
                    float fr = _strokeR.Step(this, _rimR, hands, dt);
                    PlaceGrip(_gripL, _rimL, hands, -1f, _strokeL);
                    PlaceGrip(_gripR, _rimR, hands, 1f, _strokeR);
                    lean = (fl + fr) * 0.5f;
                }
                else
                {
                    Mirror(_gripL, _gripR, hands.GripStart);
                    lean = 0f;
                }
                _gripR.rotation = _frame.rotation * Quaternion.Euler(0f, 0f, 90f);
                _gripL.rotation = _frame.rotation * Quaternion.Euler(0f, 0f, -90f);
                Mirror(_elbowL, _elbowR, hands.Elbow);
                // Closer to the stroke end, the body leans further towards the rims.
                float w = Mathf.Lerp(hands.LeanStartWeight, hands.LeanEndWeight, lean);
                if (w > 0f)
                {
                    _ik.BodyIK.SetSpinePosition(_frame.TransformPoint(Vector3.Lerp(hands.LeanStart, hands.LeanEnd, lean)));
                    s.spine.IKPositionWeight = w;
                }
                s.leftHand.IKPositionWeight = 1f; s.rightHand.IKPositionWeight = 1f;
                s.leftHand.IKRotationWeight = hands.RotationWeight; s.rightHand.IKRotationWeight = hands.RotationWeight;
            }
            var feet = _seat.Feet;
            if (feet != null)
            {
                Mirror(_footL, _footR, feet.Position);
                Mirror(_kneeL, _kneeR, feet.Knee);
                _footR.rotation = _frame.rotation * Quaternion.Euler(feet.Rotation);
                _footL.rotation = _frame.rotation * Quaternion.Euler(feet.Rotation.x, -feet.Rotation.y, -feet.Rotation.z);
                s.leftFoot.IKPositionWeight = 1f; s.rightFoot.IKPositionWeight = 1f;
                s.leftFoot.IKRotationWeight = feet.RotationWeight; s.rightFoot.IKRotationWeight = feet.RotationWeight;
            }
            else
            {
                // Without foot targets the legs follow the animator; the prefab's foot weights would pull them to a
                // stale IK position.
                s.leftFoot.IKPositionWeight = 0f; s.rightFoot.IKPositionWeight = 0f;
                s.leftFoot.IKRotationWeight = 0f; s.rightFoot.IKRotationWeight = 0f;
            }
        }

        // ---- push stroke on the rim --------------------------------------------------------------------------------

        /// <summary>
        /// One hand on one rim. The hand holds the rim and turns with it from the stroke start to the stroke end, lets
        /// go there and swings back to the start over <see cref="HandProfile.Recovery"/> seconds. Rolling backwards
        /// runs the same stroke the other way round. Coasting without input, the hand rests at the start.
        /// </summary>
        private sealed class Stroke
        {
            private float _lastSpin = float.NaN;
            internal float Fraction;     // 0 = start, 1 = end
            internal float Recovering;   // >0: seconds left swinging back
            private float _recoverFrom;
            internal float SpinDelta;

            /// <returns>Stroke fraction used for the lean.</returns>
            internal float Step(SeatRig rig, Transform rim, HandProfile h, float dt)
            {
                if (rim == null) { Fraction = 0f; return 0f; }
                // Spin of the rim around the frame's x axis, in degrees; forward rolling is positive.
                Vector3 up = rig._frame.InverseTransformDirection(rim.up);
                float spin = Mathf.Atan2(up.z, up.y) * Mathf.Rad2Deg;
                if (float.IsNaN(_lastSpin)) { _lastSpin = spin; SpinDelta = 0f; return Fraction; }
                SpinDelta = Mathf.DeltaAngle(_lastSpin, spin);
                _lastSpin = spin;

                float start = Angle(h.GripStart), end = Angle(h.GripEnd);
                float range = Mathf.DeltaAngle(start, end); // negative: the stroke runs forward/down the rim
                bool pushing = rig.Driving();

                if (Recovering > 0f)
                {
                    Recovering = Mathf.Max(0f, Recovering - dt);
                    float k = 1f - Recovering / Mathf.Max(0.05f, h.Recovery);
                    Fraction = Mathf.Lerp(_recoverFrom, _recoverTo, Mathf.SmoothStep(0f, 1f, k));
                    return Fraction;
                }
                if (!pushing || Mathf.Abs(range) < 1f)
                {
                    // Coasting: the hand does not hold the rim, it rests over the stroke start.
                    Fraction = Mathf.MoveTowards(Fraction, 0f, dt / Mathf.Max(0.05f, h.Recovery));
                    return Fraction;
                }
                // The grip point rides the rim. Rolling forward turns the rim's top towards the front, which lowers
                // the grip angle (measured from forward towards up) by the same amount; the stroke runs to a lower
                // angle too, so forward rolling moves the fraction from 0 towards 1.
                Fraction += -SpinDelta / range;
                if (Fraction >= 1f) Swing(1f, 0f, h);       // end of a forward stroke: back to the start
                else if (Fraction <= 0f && SpinDelta < 0f) Swing(0f, 1f, h); // rolling back: grab at the end again
                return Fraction;
            }

            private float _recoverTo;

            private void Swing(float from, float to, HandProfile h)
            {
                _recoverFrom = from; _recoverTo = to;
                Fraction = from;
                Recovering = Mathf.Max(0.05f, h.Recovery);
            }
        }

        /// <summary>Angle of a grip offset around the wheel axle, from forward towards up, in degrees.</summary>
        private static float Angle(Vector3 grip) => Mathf.Atan2(grip.y, grip.z) * Mathf.Rad2Deg;

        private void PlaceGrip(Transform grip, Transform rim, HandProfile h, float side, Stroke stroke)
        {
            float a0 = Angle(h.GripStart), a1 = Angle(h.GripEnd);
            float r0 = new Vector2(h.GripStart.y, h.GripStart.z).magnitude, r1 = new Vector2(h.GripEnd.y, h.GripEnd.z).magnitude;
            float f = stroke.Fraction;
            float a = (a0 + Mathf.DeltaAngle(a0, a1) * f) * Mathf.Deg2Rad;
            float r = Mathf.Lerp(r0, r1, f);
            float x = Mathf.Lerp(h.GripStart.x, h.GripEnd.x, f);
            // A hand swinging back lifts off the rim a little.
            float lift = stroke.Recovering > 0f ? Mathf.Sin(Mathf.PI * (1f - stroke.Recovering / Mathf.Max(0.05f, h.Recovery))) * 0.06f : 0f;
            Vector3 centre = rim != null ? _frame.InverseTransformPoint(rim.position) : Vector3.zero;
            Vector3 local = centre + new Vector3(x * side + lift * side, Mathf.Sin(a) * (r + lift), Mathf.Cos(a) * (r + lift));
            grip.position = _frame.TransformPoint(local);
        }

        /// <summary>True while the seated player gives drive input, or a pusher does.</summary>
        internal bool Driving()
        {
            return Mathf.Abs(DriveInput) > 0.05f;
        }

        /// <summary>Throttle this seat contributes, set every frame by the vehicle tracker (driver input).</summary>
        internal float DriveInput;

        // ---- leaving -----------------------------------------------------------------------------------------------

        /// <summary>Puts the avatar back where vanilla keeps it and restores every IK value.</summary>
        internal void Detach(string why)
        {
            try
            {
                if (_ikOn && _ik != null && _ik.BodyIK != null)
                {
                    var s = _ik.BodyIK.solvers;
                    s.leftHand.IKPositionWeight = Load("lh.p"); s.leftHand.IKRotationWeight = Load("lh.r");
                    s.rightHand.IKPositionWeight = Load("rh.p"); s.rightHand.IKRotationWeight = Load("rh.r");
                    s.leftFoot.IKPositionWeight = Load("lf.p"); s.leftFoot.IKRotationWeight = Load("lf.r");
                    s.rightFoot.IKPositionWeight = Load("rf.p"); s.rightFoot.IKRotationWeight = Load("rf.r");
                    s.spine.IKPositionWeight = Load("spine"); s.lookAt.IKPositionWeight = Load("look"); s.aim.IKPositionWeight = Load("aim");
                    s.pelvis.positionWeight = Load("pelvis.p"); s.pelvis.rotationWeight = Load("pelvis.r");
                    foreach (var kv in _limbs)
                    {
                        kv.Key.bendGoal = kv.Value.goal; kv.Key.bendModifier = kv.Value.mod; kv.Key.bendModifierWeight = kv.Value.w; kv.Key.target = kv.Value.target;
                    }
                    _ik.SetIKActive(false);
                }
                if (_avatar != null)
                {
                    var root = _avatar.transform;
                    // Only a body still hanging under the anchor goes back; one vanilla already moved stays where it is.
                    if (root.parent == _anchor)
                    {
                        if (_avatarParent0 != null) root.SetParent(_avatarParent0, false);
                        else Core.Log.Warning("[Seat] " + Who(Player) + "'s avatar had no parent before sitting; it stays under the vehicle until vanilla moves it.");
                        root.localPosition = _avatarPos0;
                        root.localRotation = _avatarRot0;
                        root.localScale = _avatarScale0;
                    }
                    else Core.Log.Msg("[Seat] " + Who(Player) + "'s avatar was already moved to " + (root.parent != null ? root.parent.name : "the scene root") + ", left there.");
                }
                if (Player != null && Player.CapCol != null)
                    foreach (var c in _ignored) if (c != null) Physics.IgnoreCollision(Player.CapCol, c, false);
            }
            catch (Exception e) { Core.Log.Error("[Seat] detaching " + Who(Player) + " failed: " + e); }
            foreach (var t in new[] { _gripL, _gripR, _footL, _footR, _kneeL, _kneeR, _elbowL, _elbowR, _anchor })
                if (t != null) UnityEngine.Object.Destroy(t.gameObject);
            _anchor = null;
            Core.Log.Msg("[Seat] " + Who(Player) + " left " + Code + " seat " + SeatIndex + " (" + why + ").");
        }

        internal string Describe()
        {
            return Who(Player) + " in " + Code + "#" + SeatIndex + " glide " + _glideT.ToString("F2") + " ik " + _ikOn
                + " strokeL " + _strokeL.Fraction.ToString("F2") + (_strokeL.Recovering > 0f ? "r" : "") + " strokeR " + _strokeR.Fraction.ToString("F2") + (_strokeR.Recovering > 0f ? "r" : "")
                + " spin " + _strokeR.SpinDelta.ToString("F1") + " drive " + DriveInput.ToString("F2")
                + " root " + (_avatar != null ? ProfileJson.F(_frame.InverseTransformPoint(_avatar.transform.position)) : "-");
        }
    }
}
