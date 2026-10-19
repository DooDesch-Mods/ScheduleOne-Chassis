using Chassis.Compat;
using System;
using HarmonyLib;

namespace Chassis.Rideables
{
    /// <summary>
    /// The drivable wheelchair as its own vehicle code: a Shitbox clone used as the network hull (the S1-Forklift
    /// pattern), shrunk to the placed wheelchair prop. The hull carries physics, seats and FishNet identity; the look
    /// is a copy of a scene prop that every peer builds for itself when the vehicle starts (<see cref="WheelchairModel"/>).
    ///
    /// The hull is added to FishNet's spawnable prefabs in VehicleManager.Awake, the same moment Yesla adds its car,
    /// so both mods land at the same PrefabIds on every peer. It is not added to VehicleManager.VehiclePrefabs: the
    /// wheelchair is never bought, saved or loaded.
    /// </summary>
    internal static class WheelchairType
    {
        internal const string Code = "chassis_wheelchair";
        internal const string Name = "Wheelchair";
        private const string DonorCode = "shitbox";

        /// <summary>Child of the vehicle that carries the copied prop meshes; the profile frame.</summary>
        internal const string FrameName = "ChassisModel";

        /// <summary>Pivots the big wheels turn on; the hands ride their rims.</summary>
        internal const string RimLeft = "RimPivot_L";
        internal const string RimRight = "RimPivot_R";

        internal const float TopSpeedKmh = 10f;
        private const float ComHeight = 0.22f;
        internal const float MassKg = 110f;

        // Wheel centres and radii in the prop's space (from the placed props, all at scale 1).
        internal static readonly Vector3 BigWheel = new Vector3(0.297f, 0.327f, -0.233f);
        internal const float BigRadius = 0.33f;
        internal static readonly Vector3 Caster = new Vector3(0.28f, 0.099f, 0.306f);
        internal const float CasterRadius = 0.1f;
        private const float Suspension = 0.04f;

        private static GameObject _prefab;

        internal static LandVehicle Prefab => _prefab != null ? _prefab.GetComponent<LandVehicle>() : null;

        internal static bool Is(LandVehicle v) => v != null && string.Equals(v.vehicleCode, Code, StringComparison.Ordinal);

        internal static void Register(VehicleManager manager)
        {
            if (manager == null) { Core.Log.Error("[Wheelchair] no VehicleManager - the wheelchair is not registered."); return; }
            if (_prefab == null && !Build(manager)) return;

            var nob = _prefab.GetComponent<FishNetObject>();
            if (nob == null) { Core.Log.Error("[Wheelchair] the hull has no NetworkObject - it cannot spawn."); return; }
            var spawnables = InstanceFinder.NetworkManager?.SpawnablePrefabs;
            if (spawnables == null) { Core.Log.Error("[Wheelchair] FishNet's SpawnablePrefabs is not available - the wheelchair is not registered."); return; }
            int count = spawnables.GetObjectCount();
            for (int i = 0; i < count; i++)
            {
                var candidate = spawnables.GetObject(true, i);
                if (Compat.Interop.Same(candidate, nob)) { Core.Log.Msg("[Wheelchair] already spawnable at index " + i + "."); return; }
            }
            spawnables.AddObject(nob);
            Core.Log.Msg("[Wheelchair] registered '" + Code + "' as FishNet spawnable #" + count + ", prefab id " + nob.PrefabId + ".");
        }

        private static bool Build(VehicleManager manager)
        {
            LandVehicle donor = manager.GetVehiclePrefab(DonorCode);
            if (donor == null) { Core.Log.Error("[Wheelchair] donor '" + DonorCode + "' not found - the wheelchair is not registered."); return false; }

            GameObject staging = new GameObject("Chassis_InactiveStaging");
            staging.SetActive(false);
            GameObject clone;
            try
            {
                clone = UnityEngine.Object.Instantiate(donor.gameObject, staging.transform, false);
                clone.SetActive(false);
                clone.transform.SetParent(null, false);
            }
            finally { UnityEngine.Object.DestroyImmediate(staging); }

            clone.name = "ChassisWheelchair";
            clone.transform.position = new Vector3(0f, -100f, 0f);
            UnityEngine.Object.DontDestroyOnLoad(clone);

            try
            {
                Shrink(clone.GetComponent<LandVehicle>());
            }
            catch (Exception e)
            {
                Core.Log.Error("[Wheelchair] shrinking the hull failed - the wheelchair is not registered: " + e);
                UnityEngine.Object.DestroyImmediate(clone);
                return false;
            }
            _prefab = clone;
            Core.Log.Msg("[Wheelchair] built the '" + Code + "' hull from '" + DonorCode + "'.");
            return true;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static void Shrink(LandVehicle lv)
        {
            var root = lv.transform;
            lv.vehicleCode = Code;
            lv.vehicleName = Name;
            lv.vehiclePrice = 0f;
            lv.TopSpeed = TopSpeedKmh;
            lv.SpawnAsPlayerOwned = true; // "Enter vehicle" needs it; price 0 keeps it out of the net worth, and only VehicleManager.SpawnVehicle puts a vehicle into the save list.

            // Nothing of the car stays visible or solid; wheel colliders and the "get in" trigger stay.
            var intObj = Find(root, "IntObj");
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c.As<WheelCollider>() != null) continue;
                bool interaction = intObj != null && (c.transform == intObj || c.transform.IsChildOf(intObj));
                c.enabled = interaction;
            }
            foreach (var l in root.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var a in root.GetComponentsInChildren<AudioSource>(true)) { a.enabled = false; a.playOnAwake = false; }

