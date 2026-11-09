using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine.InputSystem;

namespace Chassis.Vehicles
{
    /// <summary>
    /// The driver's keys, set per machine in MelonPreferences under [Chassis]: a keyboard key and a controller button
    /// for each action. The key is a Unity key name (<c>LeftArrow</c>, <c>V</c>, <c>JoystickButton4</c>); the button is
    /// an Input System control path (<c>&lt;Gamepad&gt;/dpad/left</c>, <c>&lt;Gamepad&gt;/leftShoulder</c>), empty for none.
    /// Either one triggers the action.
    /// </summary>
    internal static class Bindings
    {
        internal sealed class Binding
        {
            internal readonly string Name;
            private readonly KeyCode _defaultKey;
            private MelonPreferences_Entry<KeyCode> _key;
            private MelonPreferences_Entry<string> _button;
            private InputAction _action;
            private string _actionPath;
            private bool _actionFailed;

            internal Binding(string name, KeyCode key) { Name = name; _defaultKey = key; }

            internal KeyCode Key => _key?.Value ?? _defaultKey;
            internal string Button => _button?.Value?.Trim() ?? "";

            internal void Create(MelonPreferences_Category cat, string label, string what)
            {
                _key = cat.CreateEntry(Name + "Key", _defaultKey, label + " key",
                    "Keyboard key that " + what + ". A Unity key name such as " + _defaultKey + "; JoystickButton0 to JoystickButton19 are controller buttons.");
                _button = cat.CreateEntry(Name + "Button", "", label + " controller button",
                    "Controller button that " + what + ", as an Input System path such as <Gamepad>/select or <Gamepad>/dpad/left. Most pad buttons already do something in the game (Y opens the phone); pick one that is free while driving. Empty: none.");
            }

            /// <summary>True in the frame the key or the button went down.</summary>
            internal bool Down() => (Key != KeyCode.None && Input.GetKeyDown(Key)) || ButtonDown();

            private bool ButtonDown()
            {
                string path = Button;
                if (path.Length == 0) { Drop(); return false; }
                if (_actionFailed && path == _actionPath) return false;
                if (_action == null || path != _actionPath)
                {
                    Drop();
                    _actionPath = path;
                    try
                    {
                        // One map per action: a map with an enabled action takes no further actions.
                        var map = Map(Name + "_" + (_serial++));
                        _action = InputActionSetupExtensions.AddAction(map, Name, InputActionType.Button, path, null, null, null, null);
                        _action.Enable();
                        if (_action.controls.Count == 0)
                            Core.Log.Warning("[Bindings] " + Name + " button '" + path + "' matches no connected controller yet - it works once one with that control is plugged in.");
                        else Core.Log.Msg("[Bindings] " + Name + " on controller button '" + path + "'.");
                    }
                    catch (Exception e)
                    {
                        _actionFailed = true;
                        _action = null;
                        Core.Log.Error("[Bindings] " + Name + " button '" + path + "' is no valid Input System path - the button is ignored: " + e.Message);
                        return false;
                    }
                }
                return _action.WasPressedThisFrame();
            }

            private void Drop()
            {
                if (_action != null) { try { _action.Disable(); } catch (Exception e) { Core.Log.Warning("[Bindings] could not disable the old " + Name + " button: " + e.Message); } }
                _action = null;
                _actionFailed = false;
                _actionPath = null;
            }

#if DEBUG
            internal void SetKey(KeyCode k) { if (_key != null) _key.Value = k; }
            internal void SetButton(string path) { if (_button != null) _button.Value = path; }
#endif

            public override string ToString() => Name + " " + Key + (Button.Length > 0 ? " / " + Button : "");
        }

        internal static readonly Binding IndicatorLeft = new Binding("IndicatorLeft", KeyCode.LeftArrow);
        internal static readonly Binding IndicatorRight = new Binding("IndicatorRight", KeyCode.RightArrow);
        internal static readonly Binding FirstPerson = new Binding("FirstPerson", KeyCode.V);
        internal static readonly Binding Ignition = new Binding("Ignition", KeyCode.I);

        private static int _serial;
        private static bool _created;

