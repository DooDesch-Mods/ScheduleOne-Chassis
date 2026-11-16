using System;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace Chassis.Vehicles
{
    /// <summary>
    /// Tinted windows lower the chance that an officer starts a body search (in a car: a traffic stop) on a seated
    /// player. Vanilla rolls that chance once a second per officer in <c>PoliceOfficer.CheckNewInvestigation</c>, for
    /// every player within 8 m who is at least 20 % visible:
    /// <c>Clamp01(BodySearchChance * Lerp(0.2, 2, suspiciousness) * Lerp(0.4, 1, visibility) * 0.75)</c>.
    /// The tint factor multiplies that chance. Glass as dark as the profile's stock glass keeps the full chance, the
    /// Limo tint of Marco's Bodyshop halves it, and tints in between lower it in proportion to their alpha.
    /// Only the host rolls for real (the investigation it starts only progresses there); while no player within reach
    /// sits in a tinted car, vanilla's own method runs unchanged.
    /// </summary>
    [HarmonyPatch]
    internal static class TintStop
    {
        internal const float MaxReduction = 0.5f;               // share of the chance the darkest tint takes away
        internal const float StockAlpha = 0.55f;                // alpha of the profile's stock glass (ChassisProfile.GlassSpec.Tint)
        internal const float DarkestAlpha = 0xF8 / 255f;        // alpha of the Limo tint, the darkest Marco's Bodyshop sells
        private const float MaxDistance = 8f;                   // PoliceOfficer.INVESTIGATION_MAX_DISTANCE
        private const float MinVisibility = 0.2f;               // PoliceOfficer.INVESTIGATION_MIN_VISIBILITY

        /// <summary>
        /// The alpha of the window tint fitted to this car, or -1 when it has none. The fitted value, not the shown one:
        /// a tint previewed at the shop does not count yet.
        /// </summary>
        internal static float TintAlpha(LandVehicle lv)
        {
            if (lv == null) return -1f;
            string t = Chassis.Tune.State.Get(lv, "tint");
            return !string.IsNullOrEmpty(t) && Chassis.Tune.Values.Rgba(t, out Color c) ? c.a : -1f;
        }

        /// <summary>Multiplier on the stop chance for a tint alpha: 1 for none or stock glass, 0.5 at the Limo tint.</summary>
        internal static float FactorForAlpha(float alpha)
        {
            if (alpha < 0f) return 1f;
            float darkness = Mathf.Clamp01((alpha - StockAlpha) / (DarkestAlpha - StockAlpha));
            return 1f - MaxReduction * darkness;
        }

        internal static float Factor(LandVehicle lv) => FactorForAlpha(TintAlpha(lv));

        /// <summary>The car the player sits in, or null on foot.</summary>
        internal static LandVehicle CarOf(Player p) => p != null && p.CurrentVehicle != null ? p.CurrentVehicle.GetComponent<LandVehicle>() : null;

        /// <summary>The chance after vanilla's multipliers and the tint factor; one Random roll against it.</summary>
        internal static bool Roll(float chance, float factor, out float final, out float roll)
        {
            final = Mathf.Clamp01(chance * factor);
            roll = UnityEngine.Random.Range(0f, 1f);
            return roll < final;
        }

        [HarmonyPatch(typeof(GamePoliceOfficer), nameof(GamePoliceOfficer.CheckNewInvestigation))]
        [HarmonyPrefix]
        private static bool CheckNewInvestigation(GamePoliceOfficer __instance)
        {
            try
            {
                Calls++;
                if (!InstanceFinder.IsServer || !TrafficStop.Enabled) return true;
                if (!AnyTintedInReach(__instance)) return true;
                TakenOver++;
                Investigate(__instance);
                return false;
            }
            catch (Exception e)
            {
                Core.Log.Error("[Stop] the tinted-window roll failed - vanilla rolls this second: " + e);
                return true;
            }
        }

        private static bool AnyTintedInReach(GamePoliceOfficer o)
        {
            if (o.currentBodySearchInvestigation != null || o.BodySearchChance <= 0f) return false;
            var list = Player.PlayerList;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == null || p.Avatar == null) continue;
                if (Factor(CarOf(p)) >= 1f) continue;
                if (Vector3.Distance(o.Avatar.CenterPoint, p.Avatar.CenterPoint) <= MaxDistance) return true;
            }
            return false;
        }

        /// <summary>Vanilla <c>CheckNewInvestigation</c> with the tint factor on each player's chance.</summary>
        private static void Investigate(GamePoliceOfficer o)
        {
            if (o.currentBodySearchInvestigation != null || !o.CanInvestigate() || o.BodySearchChance <= 0f) return;
            var list = Player.PlayerList;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (!o.CanInvestigatePlayer(p) || Vector3.Distance(o.Avatar.CenterPoint, p.Avatar.CenterPoint) > MaxDistance) continue;
                float visibility = o.Awareness.VisionCone.GetPlayerVisibility(p);
                if (visibility < MinVisibility) continue;
                float suspicion = Mathf.Lerp(0.2f, 2f, p.VisualState.Suspiciousness);
                float seen = Mathf.Lerp(0.4f, 1f, visibility);
                float chance = Mathf.Clamp01(o.BodySearchChance * suspicion * seen * 0.75f);
                var lv = CarOf(p);
                float factor = Factor(lv);
                bool hit = Roll(chance, factor, out float final, out float roll);
#if DEBUG
                Core.Log.Msg("[Stop] roll: officer " + o.name + " on " + p.PlayerName + (lv != null ? " in " + Registry.Name(lv) : " on foot")
                    + " - tint factor " + F(factor) + ", chance " + F(chance) + " -> " + F(final) + ", roll " + F(roll) + (hit ? " - search starts." : "."));
#endif
                if (hit)
                {
                    o.VoiceOverEmitter.Play(EVOLineType.Think);
                    o.currentBodySearchInvestigation = new GameInvestigation(p);
                    Core.Log.Msg("[Stop] officer " + o.name + " considers searching " + p.PlayerName + " (tint factor " + F(factor) + ").");
                    break;
                }
            }
        }

        // How often the prefix ran and how often it rolled with the tint factor; read by the debug console.
        internal static int Calls, TakenOver;

        /// <summary>Why the officer would or would not roll on this player now, guard by guard.</summary>
        internal static string Probe(GamePoliceOfficer o, Player p)
        {
            if (o == null || p == null) return "no officer or player";
            var sb = new System.Text.StringBuilder();
            sb.Append("officer " + o.name + " chance " + F(o.BodySearchChance));
            sb.Append(" pending " + (o.currentBodySearchInvestigation != null));
            sb.Append(" canInvestigate " + o.CanInvestigate());
            sb.Append(" canInvestigatePlayer " + o.CanInvestigatePlayer(p));
            sb.Append(" sinceLastSearch " + F(p.CrimeData.TimeSinceLastBodySearch));
            sb.Append(" distance " + F(Vector3.Distance(o.Avatar.CenterPoint, p.Avatar.CenterPoint)));
            sb.Append(" visibility " + F(o.Awareness.VisionCone.GetPlayerVisibility(p)));
            sb.Append(" factor " + F(Factor(CarOf(p))));
            sb.Append(" calls " + Calls + " takenOver " + TakenOver);
            return sb.ToString();
        }

        private static string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);
    }
}
