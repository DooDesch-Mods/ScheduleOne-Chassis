using System;
using System.Collections.Generic;
using HarmonyLib;

namespace Chassis.Seating
{
    /// <summary>
    /// A seated player's head (and the pupils with it) turns where that player looks, instead of towards the nearest
    /// player as vanilla does. The body stays on the seat.
    ///
    /// Nothing is networked: vanilla already syncs every player's camera rotation (<c>Player.CameraRotation</c>, a
    /// SyncVar written by the owner each frame) and copies it into <c>Player.MimicCamera</c> on every peer. Each peer
    /// turns each seated head from that camera through <c>AvatarLookController.OverrideLookTarget</c>, in a prefix to
    /// its LateUpdate, the frame step that reads and clears the override. The local player's own head reads the
    /// PlayerCamera directly, without the sync's delay.
    ///
    /// Priority <see cref="Priority"/> sits above vanilla's forced look target (100) and below the paranoia effect
    /// (200), which keeps making every head stare at the paranoid local player. Other code can point a seated head at
    /// a point for a frame with <see cref="Request"/>, for example a hand reaching for a switch.
    ///
    /// Preference <c>HeadFollowsView</c> turns it off on this peer: heads then turn as vanilla has it.
    /// </summary>
    [HarmonyPatch(typeof(AvatarLookController), "LateUpdate")]
    internal static class HeadLook
    {
        /// <summary>Override priority of the view direction.</summary>
        internal const int Priority = 150;
        /// <summary>How far ahead of the head the look point sits; far enough that the eyes do not cross.</summary>
        private const float Reach = 10f;

        /// <summary>Preference HeadFollowsView, also set by `chassis head on|off` for the session.</summary>
        internal static bool Enabled = true;

        private struct Seated
        {
#if IL2CPP
            internal IntPtr Key;
#else
            internal object Key;
#endif
            internal AvatarLookController Look;
            internal SeatRig Rig;
        }

        private struct Want
        {
            internal Vector3 At;
            internal int Priority;
            internal int Frame;
        }

        private static readonly List<Seated> _seated = new List<Seated>();
        private static readonly Dictionary<int, Want> _requests = new Dictionary<int, Want>();
        private static readonly HashSet<SeatRig> _announced = new HashSet<SeatRig>();
        private static readonly List<SeatRig> _gone = new List<SeatRig>();
        private static readonly HashSet<SeatRig> _noLook = new HashSet<SeatRig>();
        private static int _frame = -1;
        private static bool _failed;

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

        /// <summary>
        /// Points a seated player's head at a world point instead of the view, for this frame and the next. Call it
        /// every frame while it should hold. A lower priority than <see cref="Priority"/> is ignored.
        /// </summary>
        internal static void Request(Player p, Vector3 world, int priority)
        {
            if (p == null) { Core.Log.Warning("[Head] look request without a player ignored."); return; }
            _requests[p.GetInstanceID()] = new Want { At = world, Priority = priority, Frame = Time.frameCount };
        }

        internal static void Reset()
        {
            _seated.Clear();
            _requests.Clear();
            _announced.Clear();
            _noLook.Clear();
            _frame = -1;
        }

        /// <summary>Collects the look controllers of seated players once per frame.</summary>
        private static void Refresh()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            _seated.Clear();
            foreach (var rig in Occupants.All)
            {
                if (!rig.Alive || rig.Player == null || rig.IsPusher) continue;
                var look = rig.Avatar.LookController;
                if (look == null)
                {
                    if (_noLook.Add(rig)) Core.Log.Warning("[Head] " + rig.Name + "'s avatar has no look controller; the head keeps vanilla's look.");
                    continue;
                }
#if IL2CPP
                _seated.Add(new Seated { Key = look.Pointer, Look = look, Rig = rig });
#else
                _seated.Add(new Seated { Key = look, Look = look, Rig = rig });
#endif
            }
            if (_announced.Count == 0) return;
            _gone.Clear();
            foreach (var rig in _announced)
            {
                bool still = false;
                foreach (var s in _seated) if (s.Rig == rig) { still = true; break; }
                if (!still) _gone.Add(rig);
            }
            foreach (var rig in _gone) { _announced.Remove(rig); _noLook.Remove(rig); }
        }

        private static void Prefix(AvatarLookController __instance)
        {
            if (!Enabled) return;
            try
            {
                Refresh();
                if (_seated.Count == 0) return;
#if IL2CPP
                IntPtr key = __instance.Pointer;
                foreach (var s in _seated) if (s.Key == key) { Apply(s); return; }
#else
                foreach (var s in _seated) if (ReferenceEquals(s.Key, __instance)) { Apply(s); return; }
#endif
            }
            catch (Exception e)
            {
                if (!_failed) Core.Log.Error("[Head] turning a seated head failed, vanilla's look stays; logged once until it works again: " + e);
                _failed = true;
            }
        }

        private static void Apply(Seated s)
        {
            var rig = s.Rig;
            Vector3 target = Target(s.Look, rig, out int priority, out string source);
            s.Look.OverrideLookTarget(target, priority, false);
            _failed = false;
            if (_announced.Add(rig))
                Core.Log.Msg("[Head] " + rig.Name + " in seat " + rig.SeatIndex + ": head follows " + source + " (priority " + priority + ").");
        }

        /// <summary>The point the head looks at this frame: a request, else straight ahead along the player's view.</summary>
        private static Vector3 Target(AvatarLookController look, SeatRig rig, out int priority, out string source)
        {
            var p = rig.Player;
            if (_requests.TryGetValue(p.GetInstanceID(), out var want))
            {
                if (Time.frameCount - want.Frame <= 1 && want.Priority >= Priority)
                {
                    priority = want.Priority;
                    source = "a request";
                    return want.At;
                }
                if (Time.frameCount - want.Frame > 1) _requests.Remove(p.GetInstanceID());
            }
            priority = Priority;
            Vector3 dir = View(p, out source);
            Transform head = look.HeadBone;
            Vector3 from = head != null ? head.position : rig.Frame.TransformPoint(rig.Profile.Eyes);
            return from + dir * Reach;
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

        /// <summary>`chassis head`: per seated player the view, the look target the IK aims at and the head's rotation in the seat frame.</summary>
        internal static string Describe()
        {
            var sb = new System.Text.StringBuilder("head: follows the view " + (Enabled ? "on" : "off"));
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
                var c = System.Globalization.CultureInfo.InvariantCulture;
                sb.Append(" aim-vs-view ").Append(aimOff.ToString("F1", c))
                  .Append(" aim ").Append(look.Aim != null && look.Aim.enabled ? "on" : "off")
                  .Append(" headq ").Append(q.x.ToString("F4", c)).Append(' ').Append(q.y.ToString("F4", c)).Append(' ')
                  .Append(q.z.ToString("F4", c)).Append(' ').Append(q.w.ToString("F4", c));
                Quaternion b = Quaternion.Inverse(rig.Frame.rotation) * rig.Avatar.transform.rotation;
                sb.Append(" bodyq ").Append(b.x.ToString("F4", c)).Append(' ').Append(b.y.ToString("F4", c)).Append(' ')
                  .Append(b.z.ToString("F4", c)).Append(' ').Append(b.w.ToString("F4", c));
            }
            if (n == 0) sb.Append("\nhead: no seated player");
            return sb.ToString();
        }
    }
}
