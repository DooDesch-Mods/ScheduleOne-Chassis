using System;
using HarmonyLib;
using UnityEngine;

namespace Chassis.Seating
{
    /// <summary>
    /// A dialogue that ends while the local player sits in a vehicle leaves the camera where the player body was
    /// left. Vanilla only talks on foot: <c>DialogueCanvas.OnClose</c> calls <c>PlayerCamera.StopTransformOverride</c>,
    /// which parents the camera back to the player root and puts it at head height. While seated that root stays where
    /// the player got in (only the avatar rides along), so after the checkpoint talk from the car the view jumped to
    /// that spot, for example the dealership, and stayed there until the next game start.
    /// The prefix keeps the camera pose of the vehicle view; the postfix puts the camera back into the free override
    /// the vehicle camera set up on entry (<c>VehicleCamera.PlayerEnteredVehicle</c>), so the vehicle camera drives it
    /// again from the next frame. The dialogue's turn of the player root also switched the character controller back
    /// on, which entering a vehicle had switched off; it is switched off again.
    /// </summary>
    [HarmonyPatch(typeof(GameDialogueCanvas), "OnClose")]
    internal static class SeatedDialogueCamera
    {
        private static bool _seated;
        private static Vector3 _pos;
        private static Quaternion _rot;

        private static void Prefix()
        {
            _seated = false;
            try
            {
                var lp = Player.Local;
                if (lp == null || lp.CurrentVehicle == null || !PlayerSingleton<PlayerCamera>.InstanceExists) return;
                var cam = PlayerSingleton<PlayerCamera>.Instance.transform;
                _pos = cam.position;
                _rot = cam.rotation;
                _seated = true;
            }
            catch (Exception e)
            {
                Core.Log.Error("[Dialogue] reading the camera before a dialogue closed in a vehicle failed: " + e);
            }
        }

        private static void Postfix()
        {
            if (!_seated) return;
            _seated = false;
            try
            {
                var lp = Player.Local;
                if (lp == null || lp.CurrentVehicle == null) { Core.Log.Msg("[Dialogue] the player left the vehicle while the dialogue closed - the game's camera reset stays."); return; }
                PlayerSingleton<PlayerCamera>.Instance.OverrideTransform(_pos, _rot, 0f, false);
                if (PlayerSingleton<PlayerMovement>.InstanceExists && PlayerSingleton<PlayerMovement>.Instance.Controller != null)
                    PlayerSingleton<PlayerMovement>.Instance.Controller.enabled = false;
                Core.Log.Msg("[Dialogue] a dialogue closed in " + lp.CurrentVehicle.name + " - the camera stays with the vehicle.");
            }
            catch (Exception e)
            {
                Core.Log.Error("[Dialogue] keeping the vehicle camera after a dialogue failed: " + e);
            }
        }
    }
}
