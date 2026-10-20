using System;
using System.Collections.Generic;
using System.Text;
using Chassis.Api;
using Chassis.Net;
using Chassis.Rideables;
using Chassis.Seating;
using UnityEngine.InputSystem;
#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.UI.Input;
using DescriptorList = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.UI.Input.InputPromptsDescriptorData>;
using ActionRefList = Il2CppSystem.Collections.Generic.List<UnityEngine.InputSystem.InputActionReference>;
#else
using ScheduleOne.UI.Input;
using DescriptorList = System.Collections.Generic.List<ScheduleOne.UI.Input.InputPromptsDescriptorData>;
using ActionRefList = System.Collections.Generic.List<UnityEngine.InputSystem.InputActionReference>;
#endif

namespace Chassis.Vehicles
{
    /// <summary>
    /// Adds the keys of the current vehicle to the key hints at the bottom left, next to the game's own Headlights /
    /// Handbrake / Exit Vehicle. The list follows the vehicle and the seat: First Person in a seat with a Chassis
    /// profile, Horn and (with indicator lamps in the profile) Indicators for the driver, Push and Let Go while pushing
    /// a wheelchair, then whatever mods add through <see cref="ChassisApi.AddKeyHint(string, string, Func{KeyHintContext, bool}, KeyCode[])"/>.
    ///
    /// The hint strip only draws descriptors that point at an input action. The Chassis keys are plain keys, so each
    /// gets an action of its own bound to the key; the strip reads only its binding path to pick the key picture.
    /// Push and Let Go use the game's own actions, so they follow the player's bindings.
    /// </summary>
    internal static class KeyHints
    {
        private const string ModuleId = "chassis_vehicle_keys";

        private sealed class Entry
        {
            internal string Id, Label;
            internal Func<KeyHintContext, bool> Show;
            internal Func<KeyCode[]> Keys;
            /// <summary>Names of the game's own input actions (Controls asset), for keys the player can rebind.</summary>
            internal string[] Buttons;
        }

        private static readonly List<Entry> _builtIn = new List<Entry>
        {
            new Entry { Id = "chassis.firstperson", Label = "First Person", Keys = () => new[] { FirstPersonView.Key },
                Show = c => !c.Pushing && Occupants.Of(Player.Local) != null },
            new Entry { Id = "chassis.horn", Label = "Horn", Keys = () => new[] { Horn.Key }, Show = c => c.Driver },
            new Entry { Id = "chassis.indicators", Label = "Indicators", Keys = () => new[] { Indicators.LeftKey, Indicators.RightKey },
                Show = c => c.Driver && c.Profile?.Lamps != null },
            new Entry { Id = "chassis.push", Label = "Push", Show = c => c.Pushing, Buttons = new[] { "Motion" } },
            new Entry { Id = "chassis.letgo", Label = "Let Go", Show = c => c.Pushing, Buttons = new[] { "Interact" } },
        };

        private static readonly List<Entry> _mods = new List<Entry>();
        private static readonly HashSet<string> _failed = new HashSet<string>();
        private static readonly KeyHintContext _context = new KeyHintContext();
        private static readonly List<Entry> _shown = new List<Entry>();
        private static readonly List<KeyCode[]> _shownKeys = new List<KeyCode[]>();
        private static readonly StringBuilder _sig = new StringBuilder();
        private static readonly Dictionary<string, InputPromptsData> _cache = new Dictionary<string, InputPromptsData>();
        /// <summary>Per signature and drawn line: the key picture labels to write over the game's, null to keep its own.</summary>
        private static readonly Dictionary<string, List<string[]>> _labels = new Dictionary<string, List<string[]>>();

        private static string _shownSig;
        private static bool _loaded;   // the strip took the module, so a missing module later was removed by the game

        internal static void Add(string id, string label, Func<KeyHintContext, bool> show, Func<KeyCode[]> keys)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("a key hint needs an id");
            if (string.IsNullOrEmpty(label)) throw new ArgumentException("a key hint needs a label");
            _mods.RemoveAll(e => e.Id == id);
            _mods.Add(new Entry { Id = id, Label = label, Show = show, Keys = keys });
            _failed.Remove(id);
            Core.Log.Msg("[KeyHints] a mod added '" + label + "' (" + id + ").");
        }

