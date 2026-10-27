using System;
using System.Collections.Generic;
using Chassis.Api;
using Chassis.Vehicles;
using HarmonyLib;

namespace Chassis.Tune
{
    /// <summary>
    /// Driving values from the installed parts. The stock values of a car are read once, the first time a part changes
    /// them; every apply sets stock times the parts' factors, so the result never drifts. Vanilla reads all of these
    /// live and never writes them, except the tyre friction, which <see cref="Wheel.ApplyFriction"/> rewrites every
    /// physics step on the driver's peer: <see cref="GripPatch"/> scales it right after.
    /// </summary>
    internal static class Handling
    {
        internal sealed class Stock
        {
            internal Keyframe[] Torque;
            internal WrapMode Pre, Post;
            internal float Brake, HandBrake, Downforce, Steer, Mass, Pitch = 1f;
            internal readonly List<WheelCollider> Wheels = new List<WheelCollider>();
            internal readonly List<JointSpring> Springs = new List<JointSpring>();
            internal readonly List<Vector3> Centers = new List<Vector3>();
            internal GameVehicleSound Sound;
        }

        // Grip factor per car, by the LandVehicle's instance id; read in every physics step of the driver.
        private static readonly Dictionary<int, float> _grip = new Dictionary<int, float>();

        internal static float GripOf(LandVehicle v)
        {
            if (_grip.Count == 0 || v == null) return 1f;
            return _grip.TryGetValue(v.GetInstanceID(), out float g) ? g : 1f;
        }

        internal static void Apply(Tune.Apply.Car c, TuningStats s)
        {
            string key = s.ToString();
            if (key == c.HandlingKey) return;
            bool stock = key == "stock";
            if (c.Stock == null)
            {
                if (stock) { c.HandlingKey = key; c.Stats = s; return; }
                c.Stock = Read(c.V);
            }
            var lv = c.V;
            var st = c.Stock;

            var curve = new AnimationCurve();
            foreach (var k0 in st.Torque)
            {
                var k = k0;
                k.time *= s.Gearing;
                k.value *= s.Torque;
                float slope = s.Torque / Mathf.Max(0.01f, s.Gearing);
                k.inTangent *= slope;
                k.outTangent *= slope;
                curve.AddKey(k);
            }
            curve.preWrapMode = st.Pre;
            curve.postWrapMode = st.Post;
            lv.motorTorque = curve;
            lv.BrakeForceMultiplier = st.Brake * s.Brake;
            lv.handBrakeForce = st.HandBrake * s.Brake;
            lv.downforce = st.Downforce * s.Downforce;
            lv.maxSteeringAngle = st.Steer * s.Steer;
            if (lv.Rb != null) lv.Rb.mass = st.Mass * s.Mass;
            float drop = 0f;
            for (int i = 0; i < st.Wheels.Count; i++)
            {
                var wc = st.Wheels[i];
                if (wc == null) continue;
                var spring = st.Springs[i];
                spring.spring *= s.Spring;
                spring.damper *= s.Damper;
                wc.suspensionSpring = spring;
                // The wheel hangs from its centre; a higher centre puts the body lower over the same wheel. The drop is
                // in metres along the car's up, whatever the wheel transform's scale.
                drop = s.RideHeight * DropScale(wc);
                wc.center = st.Centers[i] + wc.transform.InverseTransformVector(lv.transform.up * drop);
            }
            if (st.Sound != null) st.Sound.EnginePitchMultiplier = st.Pitch * s.EnginePitch;
            if (Mathf.Abs(s.Grip - 1f) > 0.0001f) _grip[lv.GetInstanceID()] = s.Grip;
            else _grip.Remove(lv.GetInstanceID());

            c.HandlingKey = key;
            c.Stats = s;
            Core.Log.Msg("[Tuning] " + Registry.Name(lv) + " drives " + key + ": torque " + Tune.Apply.F(Peak(curve)) + " peak, brake x" + Tune.Apply.F(lv.BrakeForceMultiplier) + ", steer " + Tune.Apply.F(lv.maxSteeringAngle) + " deg, mass " + Tune.Apply.F(lv.Rb != null ? lv.Rb.mass : 0f) + " kg, ride " + Tune.Apply.F(drop) + " m.");
        }

