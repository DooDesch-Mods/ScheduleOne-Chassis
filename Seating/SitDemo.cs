#if DEBUG
using System;
using System.Collections.Generic;
using HarmonyLib;

namespace Chassis.Seating
{
    /// <summary>
    /// Look prototype behind <c>chassis demo</c>: the local player sits visibly in one of the placed wheelchairs, with
    /// both hands on the push rims, seen from outside or from the eyes. It answers "does the pose look right" before
    /// the seating system is built; nothing here drives, saves or syncs.
    ///
    /// The pose follows the vanilla pieces the real system will use:
    /// * the animator bool <c>Sitting</c>, sent through <c>Player.SendAnimationBool</c> like every player pose;
    /// * the avatar root placed by hand, because <c>AvatarAnimation.SetSeat</c> needs an NPCMovement the player lacks;
    /// * BipedIK hand targets, the way <c>AvatarAnimation.SkateboardMounted</c> sets them for the skateboard.
    ///
    /// <c>Player.Update</c> rewrites the avatar's local position every frame while the player is not in a vehicle,
    /// so the root is written in a postfix to it: after that write, before the animator and the IK run.
    /// </summary>
    internal static class SitDemo
    {
        // Avatar root in the chair's local space, and its yaw against the chair. The vanilla sit clip puts the hips
        // 0.825 m above the root (AvatarAnimation.SittingOffset), and the seat cube sits about 0.49 m up.
        internal static Vector3 SeatOffset = new Vector3(0f, -0.10f, -0.12f);
        internal static float SeatYaw;

        // Grip on the push rim, relative to the big wheel's centre in the chair's space, for the right hand
        // (x is mirrored for the left). At about 1 o'clock on the rim, where a push stroke starts, just outside the tyre.
        internal static Vector3 GripOffset = new Vector3(0.04f, 0.26f, 0.15f);
        internal static float GripRotationWeight;

        // Right foot on its footrest, and the bend goals that steer knees and elbows, all in the chair's space
        // (x mirrored for the left side). Knees bend forward and up; elbows out and back like on a push stroke.
        internal static Vector3 FootOffset = new Vector3(0.165f, 0.17f, 0.46f);
        internal static float FootRotationWeight = 1f;
        // Foot bone rotation against the chair, for the right foot (yaw and roll mirrored for the left). The bone's
        // long axis is its local Y, so a pitch near 90 turns the toes forward.
        internal static Vector3 FootRotation = new Vector3(-92f, 180f, 0f);
        internal static Vector3 KneeGoal = new Vector3(0.14f, 0.75f, 0.9f);
        internal static Vector3 ElbowGoal = new Vector3(0.55f, 0.85f, -0.4f);
        internal static float ChairScale = 1f;

        // Where the spine chain reaches for, in the chair's space, and how strongly: a slight lean towards the rims
        // bends the elbows, which stay straight when the shoulders sit right above the grips.
        internal static Vector3 SpineGoal = new Vector3(0f, 1.15f, 0.18f);
        internal static float SpineWeight = 0.35f;

        internal static bool Active => _chair != null;

        private static Transform _leftFoot, _rightFoot, _leftKnee, _rightKnee, _leftElbow, _rightElbow;
        private static Vector3 _chairScale0;
        // The avatar hangs under this anchor on the chair, as vanilla hangs it under a car in Player.EnterVehicle.
        // AvatarLookController turns the avatar towards its parent's facing every LateUpdate; under the player that
        // is the camera yaw, under the anchor it is the seat. The anchor cancels the chair's scale.
        private static Transform _seatAnchor;
        private static Transform _avatarParent0;
        private static Vector3 _avatarScale0;
        private static readonly List<Collider> _ignoredColliders = new List<Collider>();
        private static readonly Dictionary<string, KeyValuePair<Transform, KeyValuePair<IKSolverLimb.BendModifier, float>>> _savedBend =
            new Dictionary<string, KeyValuePair<Transform, KeyValuePair<IKSolverLimb.BendModifier, float>>>();

        private static Transform _chair;
        private static Rigidbody _chairBody;
        private static bool _chairWasKinematic;
        private static Transform _leftWheel, _rightWheel;
        private static Transform _leftGrip, _rightGrip;
        private static GameAvatar _avatar;
        private static AvatarIKController _ik;
        private static readonly Dictionary<string, float> _savedWeights = new Dictionary<string, float>();
        private static Transform _savedLeftTarget, _savedRightTarget, _savedLeftFootTarget, _savedRightFootTarget;
        private static Transform _head;
        private static Vector3 _headScale;
        private static string _cam = "off";

        internal static List<Transform> FindChairs()
        {
            var found = new List<Transform>();
            var bodies = UnityEngine.Object.FindObjectsOfType<Rigidbody>();
            for (int i = 0; i < bodies.Length; i++)
            {
                var b = bodies[i];
                if (b != null && b.name.IndexOf("wheelchair", StringComparison.OrdinalIgnoreCase) >= 0) found.Add(b.transform);
            }
            return found;
        }

