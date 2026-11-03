using System;

namespace Chassis.Tune
{
    /// <summary>
    /// A Chassis row in the game's graphics tab, copied from the "Quality" row. The copy is made under an inactive holder
    /// and loses its <c>QualityDropdown</c> before it ever wakes, and its controls get fresh change events, so it can never
    /// change the game's quality. The row's control is a TMP dropdown with the mouse and a popup selector in other
    /// layouts; whichever the row has is filled.
    /// </summary>
    internal static class SettingsRow
    {
        /// <summary>The row of the tab's layout group that holds <paramref name="control"/>, or null outside one.</summary>
        internal static Transform RowOf(GameObject control)
        {
            // QualityDropdown sits inside the row (label and control); the row is the child of the tab's layout group.
            Transform rowT = control.transform;
            while (rowT.parent != null && rowT.parent.GetComponent<UnityEngine.UI.LayoutGroup>() == null) rowT = rowT.parent;
            return rowT.parent != null ? rowT : null;
        }

        /// <summary>
        /// Adds the row <paramref name="rowName"/> as a copy of <paramref name="template"/> right under
        /// <paramref name="after"/>, labelled <paramref name="label"/>, with <paramref name="names"/> as its steps. Returns
        /// the row, the one there already, or null when the template has no control to fill.
        /// </summary>
        internal static Transform Add(Transform template, Transform after, string rowName, string label, string[] names, int current, Action<int> onSet, string tag)
        {
            Transform parent = template.parent;
            var existing = parent.Find(rowName);
            if (existing != null) { Core.Log.Msg(tag + " the settings row is there already."); return existing; }

            var holder = new GameObject(rowName + "Holder");
            holder.SetActive(false);
            GameObject row;
            try
            {
                row = UnityEngine.Object.Instantiate(template.gameObject, holder.transform);
                row.name = rowName;
                int removed = 0;
                foreach (var copy in row.GetComponentsInChildren<GameQualityDropdown>(true)) { UnityEngine.Object.DestroyImmediate(copy); removed++; }
                if (removed == 0) Core.Log.Warning(tag + " the copied row had no QualityDropdown to remove.");
                row.transform.SetParent(parent, false);
                row.transform.SetSiblingIndex(after.GetSiblingIndex() + 1);
            }
            finally { UnityEngine.Object.Destroy(holder); }

            var dropdown = row.GetComponentInChildren<GameDropdown>(true);
            var selector = row.GetComponentInChildren<GamePopupSelector>(true);
            if (dropdown == null && selector == null)
            {
                UnityEngine.Object.Destroy(row);
                Core.Log.Warning(tag + " the Quality row has neither a dropdown nor a popup selector - no " + label + " row.");
                return null;
            }
            if (dropdown != null)
            {
                dropdown.onValueChanged = new GameDropdown.DropdownEvent();
                dropdown.ClearOptions();
                for (int i = 0; i < names.Length; i++) dropdown.options.Add(new GameDropdown.OptionData(names[i]));
                dropdown.SetValueWithoutNotify(current);
                dropdown.RefreshShownValue();
#if IL2CPP
                dropdown.onValueChanged.AddListener((Action<int>)(i => onSet(i)));
#else
                dropdown.onValueChanged.AddListener(i => onSet(i));
#endif
            }
            if (selector != null)
            {
                selector.OnChanged = new UnityEngine.Events.UnityEvent<GameMenuOption>();
                selector.ClearOptions();
                for (int i = 0; i < names.Length; i++) selector.AddOption(new GameMenuOption(i, names[i], null));
                selector.SetCurrentOptionWithoutNotify(current);
#if IL2CPP
                selector.OnChanged.AddListener((Action<GameMenuOption>)(o => onSet(o.optionID)));
#else
                selector.OnChanged.AddListener(o => onSet(o.optionID));
#endif
            }

            int labels = 0;
            foreach (var t in row.GetComponentsInChildren<GameUiText>(true))
                if (t != null && string.Equals(t.text, "Quality", StringComparison.OrdinalIgnoreCase)) { t.text = label; labels++; }
            if (labels == 0) Core.Log.Warning(tag + " no 'Quality' label in the copied row - it keeps the copied name.");
            Core.Log.Msg(tag + " settings row " + label + " added under " + after.name + " (" + (dropdown != null ? "dropdown" : "") + (dropdown != null && selector != null ? " and " : "") + (selector != null ? "popup selector" : "") + "), showing " + names[current] + ".");
            return row.transform;
        }
    }
}
