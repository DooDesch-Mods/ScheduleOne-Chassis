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
                    "How sharp and how fresh chrome paint reflects the street: 0 Low, 1 Medium, 2 High, 3 Ultra. Also in the game's graphics settings.");
                Core.Log.Msg("[Reflections] " + Names[Level] + " (preference Reflections = " + _entry.Value + ").");
            }
            catch (Exception e) { Core.Log.Warning("[Reflections] reading the Reflections preference failed - Medium is used: " + e.Message); }
        }

        internal static void Set(int level)
        {
            level = Mathf.Clamp(level, 0, Names.Length - 1);
            if (_entry == null) { Core.Log.Warning("[Reflections] no preference entry - " + Names[level] + " is not kept."); return; }
            if (_entry.Value == level) return;
            _entry.Value = level;
            try { MelonPreferences.Save(); }
            catch (Exception e) { Core.Log.Warning("[Reflections] saving the preference failed - " + Names[level] + " holds until the game closes: " + e.Message); }
            Shine.Restyle();
            Core.Log.Msg("[Reflections] set to " + Names[level] + ": probes " + Size + " px, " + TimeSlicing + ", standing every " + StandingEvery + " s.");
        }

        // ---- the settings row --------------------------------------------------------------------------------------

        /// <summary>
        /// The graphics tab starts its "Quality" row: a copy of the row goes under it. The copy is made under an inactive
        /// holder and loses its <c>QualityDropdown</c> before it ever wakes, and its controls get fresh change events, so
        /// it can never change the game's quality. The row's control is a TMP dropdown with the mouse and a popup selector
        /// in other layouts; whichever the row has is filled.
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
            // QualityDropdown sits inside the row (label and control); the row is the child of the tab's layout group.
            Transform rowT = dropdownOwner.transform;
            while (rowT.parent != null && rowT.parent.GetComponent<UnityEngine.UI.LayoutGroup>() == null) rowT = rowT.parent;
            Transform parent = rowT.parent;
            if (parent == null) { Core.Log.Warning("[Reflections] no layout group above the Quality dropdown - no Reflections row."); return; }
            if (parent.Find(RowName) != null) { Core.Log.Msg("[Reflections] the settings row is there already."); return; }
            GameObject quality = rowT.gameObject;

            var holder = new GameObject("Chassis_ReflectionsHolder");
            holder.SetActive(false);
            GameObject row;
            try
            {
                row = UnityEngine.Object.Instantiate(quality, holder.transform);
                row.name = RowName;
                int removed = 0;
                foreach (var copy in row.GetComponentsInChildren<GameQualityDropdown>(true)) { UnityEngine.Object.DestroyImmediate(copy); removed++; }
                if (removed == 0) Core.Log.Warning("[Reflections] the copied row had no QualityDropdown to remove.");
                row.transform.SetParent(parent, false);
                row.transform.SetSiblingIndex(quality.transform.GetSiblingIndex() + 1);
            }
            finally { UnityEngine.Object.Destroy(holder); }

            var dropdown = row.GetComponentInChildren<GameDropdown>(true);
            var selector = row.GetComponentInChildren<GamePopupSelector>(true);
            if (dropdown == null && selector == null)
            {
                UnityEngine.Object.Destroy(row);
                Core.Log.Warning("[Reflections] the Quality row has neither a dropdown nor a popup selector - no Reflections row.");
                return;
            }
            if (dropdown != null)
            {
                dropdown.onValueChanged = new GameDropdown.DropdownEvent();
                dropdown.ClearOptions();
                for (int i = 0; i < Names.Length; i++) dropdown.options.Add(new GameDropdown.OptionData(Names[i]));
                dropdown.SetValueWithoutNotify(Level);
                dropdown.RefreshShownValue();
#if IL2CPP
                dropdown.onValueChanged.AddListener((Action<int>)(i => Set(i)));
#else
                dropdown.onValueChanged.AddListener(i => Set(i));
#endif
            }
            if (selector != null)
            {
                selector.OnChanged = new UnityEngine.Events.UnityEvent<GameMenuOption>();
                selector.ClearOptions();
                for (int i = 0; i < Names.Length; i++) selector.AddOption(new GameMenuOption(i, Names[i], null));
                selector.SetCurrentOptionWithoutNotify(Level);
#if IL2CPP
                selector.OnChanged.AddListener((Action<GameMenuOption>)(o => Set(o.optionID)));
#else
                selector.OnChanged.AddListener(o => Set(o.optionID));
#endif
            }

            int labels = 0;
            foreach (var t in row.GetComponentsInChildren<GameUiText>(true))
                if (t != null && string.Equals(t.text, "Quality", StringComparison.OrdinalIgnoreCase)) { t.text = "Reflections"; labels++; }
            if (labels == 0) Core.Log.Warning("[Reflections] no 'Quality' label in the copied row - it keeps the copied name.");
            Core.Log.Msg("[Reflections] settings row added under Quality (" + (dropdown != null ? "dropdown" : "") + (dropdown != null && selector != null ? " and " : "") + (selector != null ? "popup selector" : "") + "), showing " + Names[Level] + ".");
        }
    }
}