        private static InputActionMap Map(string name)
        {
            var asset = KeyHints.Create<InputActionAsset>();
            return InputActionSetupExtensions.AddActionMap(asset, "ChassisBindings_" + name);
        }

        /// <summary>Creates the preference entries; called once at start.</summary>
        internal static void Load()
        {
            if (_created) return;
            _created = true;
            try
            {
                var cat = MelonPreferences.CreateCategory("Chassis");
                IndicatorLeft.Create(cat, "Left indicator", "switches the left indicator on and off");
                IndicatorRight.Create(cat, "Right indicator", "switches the right indicator on and off");
                FirstPerson.Create(cat, "First person", "switches the first-person view in a seat");
                Ignition.Create(cat, "Ignition", "turns the ignition key of the car you sit in");
                Core.Log.Msg("[Bindings] " + IndicatorLeft + ", " + IndicatorRight + ", " + FirstPerson + ", " + Ignition + ".");
            }
            catch (Exception e)
            {
                Core.Log.Error("[Bindings] could not read the key preferences - the default keys stay: " + e);
            }
        }

        internal static string Describe() => "bindings: " + IndicatorLeft + ", " + IndicatorRight + ", " + FirstPerson + ", " + Ignition;

#if DEBUG
        internal static Binding Find(string name)
        {
            foreach (var b in new[] { IndicatorLeft, IndicatorRight, FirstPerson, Ignition })
                if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) return b;
            return null;
        }

        /// <summary>Sets a key or button for this session only; the preferences file keeps its value.</summary>
        internal static string Set(Binding b, string kind, string value)
        {
            if (kind == "key")
            {
                if (!Enum.TryParse(value, true, out KeyCode k)) return "bind: '" + value + "' is no Unity key name";
                b.SetKey(k);
            }
            else if (kind == "button") b.SetButton(value == "none" ? "" : value);
            else return "bind <name> key <KeyCode> | button <path|none>";
            return "bind: " + b;
        }

        private static InputDevice _pad;

        /// <summary>Presses one control of a virtual gamepad for two frames, as a real controller would.</summary>
        internal static string Pad(string control)
        {
            if (_pad == null)
            {
                // A test game runs behind other windows; by default the Input System drops gamepad input then.
                var settings = InputSystem.settings;
                if (settings != null && settings.backgroundBehavior != UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus)
                {
                    Core.Log.Msg("[Bindings] input background behaviour " + settings.backgroundBehavior + " -> IgnoreFocus for the virtual gamepad.");
                    settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
                }
                _pad = InputSystem.AddDevice("Gamepad", "ChassisTestPad", null);
                Core.Log.Msg("[Bindings] virtual gamepad added: " + _pad.path);
            }
            var c = _pad.TryGetChildControl(control);
            if (c == null) return "bind pad: the gamepad has no control '" + control + "'";
#if IL2CPP
            var button = c.TryCast<UnityEngine.InputSystem.Controls.ButtonControl>();
#else
            var button = c as UnityEngine.InputSystem.Controls.ButtonControl;
#endif
            if (button == null) return "bind pad: '" + control + "' is no button";
            Queue(button, 1f);
            MelonCoroutines.Start(Release(button));
            return "bind pad: pressed " + button.path;
        }

        private static System.Collections.IEnumerator Release(UnityEngine.InputSystem.Controls.ButtonControl button)
        {
            yield return null;
            Core.Log.Msg("[Bindings] virtual gamepad " + button.path + " pressed: " + button.isPressed + ".");
            yield return null;
            Queue(button, 0f);
            Core.Log.Msg("[Bindings] virtual gamepad released " + button.path + ".");
        }

        private static void Queue(UnityEngine.InputSystem.Controls.ButtonControl button, float value)
        {
            var buffer = UnityEngine.InputSystem.LowLevel.StateEvent.From(_pad, out var ptr, Unity.Collections.Allocator.Temp);
            try
            {
                button.WriteValueIntoEvent(value, ptr);
                InputSystem.QueueEvent(ptr);
            }
            finally { buffer.Dispose(); }
        }
#endif
    }
}