        internal static bool Remove(string id)
        {
            bool removed = _mods.RemoveAll(e => e.Id == id) > 0;
            if (removed) Core.Log.Msg("[KeyHints] a mod removed " + id + ".");
            return removed;
        }

        /// <summary>Builds the panel again, for label changes from the console.</summary>
        internal static void Rebuild()
        {
            _cache.Clear();
            _labels.Clear();
            _shownSig = null;
        }

        internal static void Reset()
        {
            _shownSig = null;
            _loaded = false;
        }

        internal static void Tick()
        {
            string want = Collect() ? Signature() : null;
            var prompts = Singleton<InputPromptsManager>.InstanceExists ? Singleton<InputPromptsManager>.Instance : null;
            // Vanilla screens (character creator, menus) can clear the hint modules behind our back; put it back.
            if (want != null && want == _shownSig && prompts != null && _loaded && Time.frameCount % 30 == 0 && !prompts.HasActivePrompt(ModuleId))
            {
                Core.Log.Msg("[KeyHints] the game removed the vehicle key hints - showing them again.");
                _shownSig = null;
            }
            // A change of input device redraws every panel with the game's labels again.
            else if (want != null && want == _shownSig && _loaded && Time.frameCount % 30 == 0) ShortLabels(prompts, want, false);
            if (want == _shownSig) return;
            _shownSig = want;
            _loaded = false;

            if (prompts == null) { Core.Log.Warning("[KeyHints] no InputPromptsManager - the vehicle keys are not shown."); return; }
            if (prompts.HasActivePrompt(ModuleId)) prompts.UnloadModule(ModuleId);
            if (want == null) return;
            if (want.Length == 0) { Core.Log.Msg("[KeyHints] this seat offers no Chassis keys."); return; }

            var data = Data(want);
            if (data == null) return;
            prompts.LoadModule(data, EInputPromptPosition.BottomLeftInGame, null);
            _loaded = prompts.HasActivePrompt(ModuleId);
            if (_loaded) { Core.Log.Msg("[KeyHints] shown in the key hints: " + want + "."); ShortLabels(prompts, want, true); }
            else Core.Log.Warning("[KeyHints] the key hints refused the vehicle keys (" + want + ").");
        }

        /// <summary>Fills the context and the entries to show; false when the local player is in no vehicle.</summary>
        private static bool Collect()
        {
            _shown.Clear();
            _shownKeys.Clear();
            var lp = Player.Local;
            if (lp == null) return false;
            LandVehicle lv = null;
            // A seat wins over pushing: the hints follow the vehicle the player sits in.
            bool pushing = lp.CurrentVehicle == null && Push.LocalPushing;
            if (pushing) lv = Ids.Find<LandVehicle>(Push.LocalVehicleId);
            else if (lp.CurrentVehicle != null) lv = lp.CurrentVehicle.GetComponent<LandVehicle>();
            if (lv == null) return false;

            _context.Vehicle = lv;
            _context.Profile = ChassisApi.For(lv);
            _context.Pushing = pushing;
            _context.Driver = !pushing && lv.LocalPlayerIsDriver;
            _context.Seat = pushing ? -1 : SeatOf(lv, lp);

            foreach (var e in _builtIn) Consider(e);
            foreach (var e in _mods) Consider(e);
            return true;
        }

        private static void Consider(Entry e)
        {
            if (_failed.Contains(e.Id)) return;
            try
            {
                if (e.Show != null && !e.Show(_context)) return;
                KeyCode[] keys = e.Keys != null ? e.Keys() : null;
                if (e.Buttons == null && (keys == null || keys.Length == 0)) return;
                _shown.Add(e);
                _shownKeys.Add(keys);
            }
            catch (Exception ex)
            {
                _failed.Add(e.Id);
                Core.Log.Error("[KeyHints] the key hint '" + e.Label + "' (" + e.Id + ") failed and is hidden until it is added again: " + ex);
            }
        }

        private static int SeatOf(LandVehicle lv, Player p)
        {
            var seats = lv.Seats;
            if (seats == null) return -1;
            for (int i = 0; i < seats.Length; i++) if (seats[i] != null && seats[i].Occupant == p) return i;
            return -1;
        }

