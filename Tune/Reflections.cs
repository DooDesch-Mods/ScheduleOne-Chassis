using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using UnityEngine.Rendering;

namespace Chassis.Tune
{
    /// <summary>
    /// How sharp and how fresh the chrome mirror is (<see cref="Shine"/>): a "Reflections" row in the game's graphics
    /// settings, right under "Quality" and built from a copy of it, with the steps Low, Medium, High and Ultra. The value
    /// lives in MelonPreferences (<c>[Chassis] Reflections</c>), Medium is the look Chassis 1.7 had.
    /// </summary>
    internal static class Reflections
    {
        internal static readonly string[] Names = { "Low", "Medium", "High", "Ultra" };
        private const int DefaultLevel = 1;
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

        internal static int Level => Mathf.Clamp(_entry?.Value ?? DefaultLevel, 0, Names.Length - 1);
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
                    "How sharp and how fresh chrome paint and the car mirrors reflect the street: 0 Low, 1 Medium, 2 High, 3 Ultra. Also in the game's graphics settings.");
                Core.Log.Msg("[Reflections] " + Names[Level] + " (preference Reflections = " + _entry.Value + ").");
            }
            catch (Exception e) { Core.Log.Warning("[Reflections] reading the Reflections preference failed - Medium is used: " + e.Message); }
        }

        internal static void Set(int level)
        {
            level = Mathf.Clamp(level, 0, Names.Length - 1);
            if (_entry == null) { Core.Log.Warning("[Reflections] no preference entry - " + Names[level] + " is not kept."); return; }
            if (_entry.Value == level) return;
            // Set, do not save: MelonPreferences.Save() fires the preference listeners of every installed mod from this
            // callback; MelonLoader writes the value when the game closes.
            _entry.Value = level;
            Shine.Restyle();
            Interior.Mirrors.Restyle();
            Core.Log.Msg("[Reflections] set to " + Names[level] + ": probes " + Size + " px, " + TimeSlicing + ", standing every " + StandingEvery + " s.");
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
            var reflections = SettingsRow.Add(quality, quality, RowName, "Reflections", Names, Level, Set, "[Reflections]");
            if (reflections == null) return;
            SettingsRow.Add(quality, reflections, MirrorMode.RowName, "Mirrors", MirrorMode.Names, MirrorMode.Mode, MirrorMode.Set, "[Mirror]");
        }
    }
}
