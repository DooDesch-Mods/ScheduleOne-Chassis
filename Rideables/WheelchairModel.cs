using System;

namespace Chassis.Rideables
{
    /// <summary>
    /// The look of a spawned wheelchair: a copy of a placed wheelchair prop's model under the frame "ChassisModel",
    /// built by every peer for its own copy of the vehicle when it starts. The big wheels and the casters move under
    /// the hull's wheel pivots, so they turn with the physics wheels and the hands can follow the rims.
    /// </summary>
    internal static class WheelchairModel
    {
        internal static void Build(LandVehicle lv)
        {
            var root = lv.transform;
            if (root.Find(WheelchairType.FrameName) != null) return;

            var template = FindTemplate();
            if (template == null)
            {
                Core.Log.Error("[Wheelchair] no placed wheelchair prop to copy the look from - this wheelchair stays invisible.");
                return;
            }

            var frame = new GameObject(WheelchairType.FrameName).transform;
            frame.SetParent(root, false);
            var model = UnityEngine.Object.Instantiate(template.gameObject, frame, false);
            model.name = "wheelchair";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c);
            foreach (var b in model.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.Destroy(b);
            // The template can be the prop the swap just hid, whose renderers are off; the copy is the visible look.
            int shown = 0;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) if (!r.enabled) { r.enabled = true; shown++; }
            if (shown > 0) Core.Log.Msg("[Wheelchair] template " + template.parent.name + " was hidden, " + shown + " renderer(s) switched on in the copy.");

            int moved = 0;
            moved += Hang(lv, frame, model.transform, "Wheel", WheelchairType.RimRight);
            moved += Hang(lv, frame, model.transform, "Wheel (1)", WheelchairType.RimLeft);
            moved += Hang(lv, frame, model.transform, "SmallWheel", "CasterPivot_R");
            moved += Hang(lv, frame, model.transform, "SmallWheel (1)", "CasterPivot_L");
            Core.Log.Msg("[Wheelchair] look built from " + template.name + " for " + lv.name + " (net " + Net.Ids.Of(lv) + "), " + moved + "/4 wheels on pivots.");
        }

        /// <summary>The model child ("wheelchair") of any placed prop; they are the same mesh.</summary>
        private static Transform FindTemplate()
        {
            foreach (var rb in UnityEngine.Object.FindObjectsOfType<Rigidbody>())
            {
                if (rb == null || rb.name.IndexOf("wheelchair", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (rb.GetComponent<LandVehicle>() != null) continue;
                var model = rb.transform.Find("wheelchair");
                if (model != null) return model;
            }
            return null;
        }

        private static int Hang(LandVehicle lv, Transform frame, Transform model, string meshName, string pivotName)
        {
            var mesh = model.Find(meshName);
            Transform pivot = null;
            foreach (var t in lv.GetComponentsInChildren<Transform>(true)) if (t.name == pivotName) { pivot = t; break; }
            if (mesh == null || pivot == null)
            {
                Core.Log.Warning("[Wheelchair] '" + meshName + "' or pivot '" + pivotName + "' missing; that wheel stays fixed to the frame.");
                return 0;
            }
            // Put the pivot on the wheel's centre at rest, aligned with the frame, then hang the mesh under it with its
            // own import rotation. Wheel.FixedUpdateWheel moves the pivot from then on.
            pivot.position = mesh.position;
            pivot.rotation = frame.rotation;
            mesh.SetParent(pivot, true);
            return 1;
        }
    }
}