        private static string Signature()
        {
            _sig.Clear();
            for (int i = 0; i < _shown.Count; i++)
            {
                if (i > 0) _sig.Append(", ");
                var e = _shown[i];
                if (e.Buttons != null) foreach (var b in e.Buttons) _sig.Append(b).Append(' ');
                else foreach (var k in _shownKeys[i]) _sig.Append(k).Append(' ');
                _sig.Append(e.Label);
            }
            return _sig.ToString();
        }

        private static InputPromptsData Data(string sig)
        {
            if (_cache.TryGetValue(sig, out var cached) && cached != null) return cached;

            // InputActionReference.Create only takes an action that belongs to an asset.
            var asset = Create<InputActionAsset>();
            InputActionMap map = InputActionSetupExtensions.AddActionMap(asset, "Chassis");

            var data = Create<InputPromptsData>();
            data.Id = ModuleId;
            data.Position = EInputPromptPosition.BottomLeftInGame;
            data.Descriptors = new DescriptorList();
            var labels = new List<string[]>();
            for (int i = 0; i < _shown.Count; i++)
            {
                var e = _shown[i];
                var actions = new ActionRefList();
                if (e.Buttons != null) GameActions(e, actions);
                else KeyActions(e, _shownKeys[i], map, i, actions);
                if (actions.Count == 0) { Core.Log.Warning("[KeyHints] '" + e.Label + "' has no key the hints can draw - not shown."); continue; }
                var descriptor = Create<InputPromptsDescriptorData>();
                descriptor.DisplayName = e.Label;
                descriptor.DisplayColor = Color.white;
                descriptor.Actions = actions;
                data.Descriptors.Add(descriptor);
                labels.Add(e.Buttons == null && actions.Count == _shownKeys[i].Length ? ShortNames(_shownKeys[i]) : null);
            }
            if (data.Descriptors.Count == 0) { Core.Log.Warning("[KeyHints] none of the vehicle keys can be drawn (" + sig + ")."); return null; }
            _cache[sig] = data;
            _labels[sig] = labels;
            return data;
        }

        private static void KeyActions(Entry e, KeyCode[] keys, InputActionMap map, int index, ActionRefList into)
        {
            for (int k = 0; k < keys.Length; k++)
            {
                string path = ControlPath(keys[k]);
                if (path == null) { Core.Log.Warning("[KeyHints] no keyboard path for " + keys[k] + " - left out of '" + e.Label + "'."); continue; }
                InputAction action = InputActionSetupExtensions.AddAction(map, "k" + index + "_" + k, InputActionType.Button, path, null, null, null, null);
                into.Add(InputActionReference.Create(action));
            }
        }

        private static void GameActions(Entry e, ActionRefList into)
        {
            if (!Singleton<GameInput>.InstanceExists) { Core.Log.Warning("[KeyHints] no GameInput - '" + e.Label + "' is not shown."); return; }
            var input = Singleton<GameInput>.Instance;
            foreach (var b in e.Buttons)
            {
                if (!input.TryGetAction(b, out InputAction action) || action == null) { Core.Log.Warning("[KeyHints] the game has no input action " + b + " - left out of '" + e.Label + "'."); continue; }
                into.Add(InputActionReference.Create(action));
            }
        }

        /// <summary>
        /// Short key picture labels for keys whose name the game takes from the operating system: the strip writes
        /// "Nach-Links" (German Windows) or "Left Arrow" into a one-letter key picture. Null where the game's label fits.
        /// </summary>
        /// <summary>Label of an arrow key picture; {0} is the arrow glyph.</summary>
        internal static string ArrowFormat = "<b><size=250%>{0}</size></b>";

        private static string[] ShortNames(KeyCode[] keys)
        {
            string[] r = null;
            for (int k = 0; k < keys.Length; k++)
            {
                string s = keys[k] == KeyCode.LeftArrow ? "←" : keys[k] == KeyCode.RightArrow ? "→" : keys[k] == KeyCode.UpArrow ? "↑" : keys[k] == KeyCode.DownArrow ? "↓" : null;
                if (s == null) continue;
                // The arrow glyphs sit small in the game font; scaled up they match the letters of the other keys.
                s = string.Format(ArrowFormat, s);
                if (r == null) r = new string[keys.Length];
                r[k] = s;
            }
            return r;
        }