        private static Stock Read(LandVehicle lv)
        {
            var st = new Stock();
            var src = lv.motorTorque;
            var keys = src.keys;
            st.Torque = new Keyframe[keys.Length];
            for (int i = 0; i < keys.Length; i++) st.Torque[i] = keys[i];
            st.Pre = src.preWrapMode;
            st.Post = src.postWrapMode;
            st.Brake = lv.BrakeForceMultiplier;
            st.HandBrake = lv.handBrakeForce;
            st.Downforce = lv.downforce;
            st.Steer = lv.maxSteeringAngle;
            st.Mass = lv.Rb != null ? lv.Rb.mass : 0f;
            if (lv.wheels != null)
                foreach (var w in lv.wheels)
                {
                    var wc = w != null ? w.wheelCollider : null;
                    if (wc == null) continue;
                    st.Wheels.Add(wc);
                    st.Springs.Add(wc.suspensionSpring);
                    st.Centers.Add(wc.center);
                }
            st.Sound = lv.GetComponentInChildren<GameVehicleSound>(true);
            if (st.Sound != null) st.Pitch = st.Sound.EnginePitchMultiplier;
            Core.Log.Msg("[Tuning] stock driving of " + Registry.Name(lv) + ": torque " + Tune.Apply.F(Peak(src)) + " peak over " + st.Torque.Length + " key(s), brake x" + Tune.Apply.F(st.Brake) + ", handbrake " + Tune.Apply.F(st.HandBrake) + ", downforce " + Tune.Apply.F(st.Downforce) + ", steer " + Tune.Apply.F(st.Steer) + ", mass " + Tune.Apply.F(st.Mass) + ", " + st.Wheels.Count + " wheel(s)" + (st.Sound == null ? ", no engine sound" : "") + ".");
            return st;
        }

        /// <summary>Tyre radius of a stock car; the ride height of a part is meant for a wheel this size.</summary>
        private const float CarTyreRadius = 0.35f;

        /// <summary>
        /// A bigger wheel drops further, so a lowered RV or truck shows it as clearly as a car: the ride height grows with
        /// the tyre's radius in metres over a car's, never below the part's own value.
        /// </summary>
        private static float DropScale(WheelCollider wc)
        {
            float radius = wc.radius * wc.transform.lossyScale.y;
            return Mathf.Max(1f, radius / CarTyreRadius);
        }

        internal static float Peak(AnimationCurve c)
        {
            float max = 0f;
            var keys = c.keys;
            for (int i = 0; i < keys.Length; i++) max = Mathf.Max(max, keys[i].value);
            return max;
        }

        /// <summary>Stock and current values for the console.</summary>
        internal static string Describe(LandVehicle lv)
        {
            var c = Tune.Apply.Find(lv);
            var st = c?.Stock;
            string now = "torque " + Tune.Apply.F(Peak(lv.motorTorque)) + " peak (last key at " + Tune.Apply.F(LastTime(lv.motorTorque)) + "), brake x" + Tune.Apply.F(lv.BrakeForceMultiplier) + ", handbrake " + Tune.Apply.F(lv.handBrakeForce)
                + ", downforce " + Tune.Apply.F(lv.downforce) + ", steer " + Tune.Apply.F(lv.maxSteeringAngle) + ", mass " + Tune.Apply.F(lv.Rb != null ? lv.Rb.mass : 0f) + ", grip x" + Tune.Apply.F(GripOf(lv));
            var w = lv.wheels != null && lv.wheels.Count > 0 && lv.wheels[0] != null ? lv.wheels[0].wheelCollider : null;
            if (w != null) now += ", spring " + Tune.Apply.F(w.suspensionSpring.spring) + ", damper " + Tune.Apply.F(w.suspensionSpring.damper) + ", wheel centre y " + Tune.Apply.F(w.center.y);
            var snd = lv.GetComponentInChildren<GameVehicleSound>(true);
            if (snd != null) now += ", pitch x" + Tune.Apply.F(snd.EnginePitchMultiplier);
            if (st == null) return "now:   " + now + "\nstock: same (no part changed the driving)";
            string stock = "torque " + Tune.Apply.F(MaxOf(st.Torque)) + " peak, brake x" + Tune.Apply.F(st.Brake) + ", handbrake " + Tune.Apply.F(st.HandBrake) + ", downforce " + Tune.Apply.F(st.Downforce)
                + ", steer " + Tune.Apply.F(st.Steer) + ", mass " + Tune.Apply.F(st.Mass) + (st.Springs.Count > 0 ? ", spring " + Tune.Apply.F(st.Springs[0].spring) + ", damper " + Tune.Apply.F(st.Springs[0].damper) + ", wheel centre y " + Tune.Apply.F(st.Centers[0].y) : "") + ", pitch x" + Tune.Apply.F(st.Pitch);
            string cap = c.Stats != null && c.HandlingKey != "stock" ? ", reverse cap " + Tune.Apply.F(ReversePatch.CapOf(lv, c.Stats)) + " km/h" : "";
            return "now:   " + now + "\nstock: " + stock + "\nparts: " + (c.Stats != null ? c.Stats.ToString() : "stock") + cap;
        }