            // One box for the chair body, above the wheels' ground contact.
            var body = new GameObject("ChassisBody");
            body.transform.SetParent(root, false);
            body.layer = root.gameObject.layer;
            var box = body.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.68f, -0.06f);
            box.size = new Vector3(0.62f, 0.72f, 0.72f);

            if (intObj != null)
            {
                intObj.SetParent(root, false);
                intObj.localPosition = new Vector3(0f, 0.55f, 0f);
                intObj.localRotation = Quaternion.identity;
                foreach (var bc in intObj.GetComponentsInChildren<BoxCollider>(true))
                {
                    bc.enabled = true; bc.isTrigger = true;
                    bc.center = Vector3.zero;
                    bc.size = new Vector3(0.7f, 0.9f, 0.9f);
                    bc.transform.localScale = Vector3.one;
                }
            }
            else Core.Log.Warning("[Wheelchair] the donor has no IntObj - nobody can get in.");

            Wheels(lv);
            Seat(lv);

            var rb = lv.GetComponent<Rigidbody>();
            rb.mass = MassKg;
            rb.drag = 0.15f;
            rb.angularDrag = 2f;
            // Low and between the axles: the rider is not simulated, and a real chair carries anti-tip bars, so it should
            // not go over backwards on a ramp much below 45 degrees.
            var com = new Vector3(0f, ComHeight, -0.02f);
            var comT = Find(root, "CenterOfMass");
            if (comT != null) comT.localPosition = com;
            rb.centerOfMass = com;

            // A person pushing rims: full pull from standstill, gone just above the top speed. LandVehicle drives
            // each drive wheel with value * diffGearing / 2.
            lv.diffGearing = 1f;
            var curve = new AnimationCurve();
            curve.AddKey(0f, 100f); curve.AddKey(7f, 90f); curve.AddKey(TopSpeedKmh, 40f); curve.AddKey(TopSpeedKmh + 1.5f, 0f);
            lv.motorTorque = curve;
            lv.BrakeForceMultiplier = 0.06f;
            lv.handBrakeForce = 60f;
            lv.maxSteeringAngle = 45f;
            lv.downforce = 0f;
            lv.reverseMultiplier = 0.6f;

            if (lv.boundingBox != null)
            {
                lv.boundingBox.transform.localPosition = Vector3.zero;
                lv.boundingBox.transform.localRotation = Quaternion.identity;
                lv.boundingBox.transform.localScale = Vector3.one;
                lv.boundingBox.center = new Vector3(0f, 0.55f, 0f);
                lv.boundingBox.size = new Vector3(0.7f, 1.1f, 0.9f);
            }