        /// <summary>Writes the short labels over the key pictures of the shown panel; the game's fields are private.</summary>
        private static void ShortLabels(InputPromptsManager prompts, string sig, bool log)
        {
            if (!_labels.TryGetValue(sig, out var labels)) return;
            bool any = false;
            foreach (var l in labels) if (l != null) { any = true; break; }
            if (!any) return;
            try
            {
                var ui = prompts._inputPromptsUI;
                if (ui == null || !ui._activePrompts.ContainsKey(ModuleId)) { if (log) Core.Log.Warning("[KeyHints] no panel " + ModuleId + " in the hint strip - the arrow keys keep the game's labels."); return; }
                var items = ui._activePrompts[ModuleId]._activePrompts;
                int fixedCount = 0;
                for (int i = 0; i < items.Count && i < labels.Count; i++)
                {
                    if (labels[i] == null) continue;
                    var images = items[i]._promptImages;
                    for (int k = 0; k < labels[i].Length && k < images.Count; k++)
                    {
                        if (labels[i][k] == null || images[k].Label == null || images[k].Label.text == labels[i][k]) continue;
                        images[k].Label.text = labels[i][k];
                        images[k].Label.gameObject.SetActive(true);
                        fixedCount++;
                    }
                }
                if (log || fixedCount > 0) Core.Log.Msg("[KeyHints] " + fixedCount + " key picture label(s) shortened.");
            }
            catch (Exception ex) { Core.Log.Error("[KeyHints] could not shorten the arrow key labels, the game's stay: " + ex.Message); }
        }

        private static T Create<T>() where T : ScriptableObject
        {
#if IL2CPP
            var o = ScriptableObject.CreateInstance(Il2CppType.Of<T>()).Cast<T>();
#else
            var o = ScriptableObject.CreateInstance<T>();
#endif
            o.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return o;
        }

        /// <summary>
        /// Input System path for a legacy key code. Input System paths name the physical key of a US layout, while a
        /// legacy letter key follows the keyboard layout, so a letter is looked up on the current layout: on QWERTZ
        /// the key labelled Y is "&lt;Keyboard&gt;/z".
        /// </summary>
        internal static string ControlPath(KeyCode key)
        {
            if (key >= KeyCode.A && key <= KeyCode.Z)
            {
                string letter = key.ToString().ToLowerInvariant();
                var keyboard = Keyboard.current;
                var control = keyboard != null ? keyboard.FindKeyOnCurrentKeyboardLayout(letter) : null;
                if (control != null) return "<Keyboard>/" + control.name;
                Core.Log.Warning("[KeyHints] no key on the current keyboard layout types '" + letter + "' - the hint uses the US position.");
                return "<Keyboard>/" + letter;
            }
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return "<Keyboard>/" + (key - KeyCode.Alpha0);
            if (key >= KeyCode.F1 && key <= KeyCode.F12) return "<Keyboard>/" + key.ToString().ToLowerInvariant();
            switch (key)
            {
                case KeyCode.LeftArrow: return "<Keyboard>/leftArrow";
                case KeyCode.RightArrow: return "<Keyboard>/rightArrow";
                case KeyCode.UpArrow: return "<Keyboard>/upArrow";
                case KeyCode.DownArrow: return "<Keyboard>/downArrow";
                case KeyCode.Space: return "<Keyboard>/space";
                case KeyCode.Tab: return "<Keyboard>/tab";
                case KeyCode.Return: return "<Keyboard>/enter";
                case KeyCode.Backspace: return "<Keyboard>/backspace";
                case KeyCode.CapsLock: return "<Keyboard>/capsLock";
                case KeyCode.LeftShift: return "<Keyboard>/leftShift";
                case KeyCode.RightShift: return "<Keyboard>/rightShift";
                case KeyCode.LeftControl: return "<Keyboard>/leftCtrl";
                case KeyCode.RightControl: return "<Keyboard>/rightCtrl";
                case KeyCode.LeftAlt: return "<Keyboard>/leftAlt";
                case KeyCode.RightAlt: return "<Keyboard>/rightAlt";
                case KeyCode.Mouse0: return "<Mouse>/leftButton";
                case KeyCode.Mouse1: return "<Mouse>/rightButton";
                case KeyCode.Mouse2: return "<Mouse>/middleButton";
                default: return null;
            }
        }

        internal static string Describe() => "key hints: " + (_shownSig == null ? "not in a vehicle" : _shownSig.Length == 0 ? "none" : _shownSig) + (_loaded ? "" : " (not shown)") + ", " + _mods.Count + " from mods";
    }
}
