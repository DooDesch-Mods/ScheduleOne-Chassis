using System;
using MelonLoader;

namespace Chassis.Tune
{
    /// <summary>
    /// Which live mirrors render (<see cref="Interior.Mirrors"/>): none, the rear-view mirror only, rear and side mirrors of
    /// the car the player sits in, or those plus the mirrors of up to three cars nearby, also on foot. A "Mirrors" row in the
    /// game's graphics settings under "Reflections"; the value lives in MelonPreferences (<c>[Chassis] Mirrors</c>).
    /// <see cref="Reflections"/> stays the quality of the mirrors that render.
    /// </summary>
    internal static class MirrorMode
    {
        internal static readonly string[] Names = { "Off", "Rear Only", "Rear + Side", "+ Nearby Cars" };
        internal const int Off = 0, RearOnly = 1, RearSide = 2, Nearby = 3;
        private const int DefaultMode = RearSide;
        internal const string RowName = "Chassis_Mirrors";

        private static MelonPreferences_Entry<int> _entry;

        internal static int Mode => Mathf.Clamp(_entry?.Value ?? DefaultMode, 0, Names.Length - 1);

        internal static void LoadSetting()
        {
            try
            {
                var cat = MelonPreferences.CreateCategory("Chassis");
                _entry = cat.CreateEntry("Mirrors", DefaultMode, "Mirrors",
                    "Which live car mirrors render: 0 Off, 1 Rear Only, 2 Rear + Side (own car), 3 + Nearby Cars (also the mirrors of up to three cars nearby, on foot too). Also in the game's graphics settings.");
                Core.Log.Msg("[Mirror] mode " + Names[Mode] + " (preference Mirrors = " + _entry.Value + ").");
            }
            catch (Exception e) { Core.Log.Warning("[Mirror] reading the Mirrors preference failed - Rear + Side is used: " + e.Message); }
        }

        internal static void Set(int mode)
        {
            mode = Mathf.Clamp(mode, 0, Names.Length - 1);
            if (_entry == null) { Core.Log.Warning("[Mirror] no preference entry - mode " + Names[mode] + " is not kept."); return; }
            if (_entry.Value == mode) { Core.Log.Msg("[Mirror] mode is " + Names[mode] + " already."); return; }
            // Set, do not save: MelonPreferences.Save() fires the preference listeners of every installed mod from this
            // callback; MelonLoader writes the value when the game closes.
            _entry.Value = mode;
            Interior.Mirrors.ModeChanged();
            Core.Log.Msg("[Mirror] mode set to " + Names[mode] + ".");
        }
    }
}