        /// <summary>
        /// Placed chairs can lie knocked over (the Skate Park one does). Stand the chair on its wheels with its
        /// current heading and put the lowest point of its meshes on the ground below it.
        /// </summary>
        private static void Upright(Transform chair)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(chair.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(chair.up, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            chair.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            var renderers = chair.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { Core.Log.Warning("[Demo] chair has no renderers, kept its height"); return; }
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            float ground = float.NegativeInfinity;
            var hits = Physics.RaycastAll(new Vector3(b.center.x, b.max.y + 0.5f, b.center.z), Vector3.down, 5f);
            foreach (var h in hits)
            {
                if (h.collider == null || h.collider.isTrigger || h.collider.transform.IsChildOf(chair)) continue;
                if (h.point.y > ground) ground = h.point.y;
            }
            if (float.IsNegativeInfinity(ground)) { Core.Log.Warning("[Demo] no ground under the chair, kept its height"); return; }
            chair.position += Vector3.up * (ground - b.min.y);
            Core.Log.Msg("[Demo] chair upright at " + chair.position.ToString("F2") + ", ground " + ground.ToString("F2"));
        }

        /// <param name="index">Chair from the 'chassis chairs' list, or -1 for the nearest.</param>
        internal static string Start(bool teleport, int index = -1)
        {
            if (Active) return "demo: already sitting, 'chassis demo off' first";
            var local = Player.Local;
            if (local == null) return "demo: no local player";

            var chairs = FindChairs();
            Transform best = null;
            float bestDist = float.MaxValue;
            if (index >= 0)
            {
                if (index >= chairs.Count) return "demo: no chair " + index + ", there are " + chairs.Count;
                best = chairs[index];
                bestDist = Vector3.Distance(best.position, local.transform.position);
            }
            else
            {
                foreach (var c in chairs)
                {
                    float d = Vector3.Distance(c.position, local.transform.position);
                    if (d < bestDist) { best = c; bestDist = d; }
                }
            }
            if (best == null) return "demo: no wheelchair in the scene";
            if (!teleport && bestDist > 6f) return "demo: nearest wheelchair is " + bestDist.ToString("F0") + " m away at " + best.position.ToString("F1") + ", use 'chassis demo tp'";

            _avatar = local.Avatar;
            _ik = _avatar != null ? _avatar.GetComponentInChildren<AvatarIKController>(true) : null;
            if (_avatar == null || _ik == null || _ik.BodyIK == null) return "demo: avatar or its BipedIK not found";

            if (!FindWheels(best)) return "demo: the wheelchair at " + best.position.ToString("F1") + " has no 'Wheel' children";
            _chair = best;

            _chairBody = best.GetComponent<Rigidbody>();
            _chairWasKinematic = _chairBody.isKinematic;
            _chairBody.isKinematic = true;
            _chairScale0 = best.localScale;
            best.localScale = _chairScale0 * ChairScale;
            Upright(best);

            // The capsule stands where the chair is, so the camera and the network position stay with the body.
            var movement = PlayerSingleton<PlayerMovement>.Instance;
            movement.CanMove = false;
            movement.Controller.enabled = false;
            local.transform.position = best.position;

            AttachAvatar(local);

            local.SetThirdPersonMeshesVisibility(true);
            local.SendAnimationBool("Sitting", true);
            SetHandTargets();

            _head = _ik.BodyIK.references.head;
            if (_head != null) _headScale = _head.localScale;

            Core.Log.Msg("[Demo] sitting in " + Path(best) + " at " + best.position.ToString("F2") + ", wheels " + _leftWheel.name + " / " + _rightWheel.name);
            return "demo: sitting in " + best.name + " (" + bestDist.ToString("F1") + " m away)";
        }

        internal static string Stop()
        {
            if (!Active) return "demo: not sitting";
            var local = Player.Local;
            try
            {
                SetCamera("off");
                RestoreHandTargets();
                DetachAvatar(local);
                if (local != null)
                {
                    local.SendAnimationBool("Sitting", false);
                    local.SetThirdPersonMeshesVisibility(false);
                    var movement = PlayerSingleton<PlayerMovement>.Instance;
                    movement.Controller.enabled = true;
                    movement.CanMove = true;
                    local.transform.position = _chair.position + _chair.right * 0.9f + Vector3.up * 0.2f;
                }
                if (_chairBody != null) _chairBody.isKinematic = _chairWasKinematic;
                _chair.localScale = _chairScale0;
                foreach (var t in new[] { _leftGrip, _rightGrip, _leftFoot, _rightFoot, _leftKnee, _rightKnee, _leftElbow, _rightElbow })
                    if (t != null) UnityEngine.Object.Destroy(t.gameObject);
            }
            catch (Exception e) { Core.Log.Error("[Demo] stop failed: " + e); }
            Clear();
            return "demo: stood up";
        }

        internal static void Reset()
        {
            if (Active) Core.Log.Warning("[Demo] scene changed while sitting; the demo state is dropped");
            Clear();
        }

        private static void Clear()
        {
            _chair = null; _chairBody = null; _leftWheel = _rightWheel = null; _leftGrip = _rightGrip = null;
            _leftFoot = _rightFoot = _leftKnee = _rightKnee = _leftElbow = _rightElbow = null;
            if (_marks != null) { foreach (var m in _marks) if (m != null) UnityEngine.Object.Destroy(m); _marks = null; }
            _avatar = null; _ik = null; _head = null; _cam = "off";
            _seatAnchor = null; _avatarParent0 = null; _ignoredColliders.Clear();
            _savedWeights.Clear();
            _savedBend.Clear();
        }

        private static Vector3 Inverse(Vector3 v) =>
            new Vector3(Mathf.Approximately(v.x, 0f) ? 1f : 1f / v.x, Mathf.Approximately(v.y, 0f) ? 1f : 1f / v.y, Mathf.Approximately(v.z, 0f) ? 1f : 1f / v.z);

        private static void AttachAvatar(Player local)
        {
            var root = _avatar.transform;
            _avatarParent0 = root.parent;
            _avatarScale0 = root.localScale;
            _seatAnchor = new GameObject("ChassisSeatAnchor").transform;
            _seatAnchor.SetParent(_chair, false);
            _seatAnchor.localScale = Inverse(_chair.lossyScale);
            root.SetParent(_seatAnchor, true);
            root.localScale = _avatarScale0;
            root.localRotation = Quaternion.identity;

            // Like Player.EnterVehicle: the player's capsule sits inside the chair and must not collide with it.
            _ignoredColliders.Clear();
            if (local.CapCol == null) { Core.Log.Warning("[Demo] player has no capsule collider, chair collisions stay on"); return; }
            foreach (var c in _chair.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || c.isTrigger) continue;
                Physics.IgnoreCollision(local.CapCol, c, true);
                _ignoredColliders.Add(c);
            }
            Core.Log.Msg("[Demo] avatar attached to the chair, chair scale " + _chair.lossyScale.ToString("F2") + ", " + _ignoredColliders.Count + " chair collider(s) ignored by the capsule");
        }