        private static float MaxOf(Keyframe[] k) { float m = 0f; foreach (var x in k) m = Mathf.Max(m, x.value); return m; }

        private static float LastTime(AnimationCurve c) { var k = c.keys; return k.Length > 0 ? k[k.Length - 1].time : 0f; }

        internal static void Forget(int instanceId) => _grip.Remove(instanceId);

        internal static void Reset() => _grip.Clear();
    }

    /// <summary>
    /// Reverse keeps vanilla's share of the forward speed on a tuned car. Vanilla reads the torque curve at
    /// <c>speed / reverseMultiplier</c> in reverse, so reverse reaches the curve's flat end at a fraction of the forward
    /// speed, and parts multiply that flat end: a tuned car reversed nearly as fast as it drove forward. On a tuned car
    /// the drive wheels get no torque once reverse passes <c>reverseMultiplier</c> x the car's top speed x the parts'
    /// gearing and torque. A car without driving parts stays vanilla.
    /// </summary>
    [HarmonyPatch(typeof(LandVehicle), "ApplyThrottle")]
    internal static class ReversePatch
    {
        private static bool _failed;
        private static int _loggedFor;

        internal static float CapOf(LandVehicle lv, TuningStats s) =>
            lv.reverseMultiplier * lv.TopSpeed * Mathf.Max(1f, s.Gearing) * Mathf.Max(1f, s.Torque);

        private static void Postfix(LandVehicle __instance)
        {
            if (_failed || !__instance.LocalPlayerIsDriver) return;
            try
            {
                if (__instance.currentThrottle >= 0f || __instance.Speed_Kmh > -0.1f) return;
                var c = Tune.Apply.Find(__instance);
                var s = c?.Stats;
                if (s == null || c.HandlingKey == "stock") return;
                float cap = CapOf(__instance, s);
                if (-__instance.Speed_Kmh < cap) return;
                foreach (var wc in __instance.driveWheels) if (wc != null) wc.motorTorque = 0f;
                int id = __instance.GetInstanceID();
                if (_loggedFor != id)
                {
                    _loggedFor = id;
                    Core.Log.Msg("[Tuning] " + Registry.Name(__instance) + " reverses at its cap of " + Tune.Apply.F(cap) + " km/h (" + Tune.Apply.F(__instance.reverseMultiplier) + " x top " + Tune.Apply.F(__instance.TopSpeed) + " km/h x gearing " + Tune.Apply.F(s.Gearing) + " x torque " + Tune.Apply.F(s.Torque) + ").");
                }
            }
            catch (Exception e)
            {
                _failed = true;
                Core.Log.Error("[Tuning] capping the reverse speed failed - tuned cars reverse without a cap for this session: " + e);
            }
        }
    }

    /// <summary>Scales the tyre friction vanilla has just written from the weather settings.</summary>
    [HarmonyPatch(typeof(Wheel), "ApplyFriction")]
    internal static class GripPatch
    {
        private static bool _failed;

        private static void Postfix(Wheel __instance)
        {
            if (_failed) return;
            try
            {
                float g = Handling.GripOf(__instance.vehicle);
                if (g == 1f) return;
                var wc = __instance.wheelCollider;
                var f = wc.forwardFriction;
                f.stiffness *= g;
                wc.forwardFriction = f;
                var sw = wc.sidewaysFriction;
                sw.stiffness *= g;
                wc.sidewaysFriction = sw;
            }
            catch (Exception e)
            {
                _failed = true;
                Core.Log.Error("[Tuning] scaling the tyre grip failed - grip parts stay without effect for this session: " + e);
            }
        }
    }
}
