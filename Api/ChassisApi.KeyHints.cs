// Copyright (c) 2026 DooDesch. DooDesch API License (see LICENSE.md): you may copy this file unchanged.
using System;
using Chassis.Vehicles;

namespace Chassis.Api
{
    /// <summary>Where the local player is while the key hint strip asks an entry whether it shows.</summary>
    public sealed class KeyHintContext
    {
        /// <summary>The vehicle the local player sits in or pushes.</summary>
        public LandVehicle Vehicle { get; internal set; }
        /// <summary>The vehicle's Chassis profile, or null when Chassis leaves the vehicle to vanilla.</summary>
        public ChassisProfile Profile { get; internal set; }
        /// <summary>Vanilla seat index of the local player; -1 while pushing.</summary>
        public int Seat { get; internal set; }
        /// <summary>The local player drives.</summary>
        public bool Driver { get; internal set; }
        /// <summary>The local player pushes the vehicle from behind (<see cref="ChassisApi.PoseAsPusher"/>) instead of sitting in it.</summary>
        public bool Pushing { get; internal set; }
    }

    public static partial class ChassisApi
    {
        // ---- key hints ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Adds a line to the key hints at the bottom left while the local player sits in or pushes a vehicle, below
        /// First Person and Horn and above Indicators. <paramref name="show"/> runs every
        /// frame and picks the vehicles and seats the key works in; null shows it in every vehicle. The same id again
        /// replaces the entry. Letters follow the keyboard layout, so <c>KeyCode.Y</c> shows the key labelled Y.
        /// </summary>
        /// <param name="id">Your own unique id, e.g. "mymod.siren".</param>
        /// <param name="label">The text next to the key, e.g. "Siren".</param>
        /// <param name="show">Whether the line shows for the vehicle and seat in <see cref="KeyHintContext"/>; null shows it always.</param>
        /// <param name="keys">One key or several shown side by side, e.g. <c>KeyCode.LeftArrow, KeyCode.RightArrow</c>.</param>
        /// <example>
        /// A siren key only for the driver of the mod's own vehicle:
        /// <code>
        /// ChassisApi.AddKeyHint("mymod.siren", "Siren", c => c.Driver &amp;&amp; ChassisApi.CodeOf(c.Vehicle) == "mymod_cruiser", KeyCode.H);
        /// </code>
        /// </example>
        public static void AddKeyHint(string id, string label, Func<KeyHintContext, bool> show, params KeyCode[] keys)
        {
            if (keys == null || keys.Length == 0) throw new ArgumentException("a key hint needs at least one key");
            var copy = (KeyCode[])keys.Clone();
            KeyHints.Add(id, label, show, () => copy);
        }

        /// <summary>
        /// Adds a key hint whose key can change while the game runs, for a key from a mod's preferences. The key is
        /// read every frame and the hint follows it.
        /// </summary>
        /// <param name="id">Your own unique id, e.g. "mymod.siren".</param>
        /// <param name="label">The text next to the key, e.g. "Siren".</param>
        /// <param name="show">Whether the line shows for the vehicle and seat in <see cref="KeyHintContext"/>; null shows it always.</param>
        /// <param name="key">Read every frame, e.g. <c>() =&gt; myKeyEntry.Value</c> for a MelonPreferences entry.</param>
        public static void AddKeyHint(string id, string label, Func<KeyHintContext, bool> show, Func<KeyCode> key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            KeyHints.Add(id, label, show, () => new[] { key() });
        }

        /// <summary>Removes a key hint a mod added; false when there was none with that id.</summary>
        public static bool RemoveKeyHint(string id) => KeyHints.Remove(id);
    }
}
