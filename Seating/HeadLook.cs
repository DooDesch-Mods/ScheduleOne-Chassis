using System;
using System.Collections.Generic;

namespace Chassis.Seating
{
    /// <summary>
    /// A seated player's head (and the pupils with it) turns where that player looks, instead of towards the nearest
    /// player as vanilla does. The body stays on the seat.
    ///
    /// Nothing is networked: vanilla already syncs every player's camera rotation (<c>Player.CameraRotation</c>, a
    /// SyncVar written by the owner each frame) and copies it into <c>Player.MimicCamera</c> on every peer. Each peer
    /// points each seated head along that camera with <c>AvatarLookController.OverrideLookTarget</c>, called from
    /// Update; the controller's LateUpdate of the same frame reads the override and clears it. The local player's own
    /// head reads the PlayerCamera directly, without the sync's delay.
    ///
    /// Priority <see cref="Priority"/> sits above vanilla's forced look target (100) and below the paranoia effect
    /// (200), which keeps making every head stare at the paranoid local player. The controller keeps the highest
    /// override of a frame, so other code points a seated head elsewhere with <see cref="Request"/> each frame at a
    /// higher priority, for example while a hand reaches for a switch.
    ///
    /// Preference <c>HeadFollowsView</c> turns it off on this peer: heads then turn as vanilla has it.
    /// </summary>
    internal static class HeadLook
    {
        /// <summary>Override priority of the view direction.</summary>
        internal const int Priority = 150;
        /// <summary>How far ahead of the head the look point sits; far enough that the eyes do not cross.</summary>
        private const float Reach = 10f;

        /// <summary>Preference HeadFollowsView, also set by `chassis head on|off` for the session.</summary>
        internal static bool Enabled = true;

        private static readonly HashSet<SeatRig> _announced = new HashSet<SeatRig>();
        private static readonly HashSet<SeatRig> _noLook = new HashSet<SeatRig>();
        private static readonly List<SeatRig> _gone = new List<SeatRig>();
        private static readonly HashSet<SeatRig> _seen = new HashSet<SeatRig>();

        internal static void LoadSetting()
        {
            try
            {
                var cat = MelonLoader.MelonPreferences.CreateCategory("Chassis");
                var entry = cat.CreateEntry("HeadFollowsView", true, "Head follows the view",
                    "Seated players' heads turn where they look. Off: heads turn towards the nearest player, as in vanilla.");
                Enabled = entry.Value;
                Core.Log.Msg("[Head] seated heads follow the view " + (Enabled ? "on" : "off") + " (preference HeadFollowsView).");
            }
            catch (Exception e)
            {
                Enabled = true;
                Core.Log.Warning("[Head] reading the HeadFollowsView preference failed - seated heads follow the view: " + e.Message);
            }
        }

        internal static void Reset()
        {
            _announced.Clear();
            _noLook.Clear();
            _seen.Clear();
        }

        /// <summary>
        /// Points a seated player's head at a world point for this frame instead of the view. Call it every frame
        /// from Update while it should hold, with a priority above <see cref="Priority"/>. Returns false when the
        /// player is not seated or has no look controller.
        /// </summary>
        internal static bool Request(Player p, Vector3 world, int priority)
        {
            var rig = Occupants.Of(p);
            var look = rig != null && rig.Alive ? rig.Avatar.LookController : null;
            if (look == null) return false;
            look.OverrideLookTarget(world, priority, false);
            return true;
        }

        internal static void Tick()
        {
            if (!Enabled) return;
            _seen.Clear();
            foreach (var rig in Occupants.All)
            {
                if (!rig.Alive || rig.Player == null || rig.IsPusher) continue;
                var look = rig.Avatar.LookController;
                if (look == null)
                {
                    if (_noLook.Add(rig)) Core.Log.Warning("[Head] " + rig.Name + "'s avatar has no look controller; the head keeps vanilla's look.");
                    continue;
                }
                _seen.Add(rig);
                Vector3 dir = View(rig.Player, out string source);
                Transform head = look.HeadBone;
                Vector3 from = head != null ? head.position : rig.Frame.TransformPoint(rig.Profile.Eyes);
                look.OverrideLookTarget(from + dir * Reach, Priority, false);
                if (_announced.Add(rig))
                    Core.Log.Msg("[Head] " + rig.Name + " in seat " + rig.SeatIndex + ": head follows " + source + " (priority " + Priority + ").");
            }
            if (_announced.Count == _seen.Count && _noLook.Count == 0) return;
            _gone.Clear();
            foreach (var rig in _announced) if (!_seen.Contains(rig)) _gone.Add(rig);
            foreach (var rig in _noLook) if (!rig.Alive) _gone.Add(rig);
            foreach (var rig in _gone) { _announced.Remove(rig); _noLook.Remove(rig); }
        }

        /// <summary>The player's view direction: the local camera, or the synced camera of a remote player.</summary>
        private static Vector3 View(Player p, out string source)
        {
            if (p.IsLocalPlayer && PlayerSingleton<PlayerCamera>.InstanceExists)
            {
                source = "the local camera";
                return PlayerSingleton<PlayerCamera>.Instance.transform.forward;
            }
            source = "the synced camera";
            return p.MimicCamera != null ? p.MimicCamera.forward : p.transform.forward;
        }

        /// <summary>`chassis head`: per seated player the angle between its view and the IK aim, and the head's and body's rotation in the seat frame.</summary>
        internal static string Describe()
        {
            var sb = new System.Text.StringBuilder("head: follows the view " + (Enabled ? "on" : "off"));
            var c = System.Globalization.CultureInfo.InvariantCulture;
            int n = 0;
            foreach (var rig in Occupants.All)
            {
                if (!rig.Alive || rig.Player == null || rig.IsPusher) continue;
                n++;
                var look = rig.Avatar.LookController;
                Vector3 view = View(rig.Player, out _);
                sb.Append("\nhead: ").Append(rig.Name).Append(" seat ").Append(rig.SeatIndex);
                if (look == null) { sb.Append(" no look controller"); continue; }
                float aimOff = -1f;
                if (look.Aim != null && look.LookOrigin != null)
                    aimOff = Vector3.Angle(look.Aim.solver.GetIKPosition() - look.LookOrigin.position, view);
                Transform head = look.HeadBone;
                Quaternion q = head != null ? Quaternion.Inverse(rig.Frame.rotation) * head.rotation : Quaternion.identity;
                Quaternion b = Quaternion.Inverse(rig.Frame.rotation) * rig.Avatar.transform.rotation;
                sb.Append(" aim-vs-view ").Append(aimOff.ToString("F1", c))
                  .Append(" aim ").Append(look.Aim != null && look.Aim.enabled ? "on" : "off");
                Quat(sb, " headq ", q, c);
                Quat(sb, " bodyq ", b, c);
            }
            if (n == 0) sb.Append("\nhead: no seated player");
            return sb.ToString();
        }

        private static void Quat(System.Text.StringBuilder sb, string label, Quaternion q, IFormatProvider c)
        {
            sb.Append(label).Append(q.x.ToString("F4", c)).Append(' ').Append(q.y.ToString("F4", c)).Append(' ')
              .Append(q.z.ToString("F4", c)).Append(' ').Append(q.w.ToString("F4", c));
        }
    }
}