            var camOrigin = Find(root, "CameraOrigin");
            if (camOrigin != null) camOrigin.localPosition = new Vector3(0f, 1.2f, 0f);
        }

        private static void Wheels(LandVehicle lv)
        {
            var root = lv.transform;
            Wheel fl = null, fr = null, rl = null, rr = null;
            foreach (var w in root.GetComponentsInChildren<Wheel>(true))
            {
                string n = w.name.ToLowerInvariant().Replace("_", "").Replace(" ", "");
                if (n.Contains("fl")) fl = w; else if (n.Contains("fr")) fr = w; else if (n.Contains("rl")) rl = w; else if (n.Contains("rr")) rr = w;
            }
            if (fl == null || fr == null || rl == null || rr == null)
                throw new InvalidOperationException("the donor's wheels are not named FL/FR/RL/RR");

            Place(lv, fl, new Vector3(-Caster.x, Caster.y, Caster.z), CasterRadius, "CasterPivot_L");
            Place(lv, fr, Caster, CasterRadius, "CasterPivot_R");
            Place(lv, rl, new Vector3(-BigWheel.x, BigWheel.y, BigWheel.z), BigRadius, RimLeft);
            Place(lv, rr, BigWheel, BigRadius, RimRight);

            lv.driveWheels = Compat.Interop.Arr(rl.wheelCollider, rr.wheelCollider);
            lv.steerWheels = Compat.Interop.Arr(fl.wheelCollider, fr.wheelCollider);
            lv.handbrakeWheels = Compat.Interop.Arr(rl.wheelCollider, rr.wheelCollider);
        }

        /// <summary>
        /// Puts a donor wheel at a chair wheel. The wheel's model is an empty pivot: Wheel writes the collider's world
        /// pose into it each physics step, and the prop's mesh hangs under it later, keeping its own import rotation.
        /// </summary>
        private static void Place(LandVehicle lv, Wheel w, Vector3 centre, float radius, string pivotName)
        {
            var root = lv.transform;
            w.transform.SetParent(root, false);
            // The collider hangs the wheel half its suspension travel below the transform at rest.
            w.transform.localPosition = centre + Vector3.up * (Suspension * 0.5f);
            w.transform.localRotation = Quaternion.identity;
            w.transform.localScale = Vector3.one;

            var wc = w.wheelCollider;
            // On the donor the collider sits on the wheel's own transform; only a separate child is reset.
            if (wc.transform != w.transform)
            {
                wc.transform.localPosition = Vector3.zero;
                wc.transform.localRotation = Quaternion.identity;
                wc.transform.localScale = Vector3.one;
            }
            wc.center = Vector3.zero;
            wc.radius = radius;
            wc.mass = 2f;
            wc.suspensionDistance = Suspension;
            var spring = wc.suspensionSpring;
            spring.spring = 9000f;
            spring.damper = 900f;
            spring.targetPosition = 0.5f;
            wc.suspensionSpring = spring;
            // Tyre forces act at the height of the centre of mass, so pulling on the rims does not lift the casters
            // and flip the chair over backwards.
            wc.forceAppPointDistance = ComHeight;

            var container = new GameObject(pivotName + "_Container").transform;
            container.SetParent(w.transform, false);
            var pivot = new GameObject(pivotName).transform;
            pivot.SetParent(container, false);
            w.modelContainer = container;
            w.wheelModel = pivot;
            w.DriftParticlesEnabled = false;
            w.DriftAudioEnabled = false;
            if (w.staticCollider != null) w.staticCollider.enabled = false;
        }

        private static void Seat(LandVehicle lv)
        {
            var seats = lv.Seats;
            if (seats == null || seats.Length == 0) throw new InvalidOperationException("the donor has no seats");
            VehicleSeat driver = null;
            foreach (var s in seats) if (s != null && s.isDriverSeat) { driver = s; break; }
            if (driver == null) driver = seats[0];
            driver.transform.SetParent(lv.transform, false);
            driver.transform.localPosition = new Vector3(0f, 0.5f, -0.12f);
            driver.transform.localRotation = Quaternion.identity;
            driver.transform.localScale = Vector3.one;
            foreach (var s in seats) if (s != null && s != driver) s.gameObject.SetActive(false);
            lv.Seats = Compat.Interop.Arr(driver);

            // Out to the left first, then the right, then behind.
            var exits = Find(lv.transform, "ExitPoints");
            if (exits == null) { Core.Log.Warning("[Wheelchair] the donor has no ExitPoints - leaving drops the player at the car's old doors."); return; }
            exits.SetParent(lv.transform, false);
            exits.localPosition = Vector3.zero; exits.localRotation = Quaternion.identity; exits.localScale = Vector3.one;
            // The player is set down with the capsule centre on the point, and vanilla only takes a point whose 0.35 m
            // sphere touches nothing; 1 m up keeps both clear of the ground.
            var spots = new[] { new Vector3(-0.85f, 1f, 0f), new Vector3(0.85f, 1f, 0f), new Vector3(0f, 1f, -1.0f), new Vector3(0f, 1f, 1.0f) };
            for (int i = 0; i < exits.childCount; i++)
            {
                exits.GetChild(i).localPosition = spots[Mathf.Min(i, spots.Length - 1)];
                exits.GetChild(i).localRotation = Quaternion.identity;
            }
        }

        [HarmonyPatch(typeof(VehicleManager), nameof(VehicleManager.Awake))]
        private static class RegisterOnAwake
        {
            private static void Postfix(VehicleManager __instance)
            {
                try { Register(__instance); }
                catch (Exception e) { Core.Log.Error("[Wheelchair] registration failed: " + e); }
            }
        }
    }
}
