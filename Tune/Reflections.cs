using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using UnityEngine.Rendering;

namespace Chassis.Tune
{
    /// <summary>
    /// How sharp and how fresh the chrome mirror is (<see cref="Shine"/>): a "Reflections" row in the game's graphics
    /// settings, right under "Quality" and built from a copy of it, with the steps Off, Low, Medium, High and Ultra. The
    /// value lives in MelonPreferences (<c>[Chassis] Reflections</c>), Medium is the look Chassis 1.7 had.
    /// Off is stored as -1, so the values 0 to 3 of older versions keep their step. Off renders no chrome probe at all;
    /// chrome paint then shows the game's own sky light. The car mirrors keep their own row and render at Low.
    /// </summary>
    internal static class Reflections
    {
        /// <summary>Quality steps, indexed by <see cref="Level"/>.</summary>
        internal static readonly string[] Names = { "Low", "Medium", "High", "Ultra" };
        /// <summary>The settings row: Off first, then <see cref="Names"/>; row index = stored value + 1.</summary>
        private static readonly string[] RowNames = { "Off", "Low", "Medium", "High", "Ultra" };
        private const int DefaultLevel = 1, OffValue = -1;
        private const string RowName = "Chassis_Reflections";

        /// <summary>Probe size in pixels, per level.</summary>
        private static readonly int[] Sizes = { 64, 128, 256, 512 };
        /// <summary>Seconds between two renders of standing cars' probes, shared by all of them, per level.</summary>
        private static readonly float[] Standing = { 4f, 2f, 1f, 0.5f };
        /// <summary>Seconds between two renders of moving cars' probes, shared by all of them, per level; 0 starts the next as soon as one is done.</summary>
        private static readonly float[] Moving = { 0.25f, 0f, 0f, 0f };
        /// <summary>
        /// Low and Medium render one cube face per frame, High all six spread over frames, Ultra all six in one frame:
        /// the mirror never lags behind the street, at the highest cost.
        /// </summary>
        private static readonly ReflectionProbeTimeSlicingMode[] Slicing =
        {
            ReflectionProbeTimeSlicingMode.IndividualFaces, ReflectionProbeTimeSlicingMode.IndividualFaces,
            ReflectionProbeTimeSlicingMode.AllFacesAtOnce, ReflectionProbeTimeSlicingMode.NoTimeSlicing,
        };

        private static MelonPreferences_Entry<int> _entry;

        /// <summary>The quality step 0 Low to 3 Ultra; Off counts as Low for the mirrors.</summary>
        internal static int Level => Mathf.Clamp(_entry?.Value ?? DefaultLevel, 0, Names.Length - 1);
        /// <summary>No chrome probe renders.</summary>
        internal static bool Off => (_entry?.Value ?? DefaultLevel) <= OffValue;
        /// <summary>The step as the settings row names it.</summary>
        internal static string Label => Off ? RowNames[0] : Names[Level];
        internal static int Size => Sizes[Level];
        internal static float StandingEvery => Standing[Level];
        internal static float MovingEvery => Moving[Level];
        internal static ReflectionProbeTimeSlicingMode TimeSlicing => Slicing[Level];

        internal static void LoadSetting()
        {
            try
            {
                var cat = MelonPreferences.CreateCategory("Chassis");
                _entry = cat.CreateEntry("Reflections", DefaultLevel, "Chrome reflections",
                    "How sharp and how fresh chrome paint and the car mirrors reflect the street: -1 Off, 0 Low, 1 Medium, 2 High, 3 Ultra. Off renders no chrome reflections, the mirrors render at Low. Also in the game's graphics settings.");
                if (_entry.Value < OffValue || _entry.Value >= Names.Length)
                    Core.Log.Warning("[Reflections] preference Reflections = " + _entry.Value + " is outside -1 to 3 - " + Label + " is used.");
                Core.Log.Msg("[Reflections] " + Label + " (preference Reflections = " + _entry.Value + ").");
            }
            catch (Exception e) { Core.Log.Warning("[Reflections] reading the Reflections preference failed - Medium is used: " + e.Message); }
        }

        /// <summary>Sets the step: -1 Off, 0 Low to 3 Ultra.</summary>
        internal static void Set(int level)
        {
            level = Mathf.Clamp(level, OffValue, Names.Length - 1);
            string name = RowNames[level + 1];
            if (_entry == null) { Core.Log.Warning("[Reflections] no preference entry - " + name + " is not kept."); return; }
            if (_entry.Value == level) { Core.Log.Msg("[Reflections] already " + name + "."); return; }
            // Set, do not save: MelonPreferences.Save() fires the preference listeners of every installed mod from this
            // callback; MelonLoader writes the value when the game closes.
            _entry.Value = level;
            Shine.Restyle();
            Interior.Mirrors.Restyle();
            if (Off) Core.Log.Msg("[Reflections] set to Off: no chrome probe renders, mirrors at Low.");
            else Core.Log.Msg("[Reflections] set to " + name + ": probes " + Size + " px, " + TimeSlicing + ", standing every " + StandingEvery + " s.");
        }

        // ---- the settings row --------------------------------------------------------------------------------------

        /// <summary>
        /// The graphics tab starts its "Quality" row: copies of it (<see cref="SettingsRow"/>) go under it, "Reflections"
        /// and below that "Mirrors" (<see cref="MirrorMode"/>).
        /// </summary>
        [HarmonyPatch(typeof(GameQualityDropdown), "Start")]
        private static class QualityRowPatch
        {
            private static bool _failed;

            private static void Postfix(GameQualityDropdown __instance)
            {
                if (_failed || __instance == null) return;
                try { MelonCoroutines.Start(AddRow(__instance.gameObject)); }
                catch (Exception e) { _failed = true; Core.Log.Error("[Reflections] adding the settings row failed - the level stays in MelonPreferences only: " + e); }
            }
        }

        private static IEnumerator AddRow(GameObject quality)
        {
            // One frame later: the row's own Start is still running when the postfix fires.
            yield return null;
            if (quality == null) { Core.Log.Warning("[Reflections] the Quality row is gone before the copy was made."); yield break; }
            try { Build(quality); }
            catch (Exception e) { Core.Log.Error("[Reflections] adding the settings row failed - the level stays in MelonPreferences only: " + e); }
        }

        private static void Build(GameObject dropdownOwner)
        {
            Transform quality = SettingsRow.RowOf(dropdownOwner);
            if (quality == null) { Core.Log.Warning("[Reflections] no layout group above the Quality dropdown - no Reflections or Mirrors row."); return; }
            var reflections = SettingsRow.Add(quality, quality, RowName, "Reflections", RowNames, Off ? 0 : Level + 1, i => Set(i - 1), "[Reflections]");
            if (reflections == null) return;
            SettingsRow.Add(quality, reflections, MirrorMode.RowName, "Mirrors", MirrorMode.Names, MirrorMode.Mode, MirrorMode.Set, "[Mirror]");
        }
    }
}
