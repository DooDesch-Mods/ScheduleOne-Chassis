using System;
using System.Collections;
using System.Globalization;
using Chassis.Api;
using Chassis.Net;
using MelonLoader;

namespace Chassis.Vehicles
{
    /// <summary>
    /// A horn for every vehicle: the driver presses G, mods call <see cref="ChassisApi.Honk"/>. The request goes to the
    /// host, which lets it through when the player sits in the vehicle or owns it, and every peer plays the sound at
    /// the vehicle. A flash toggles <c>VehicleLights.HeadlightsOn</c> on the host, a SyncVar that replicates at about
    /// 4 Hz; each step is held longer than that so every peer sees it.
    /// </summary>
    internal static class Horn
    {
        internal const KeyCode Key = KeyCode.G;
        private const float FlashStep = 0.35f;
        private const int Flashes = 2;

        internal static void Tick()
        {
            if (!Input.GetKeyDown(Key) || GameInput.IsTyping) return;
            var lp = Player.Local;
            var lv = lp != null && lp.CurrentVehicle != null ? lp.CurrentVehicle.GetComponent<LandVehicle>() : null;
            if (lv == null || !lv.LocalPlayerIsDriver) return;
            if (lv.State != null && !lv.State.IsAcceptingInput) return;
            Request(lv, false);
        }

        internal static void Request(LandVehicle lv, bool flash)
        {
            if (lv == null) { Core.Log.Warning("[Horn] honk asked for a vehicle that is gone."); return; }
            var lp = Player.Local;
            if (lp == null) { Core.Log.Warning("[Horn] honk asked before the local player exists."); return; }
            ChassisNet.ToHost("req.honk", Ids.Of(lv).ToString(CultureInfo.InvariantCulture), Ids.Of(lp).ToString(CultureInfo.InvariantCulture), flash ? "1" : "0");
        }

        /// <summary>Host: honk for a player who sits in the vehicle or owns it.</summary>
        internal static void HostHonk(int vehicle, int player, bool flash)
        {
            var lv = Registry.ById(vehicle);
            if (lv == null) { Core.Log.Warning("[Horn] honk for net " + vehicle + ", which is not here - dropped."); return; }
            var p = Ids.PlayerById(player);
            if (p == null) { Core.Log.Warning("[Horn] honk from player net " + player + ", who is not here - dropped."); return; }
            if (!Inside(lv, p) && !Fleet.IsOwner(lv, p.PlayerCode))
            {
                Core.Log.Msg("[Horn] " + p.PlayerName + " neither sits in nor owns " + Registry.Name(lv) + " - no honk.");
                return;
            }
            ChassisNet.ToAll("honk", vehicle.ToString(CultureInfo.InvariantCulture));
            if (flash) Flash(lv);
        }

        private static bool Inside(LandVehicle lv, Player p)
        {
            var seats = lv.Seats;
            if (seats == null) return false;
            foreach (var s in seats) if (s != null && s.Occupant == p) return true;
            return false;
        }

        /// <summary>Everyone: the horn sounds at the vehicle.</summary>
        internal static void OnHonk(int vehicle)
        {
            var lv = Registry.ById(vehicle);
            if (lv == null) { Core.Log.Warning("[Horn] honk for net " + vehicle + ", which is not here."); return; }
            var horn = ChassisApi.For(lv)?.Horn;
            try
            {
                var clip = Sounds.Get(horn?.Clip, Sounds.Horn);
                var source = Sounds.On(lv, "ChassisHorn", horn != null ? horn.Position : new Vector3(0f, 0.6f, 1.8f));
                source.PlayOneShot(clip, (horn != null ? horn.Volume : 0.8f) * Sounds.Volume());
                Core.Log.Msg("[Horn] honked " + Registry.Name(lv) + " at " + lv.transform.position.ToString("F0") + ".");
            }
            catch (Exception e) { Core.Log.Error("[Horn] could not play the horn of " + Registry.Name(lv) + ": " + e); }
        }

        /// <summary>Everyone: two chirps when a vehicle locks, one when it unlocks.</summary>
        internal static void OnChirp(int vehicle, bool locked)
        {
            var lv = Registry.ById(vehicle);
            if (lv == null) { Core.Log.Warning("[Horn] lock chirp for net " + vehicle + ", which is not here."); return; }
            if (!PlayChirp(lv)) return;
            if (locked) MelonCoroutines.Start(SecondChirp(lv));
            Core.Log.Msg("[Horn] " + (locked ? "lock" : "unlock") + " chirp on " + Registry.Name(lv) + ".");
        }

        private static IEnumerator SecondChirp(LandVehicle lv)
        {
            yield return new WaitForSeconds(0.22f);
            if (lv == null) { Core.Log.Warning("[Horn] second lock chirp: the vehicle is gone."); yield break; }
            PlayChirp(lv);
        }

        private static bool PlayChirp(LandVehicle lv)
        {
            var horn = ChassisApi.For(lv)?.Horn;
            try
            {
                var clip = Sounds.Get(horn?.Chirp, Sounds.Chirp);
                var source = Sounds.On(lv, "ChassisHorn", horn != null ? horn.Position : new Vector3(0f, 0.6f, 1.8f));
                source.PlayOneShot(clip, 0.7f * Sounds.Volume());
                return true;
            }
            catch (Exception e) { Core.Log.Error("[Horn] could not play the lock chirp of " + Registry.Name(lv) + ": " + e.Message); return false; }
        }

        /// <summary>Host: flash the headlights twice and leave them as they were.</summary>
        internal static void Flash(LandVehicle lv)
        {
            if (lv == null || lv.lights == null) { Core.Log.Warning("[Horn] " + Registry.Name(lv) + " has no lights to flash."); return; }
            MelonCoroutines.Start(FlashRoutine(lv));
        }

        private static IEnumerator FlashRoutine(LandVehicle lv)
        {
            VehicleLights lights = lv.lights;
            bool was = lights.HeadlightsOn;
            for (int i = 0; i < Flashes * 2; i++)
            {
                if (lv == null || lights == null) { Core.Log.Warning("[Horn] the flashing vehicle is gone."); yield break; }
                bool failed = false;
                try { lights.HeadlightsOn = (i % 2 == 0) != was; }
                catch (Exception e) { Core.Log.Error("[Horn] flashing the lights failed: " + e.Message); failed = true; }
                if (failed) yield break;
                yield return new WaitForSeconds(FlashStep);
            }
            if (lv == null || lights == null) yield break;
            lights.HeadlightsOn = was;
            Core.Log.Msg("[Horn] flashed the headlights of " + Registry.Name(lv) + ".");
        }
    }
}