        private static void DetachAvatar(Player local)
        {
            if (_avatar != null)
            {
                var root = _avatar.transform;
                if (_avatarParent0 != null) root.SetParent(_avatarParent0, false);
                else Core.Log.Warning("[Demo] avatar had no parent before sitting, left it under the chair");
                root.localScale = _avatarScale0;
                root.localPosition = Vector3.zero;
                root.localRotation = Quaternion.identity;
            }
            if (local != null && local.CapCol != null)
                foreach (var c in _ignoredColliders) if (c != null) Physics.IgnoreCollision(local.CapCol, c, false);
            _ignoredColliders.Clear();
            if (_seatAnchor != null) UnityEngine.Object.Destroy(_seatAnchor.gameObject);
            _seatAnchor = null; _avatarParent0 = null;
        }

        private static bool FindWheels(Transform chair)
        {
            _leftWheel = _rightWheel = null;
            var all = chair.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t.name != "Wheel" && t.name != "Wheel (1)") continue;
                if (chair.InverseTransformPoint(t.position).x < 0f) _leftWheel = t; else _rightWheel = t;
            }
            return _leftWheel != null && _rightWheel != null;
        }

        /// <summary>Called from the Player.Update postfix: puts the avatar root on the seat.</summary>
        internal static void Pose(Player player)
        {
            if (!Active || player != Player.Local || _avatar == null) return;
            var root = _avatar.transform;
            if (_probeLeft > 0) Acc(ref _probeStart, _chair.InverseTransformPoint(root.position));
            if (_seatAnchor != null)
            {
                // Player.Update puts the avatar back to its standing offset every frame; the seat goes on top.
                _seatAnchor.localScale = Inverse(_chair.lossyScale);
                _seatAnchor.localRotation = Quaternion.Euler(0f, SeatYaw, 0f);
                root.localRotation = Quaternion.identity;
            }
            else root.rotation = _chair.rotation * Quaternion.Euler(0f, SeatYaw, 0f);
            root.position = _chair.TransformPoint(SeatOffset);
            PlaceGrips();
            PlaceLimbGoals();
        }

        private static Transform ChairChild(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(_chair, false);
            return t;
        }

        /// <summary>Feet on the footrests, knee and elbow bend goals; offsets are mirrored for the left side.</summary>
        private static void PlaceLimbGoals()
        {
            if (_leftFoot == null) return;
            // Here the bones still hold last frame's solved pose; Final IK resets them later in the frame, so a
            // console readout would show the unsolved pose.
            var s = _ik.BodyIK.solvers;
            _solvedFeet = Foot(s.rightFoot.bone3.transform, "R") + " | " + Foot(s.leftFoot.bone3.transform, "L");
            PlaceMirrored(_leftFoot, _rightFoot, FootOffset);
            PlaceMirrored(_leftKnee, _rightKnee, KneeGoal);
            PlaceMirrored(_leftElbow, _rightElbow, ElbowGoal);
            // Sole flat on the footrest, toes forward.
            _rightFoot.rotation = _chair.rotation * Quaternion.Euler(FootRotation);
            _leftFoot.rotation = _chair.rotation * Quaternion.Euler(FootRotation.x, -FootRotation.y, -FootRotation.z);
            _ik.BodyIK.SetSpinePosition(_chair.TransformPoint(SpineGoal));
            _ik.BodyIK.solvers.spine.IKPositionWeight = SpineWeight;
        }

        private static string _solvedFeet = "";

        // Shake probe: where the avatar root, the right ankle and the player end up at LateUpdate, and where the root
        // starts the next Update, all in the chair's space, over a number of frames. A wide range means something
        // moves the body after Pose.
        private struct Range { public Vector3 Min, Max; public bool Any; }
        private static int _probeLeft, _probeTotal;
        private static Range _probeStart, _probeRoot, _probeAnkle, _probePlayer, _probeChair, _probeEndRoot, _probeEndHips, _probeEndAnkle, _probeCap, _probeEndRootRot, _probeEndHipsRot;

        private static System.Collections.IEnumerator EndOfFrameProbe()
        {
            while (_probeLeft > 0)
            {
                yield return new WaitForEndOfFrame();
                if (!Active || _avatar == null) yield break;
                Acc(ref _probeEndRoot, _chair.InverseTransformPoint(_avatar.transform.position));
                var hips = _ik.BodyIK.references.pelvis;
                if (hips != null) Acc(ref _probeEndHips, _chair.InverseTransformPoint(hips.position));
                Acc(ref _probeEndRootRot, (Quaternion.Inverse(_chair.rotation) * _avatar.transform.rotation).eulerAngles);
                if (hips != null) Acc(ref _probeEndHipsRot, (Quaternion.Inverse(_chair.rotation) * hips.rotation).eulerAngles);
                Acc(ref _probeEndAnkle, _chair.InverseTransformPoint(_ik.BodyIK.solvers.rightFoot.bone3.transform.position));
                var local = Player.Local;
                if (local != null && local.CapCol != null) Acc(ref _probeCap, _chair.InverseTransformPoint(local.CapCol.transform.position));
            }
        }

        private static void Acc(ref Range r, Vector3 v)
        {
            if (!r.Any) { r.Min = r.Max = v; r.Any = true; return; }
            r.Min = Vector3.Min(r.Min, v); r.Max = Vector3.Max(r.Max, v);
        }

        private static string Show(string name, Range r) =>
            name + " " + (r.Any ? r.Min.ToString("F3") + ".." + r.Max.ToString("F3") + " spread " + (r.Max - r.Min).magnitude.ToString("F3") : "no samples");

        internal static string StartProbe(int frames)
        {
            if (!Active) return "probe: sit first ('chassis demo')";
            _probeStart = _probeRoot = _probeAnkle = _probePlayer = _probeChair = _probeEndRoot = _probeEndHips = _probeEndAnkle = _probeCap = _probeEndRootRot = _probeEndHipsRot = default;
            _probeLeft = _probeTotal = Mathf.Max(1, frames);
            MelonLoader.MelonCoroutines.Start(EndOfFrameProbe());
            return "probe: sampling " + _probeTotal + " frames";
        }

        /// <summary>Called from Core.OnLateUpdate while a probe runs.</summary>
        internal static void LateProbe()
        {
            if (_probeLeft <= 0) return;
            if (!Active || _avatar == null) { _probeLeft = 0; Core.Log.Warning("[Demo] probe stopped: not sitting"); return; }
            Acc(ref _probeRoot, _chair.InverseTransformPoint(_avatar.transform.position));
            Acc(ref _probeAnkle, _chair.InverseTransformPoint(_ik.BodyIK.solvers.rightFoot.bone3.transform.position));
            var local = Player.Local;
            if (local != null) Acc(ref _probePlayer, _chair.InverseTransformPoint(local.transform.position));
            Acc(ref _probeChair, _chair.position);
            if (--_probeLeft > 0) return;
            Debugging.DebugConsolePatch.Say("probe " + _probeTotal + " frames, seat set " + SeatOffset.ToString("F3"));
            Debugging.DebugConsolePatch.Say(Show("rootAtUpdate", _probeStart));
            Debugging.DebugConsolePatch.Say(Show("rootLate", _probeRoot));
            Debugging.DebugConsolePatch.Say(Show("ankleLate", _probeAnkle));
            Debugging.DebugConsolePatch.Say(Show("player", _probePlayer));
            Debugging.DebugConsolePatch.Say(Show("chairWorld", _probeChair));
            Debugging.DebugConsolePatch.Say(Show("rootEnd", _probeEndRoot));
            Debugging.DebugConsolePatch.Say(Show("hipsEnd", _probeEndHips));
            Debugging.DebugConsolePatch.Say(Show("ankleEnd", _probeEndAnkle) + " target " + FootOffset.ToString("F3"));
            Debugging.DebugConsolePatch.Say(Show("capsuleEnd", _probeCap));
            Debugging.DebugConsolePatch.Say(Show("rootRotEnd", _probeEndRootRot));
            Debugging.DebugConsolePatch.Say(Show("hipsRotEnd", _probeEndHipsRot));
        }

        /// <summary>Every behaviour on the avatar and its children, counted by type, with its enabled state.</summary>
        internal static IEnumerable<string> Components()
        {
            if (!Active || _avatar == null) { yield return "comps: sit first ('chassis demo')"; yield break; }
            var counts = new SortedDictionary<string, int>();
            foreach (var b in _avatar.GetComponentsInChildren<Behaviour>(true))
            {
                string key = Compat.Interop.TypeName(b, true) + (b.enabled ? "" : " (off)") + " on " + b.gameObject.name;
                counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
            }
            foreach (var kv in counts) yield return kv.Key + (kv.Value > 1 ? " x" + kv.Value : "");
            var parent = _avatar.transform.parent;
            yield return "avatar parent: " + (parent != null ? Path(parent) : "none");
        }

        private static void PlaceMirrored(Transform left, Transform right, Vector3 offset)
        {
            left.position = _chair.TransformPoint(new Vector3(-offset.x, offset.y, offset.z));
            right.position = _chair.TransformPoint(offset);
        }

        private static void SaveBend(string key, IKSolverLimb limb)
        {
            _savedBend[key] = new KeyValuePair<Transform, KeyValuePair<IKSolverLimb.BendModifier, float>>(
                limb.bendGoal, new KeyValuePair<IKSolverLimb.BendModifier, float>(limb.bendModifier, limb.bendModifierWeight));
        }

        private static void RestoreBend(string key, IKSolverLimb limb)
        {
            if (!_savedBend.TryGetValue(key, out var v)) return;
            limb.bendGoal = v.Key; limb.bendModifier = v.Value.Key; limb.bendModifierWeight = v.Value.Value;
        }

        private static void UseBendGoal(IKSolverLimb limb, Transform goal)
        {
            limb.bendGoal = goal;
            limb.bendModifier = IKSolverLimb.BendModifier.Goal;
            limb.bendModifierWeight = 1f;
        }

        private static void SetHandTargets()
        {
            var s = _ik.BodyIK.solvers;
            _savedLeftTarget = s.leftHand.target;
            _savedRightTarget = s.rightHand.target;
            _savedLeftFootTarget = s.leftFoot.target; _savedRightFootTarget = s.rightFoot.target;
            Save("leftHand.pos", s.leftHand.IKPositionWeight); Save("leftHand.rot", s.leftHand.IKRotationWeight);
            Save("rightHand.pos", s.rightHand.IKPositionWeight); Save("rightHand.rot", s.rightHand.IKRotationWeight);
            Save("leftFoot.pos", s.leftFoot.IKPositionWeight); Save("leftFoot.rot", s.leftFoot.IKRotationWeight);
            Save("rightFoot.pos", s.rightFoot.IKPositionWeight); Save("rightFoot.rot", s.rightFoot.IKRotationWeight);
            Save("spine", s.spine.IKPositionWeight);
            Save("lookAt", s.lookAt.IKPositionWeight);
            Save("aim", s.aim.IKPositionWeight);
            Save("pelvis.pos", s.pelvis.positionWeight); Save("pelvis.rot", s.pelvis.rotationWeight);

            // Grips hang from the wheels, so they turn with the rims once the chair rolls.
            _leftGrip = new GameObject("ChassisGrip_L").transform;
            _rightGrip = new GameObject("ChassisGrip_R").transform;
            _leftGrip.SetParent(_leftWheel, false);
            _rightGrip.SetParent(_rightWheel, false);
            PlaceGrips();

            _leftFoot = ChairChild("ChassisFoot_L"); _rightFoot = ChairChild("ChassisFoot_R");
            _leftKnee = ChairChild("ChassisKnee_L"); _rightKnee = ChairChild("ChassisKnee_R");
            _leftElbow = ChairChild("ChassisElbow_L"); _rightElbow = ChairChild("ChassisElbow_R");
            PlaceLimbGoals();
            SaveBend("leftHand", s.leftHand); SaveBend("rightHand", s.rightHand);
            SaveBend("leftFoot", s.leftFoot); SaveBend("rightFoot", s.rightFoot);

            // Hands on the rims and feet on the footrests; the sit clip keeps the spine and the pelvis.
            s.leftFoot.target = _leftFoot; s.rightFoot.target = _rightFoot;
            UseBendGoal(s.leftFoot, _leftKnee); UseBendGoal(s.rightFoot, _rightKnee);
            UseBendGoal(s.leftHand, _leftElbow); UseBendGoal(s.rightHand, _rightElbow);
            s.spine.IKPositionWeight = 0f;
            s.lookAt.IKPositionWeight = 0f;
            s.aim.IKPositionWeight = 0f;
            s.pelvis.positionWeight = 0f; s.pelvis.rotationWeight = 0f;
            s.leftHand.target = _leftGrip; s.rightHand.target = _rightGrip;
            ApplyGripWeights();
            _ik.SetIKActive(true);
        }

        internal static void ApplyGripWeights()
        {
            if (_ik == null) return;
            var s = _ik.BodyIK.solvers;
            s.leftHand.IKPositionWeight = 1f; s.rightHand.IKPositionWeight = 1f;
            s.leftHand.IKRotationWeight = GripRotationWeight; s.rightHand.IKRotationWeight = GripRotationWeight;
            s.leftFoot.IKPositionWeight = 1f; s.rightFoot.IKPositionWeight = 1f;
            s.leftFoot.IKRotationWeight = FootRotationWeight; s.rightFoot.IKRotationWeight = FootRotationWeight;
        }

        /// <summary>Scales the chair against its original size and stands it on the ground again.</summary>
        internal static string SetScale(float scale)
        {
            ChairScale = Mathf.Clamp(scale, 0.5f, 2f);
            if (!Active) return "scale " + ChairScale.ToString("F2") + " (next sit)";
            _chair.localScale = _chairScale0 * ChairScale;
            Upright(_chair);
            return "scale " + ChairScale.ToString("F2");
        }

        /// <summary>Every mesh of the chair with its centre and size in the chair's space, to find footrests and seat.</summary>
        internal static List<string> Parts()
        {
            var lines = new List<string>();
            if (!Active) { lines.Add("parts: sit first"); return lines; }
            foreach (var r in _chair.GetComponentsInChildren<Renderer>())
            {
                Bounds b = r.bounds;
                Vector3 c = _chair.InverseTransformPoint(b.center);
                Vector3 size = _chair.InverseTransformVector(b.size);
                lines.Add(Path(r.transform).Substring(Path(_chair).Length) + " centre " + c.ToString("F2") + " size " + size.ToString("F2"));
            }
            lines.Add(Footrests());
            return lines;
        }

        /// <summary>
        /// Footrest plates from the main mesh: bounds of the vertices below 0.25 m and in front of z 0.3, per side,
        /// in the chair's space. Needs a readable mesh; says so when it is not.
        /// </summary>
        private static string Footrests()
        {
            MeshFilter main = null;
            foreach (var mf in _chair.GetComponentsInChildren<MeshFilter>())
                if (mf.gameObject.name == "Main_LOD0") { main = mf; break; }
            if (main == null || main.sharedMesh == null) return "footrests: no Main_LOD0 mesh";
            if (!main.sharedMesh.isReadable) return "footrests: Main_LOD0 is not readable";
            var verts = main.sharedMesh.vertices;
            var side = new[] { new Bounds(), new Bounds() };
            var any = new[] { false, false };
            foreach (var v in verts)
            {
                Vector3 p = _chair.InverseTransformPoint(main.transform.TransformPoint(v));
                if (p.y > 0.25f || p.z < 0.3f) continue;
                int i = p.x < 0f ? 0 : 1;
                if (!any[i]) { side[i] = new Bounds(p, Vector3.zero); any[i] = true; }
                else side[i].Encapsulate(p);
            }
            string Show(int i) => any[i] ? "min " + side[i].min.ToString("F3") + " max " + side[i].max.ToString("F3") : "none";
            return "footrests (" + verts.Length + " verts): L " + Show(0) + " | R " + Show(1);
        }

        private static void PlaceGrips()
        {
            if (_leftGrip == null || _rightGrip == null) return;
            PlaceGrip(_leftGrip, _leftWheel, -1f);
            PlaceGrip(_rightGrip, _rightWheel, 1f);
        }

        private static void PlaceGrip(Transform grip, Transform wheel, float side)
        {
            // The offset is in the chair's space, not the wheel's: the wheel transforms carry the FBX import rotation.
            Vector3 centre = _chair.InverseTransformPoint(wheel.position);
            Vector3 local = centre + new Vector3(GripOffset.x * side, GripOffset.y, GripOffset.z);
            grip.position = _chair.TransformPoint(local);
            // Palm towards the chair, fingers forward over the rim.
            grip.rotation = _chair.rotation * Quaternion.Euler(0f, 0f, side * 90f);
        }

        private static void RestoreHandTargets()
        {
            if (_ik == null) return;
            var s = _ik.BodyIK.solvers;
            s.leftHand.target = _savedLeftTarget; s.rightHand.target = _savedRightTarget;
            s.leftHand.IKPositionWeight = Load("leftHand.pos"); s.leftHand.IKRotationWeight = Load("leftHand.rot");
            s.rightHand.IKPositionWeight = Load("rightHand.pos"); s.rightHand.IKRotationWeight = Load("rightHand.rot");
            s.leftFoot.IKPositionWeight = Load("leftFoot.pos"); s.leftFoot.IKRotationWeight = Load("leftFoot.rot");
            s.rightFoot.IKPositionWeight = Load("rightFoot.pos"); s.rightFoot.IKRotationWeight = Load("rightFoot.rot");
            s.spine.IKPositionWeight = Load("spine");
            s.lookAt.IKPositionWeight = Load("lookAt");
            s.aim.IKPositionWeight = Load("aim");
            s.pelvis.positionWeight = Load("pelvis.pos"); s.pelvis.rotationWeight = Load("pelvis.rot");
            s.leftFoot.target = _savedLeftFootTarget; s.rightFoot.target = _savedRightFootTarget;
            RestoreBend("leftHand", s.leftHand); RestoreBend("rightHand", s.rightHand);
            RestoreBend("leftFoot", s.leftFoot); RestoreBend("rightFoot", s.rightFoot);
            _ik.SetIKActive(false);
        }

        private static void Save(string key, float v) => _savedWeights[key] = v;
        private static float Load(string key) => _savedWeights.TryGetValue(key, out float v) ? v : 0f;

        private static readonly List<KeyValuePair<Renderer, UnityEngine.Rendering.ShadowCastingMode>> _hiddenHead = new List<KeyValuePair<Renderer, UnityEngine.Rendering.ShadowCastingMode>>();

        /// <summary>
        /// Glasses, eyes and other face parts are not children of the head bone, so scaling the head leaves them in
        /// front of the camera. Every small renderer around the eyes casts only its shadow while the view is first person.
        /// </summary>
        private static int HideHeadParts(Vector3 eyes)
        {
            foreach (var r in _avatar.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
                Bounds b = r.bounds;
                if (Vector3.Distance(b.center, eyes) > 0.25f || b.extents.magnitude > 0.35f) continue;
                _hiddenHead.Add(new KeyValuePair<Renderer, UnityEngine.Rendering.ShadowCastingMode>(r, r.shadowCastingMode));
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                Core.Log.Msg("[Demo] fp hides " + r.gameObject.name);
            }
            return _hiddenHead.Count;
        }

        private static void ShowHeadParts()
        {
            foreach (var kv in _hiddenHead)
                if (kv.Key != null) kv.Key.shadowCastingMode = kv.Value;
            _hiddenHead.Clear();
        }

        /// <summary>Camera for the look check: "out [dist] [yaw] [height]", "fp" or "off".</summary>
        internal static string SetCamera(string mode, float dist = 2.6f, float yaw = 35f, float height = 1.2f)
        {
            var cam = PlayerSingleton<PlayerCamera>.Instance;
            if (cam == null) return "cam: no PlayerCamera";
            if (_head != null) _head.localScale = _headScale;
            ShowHeadParts();

            if (mode == "off")
            {
                if (_cam != "off") cam.StopTransformOverride(0f, true, true);
                _cam = "off";
                return "cam: back to the player camera";
            }
            if (!Active) return "cam: sit first ('chassis demo')";

            if (mode == "fp")
            {
                var eyes = _avatar.Eyes != null ? _avatar.Eyes.transform : _head;
                Vector3 pos = eyes.position + _chair.forward * yaw;
                // For "fp" the first number is the pitch down in degrees, the second the step forward from the eyes.
                float pitch = dist;
                Quaternion rot = Quaternion.LookRotation(_chair.forward, Vector3.up) * Quaternion.Euler(pitch, 0f, 0f);
                cam.OverrideTransform(pos, rot, 0f, false);
                // Only this peer's copy of the avatar loses its head; the camera would sit inside it otherwise.
                if (_head != null) _head.localScale = Vector3.one * 0.001f;
                int hidden = HideHeadParts(eyes.position);
                _cam = "fp";
                return "cam: first person at " + pos.ToString("F2") + ", " + hidden + " head part(s) shadow-only";
            }

            Vector3 centre = _chair.position + Vector3.up * 0.7f;
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * _chair.forward;
            Vector3 at = centre + dir * dist + Vector3.up * (height - 0.7f);
            cam.OverrideTransform(at, Quaternion.LookRotation(centre - at, Vector3.up), 0f, false);
            _cam = "out";
            return "cam: outside, " + dist.ToString("F1") + " m at " + yaw.ToString("F0") + " deg";
        }

        internal static string Describe()
        {
            if (!Active) return "demo: not sitting; chairs in scene: " + FindChairs().Count;
            var s = _ik.BodyIK.solvers;
            return "demo: " + Path(_chair) + " seat " + SeatOffset.ToString("F2") + " yaw " + SeatYaw.ToString("F0")
                + " grip " + GripOffset.ToString("F2") + " rotW " + GripRotationWeight.ToString("F2")
                + " | IK on=" + _ik.BodyIK.enabled + " L " + (s.leftHand.target != null) + " R " + (s.rightHand.target != null)
                + " | handL " + _avatar.transform.InverseTransformPoint(s.leftHand.bone3.transform.position).ToString("F2")
                + " gripL " + _avatar.transform.InverseTransformPoint(_leftGrip.position).ToString("F2")
                + " | hip gap " + HipGap(out string seat).ToString("F3") + " over '" + seat + "'"
                + " | head " + _chair.InverseTransformPoint(_ik.BodyIK.references.head.position).ToString("F2")
                + " shoulderL " + _chair.InverseTransformPoint(_ik.BodyIK.references.leftUpperArm.position).ToString("F2")
                + " elbowL " + _chair.InverseTransformPoint(_ik.BodyIK.references.leftForearm.position).ToString("F2")
                + " lean " + SpineGoal.ToString("F2") + " w " + SpineWeight.ToString("F2")
                + " | cam " + _cam;
        }

        /// <summary>
        /// Both feet in the chair's space: ankle, the first child bone (toe) and what a ray straight down from the
        /// ankle hits on the chair, so the shoes can be measured against the footrest plates.
        /// </summary>
        internal static string Feet()
        {
            if (!Active) return "feet: sit first ('chassis demo')";
            return "feet (solved): " + _solvedFeet + " | target " + FootOffset.ToString("F2")
                + " rotW " + FootRotationWeight.ToString("F2") + " rot " + FootRotation.ToString("F0");
        }

        private static string Foot(Transform ankle, string side)
        {
            string line = side + " ankle " + _chair.InverseTransformPoint(ankle.position).ToString("F3");
            if (ankle.childCount > 0) line += " toe " + _chair.InverseTransformPoint(ankle.GetChild(0).position).ToString("F3");
            line += " sideAxis " + _chair.InverseTransformDirection(ankle.right).ToString("F2");
            return line;
        }

        private static List<GameObject> _marks;

        /// <summary>Small spheres at a chair-space point and its mirror, to find chair parts that have no collider.</summary>
        internal static string Mark(Vector3 p, float size)
        {
            if (!Active) return "mark: sit first ('chassis demo')";
            if (_marks == null) _marks = new List<GameObject>();
            foreach (var x in new[] { p.x, -p.x })
            {
                var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                m.name = "ChassisMark";
                var col = m.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);
                m.transform.SetParent(_chair, false);
                m.transform.localPosition = new Vector3(x, p.y, p.z);
                m.transform.localScale = Vector3.one * size;
                var r = m.GetComponent<Renderer>();
                if (r != null)
                {
                    // The primitive's default material has no shader in the build and renders nothing.
                    if (MarkMaterial() != null) r.sharedMaterial = _markMaterial;
                    r.material.color = x >= 0f ? Color.red : Color.blue;
                }
                _marks.Add(m);
            }
            return "mark at " + p.ToString("F3") + " and mirror, " + _marks.Count + " marks, material " + _markSource;
        }

        /// <summary>Spheres on both foot bones (red right, blue left), to compare the rendered bones with their targets.</summary>
        internal static string MarkBones(float size)
        {
            if (!Active) return "mark: sit first ('chassis demo')";
            if (_marks == null) _marks = new List<GameObject>();
            var s = _ik.BodyIK.solvers;
            foreach (var pair in new[] { new KeyValuePair<Transform, Color>(s.rightFoot.bone3.transform, Color.red), new KeyValuePair<Transform, Color>(s.leftFoot.bone3.transform, Color.blue) })
            {
                var m = GameObject.CreatePrimitive(PrimitiveType.Cube);
                m.name = "ChassisBoneMark";
                var col = m.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);
                m.transform.SetParent(pair.Key, false);
                m.transform.localPosition = Vector3.zero;
                m.transform.localScale = Vector3.one * size / Mathf.Max(0.0001f, pair.Key.lossyScale.x);
                var r = m.GetComponent<Renderer>();
                if (r != null)
                {
                    if (MarkMaterial() != null) r.sharedMaterial = _markMaterial;
                    r.material.color = pair.Value;
                }
                _marks.Add(m);
            }
            return "bone marks on " + s.rightFoot.bone3.transform.name + " / " + s.leftFoot.bone3.transform.name;
        }

        private static Material _markMaterial;
        private static string _markSource = "none";

        private static Material MarkMaterial()
        {
            if (_markMaterial != null) return _markMaterial;
            foreach (var name in new[] { "Universal Render Pipeline/Unlit", "Unlit/Color", "Universal Render Pipeline/Lit", "Standard" })
            {
                var sh = Shader.Find(name);
                if (sh == null) continue;
                _markMaterial = new Material(sh);
                _markSource = "shader " + name;
                return _markMaterial;
            }
            var chairRenderer = _chair.GetComponentInChildren<Renderer>();
            if (chairRenderer != null && chairRenderer.sharedMaterial != null)
            {
                _markMaterial = new Material(chairRenderer.sharedMaterial);
                _markSource = "copy of the chair material";
                return _markMaterial;
            }
            Core.Log.Warning("[Demo] mark: no shader found and the chair has no material, marks stay invisible");
            return null;
        }

        internal static string ClearMarks()
        {
            int n = 0;
            if (_marks != null) { foreach (var m in _marks) if (m != null) { UnityEngine.Object.Destroy(m); n++; } _marks.Clear(); }
            return "marks cleared: " + n;
        }

        /// <summary>
        /// Top of the chair's seat under the thighs: the highest chair collider a ray hits straight down, half way
        /// between hip and knee. NaN when the ray hits no collider of the chair.
        /// </summary>
        private static float SeatTop(out string what)
        {
            var refs = _ik.BodyIK.references;
            Vector3 hip = (refs.leftThigh.position + refs.rightThigh.position) * 0.5f;
            Vector3 knee = (refs.leftCalf.position + refs.rightCalf.position) * 0.5f;
            Vector3 probe = Vector3.Lerp(hip, knee, 0.5f);
            float top = float.NaN;
            what = "nothing";
            foreach (var h in Physics.RaycastAll(probe + Vector3.up * 0.6f, Vector3.down, 1.5f))
            {
                if (h.collider == null || !h.collider.transform.IsChildOf(_chair)) continue;
                if (float.IsNaN(top) || h.point.y > top) { top = h.point.y; what = h.collider.gameObject.name; }
            }
            return top;
        }

        /// <summary>Hip joints (thigh bone roots) above the seat top, in metres; NaN without a seat hit.</summary>
        internal static float HipGap(out string what)
        {
            float top = SeatTop(out what);
            if (float.IsNaN(top)) return float.NaN;
            var refs = _ik.BodyIK.references;
            return (refs.leftThigh.position.y + refs.rightThigh.position.y) * 0.5f - top;
        }

        /// <summary>Moves the seat point up or down so the hip joints sit <paramref name="gap"/> above the seat top.</summary>
        internal static string Fit(float gap)
        {
            if (!Active) return "fit: sit first ('chassis demo')";
            float now = HipGap(out string what);
            if (float.IsNaN(now)) return "fit: the ray under the thighs hit no collider of the chair";
            // SeatOffset is in chair space; the chair stands upright, so its local y is world y.
            SeatOffset.y += gap - now;
            return "fit: hips were " + now.ToString("F3") + " m above '" + what + "', seat y now " + SeatOffset.y.ToString("F3");
        }

        internal static string Path(Transform t)
        {
            string p = t.name;
            for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
            return p;
        }
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class SitDemo_Player_Update_Patch
    {
        private static void Postfix(Player __instance)
        {
            try { SitDemo.Pose(__instance); }
            catch (Exception e) { Core.Log.Error("[Demo] pose failed, standing up: " + e); SitDemo.Stop(); }
        }
    }
}
#endif
