using System;
using System.Collections.Generic;
using System.Globalization;
using Chassis.Api;
using Chassis.Net;

namespace Chassis.Vehicles
{
    /// <summary>
    /// Turn signals for vehicles whose profile has <see cref="LampProfile"/>. The driver uses the left and right arrow
    /// keys (the same key again switches off; after the turn the signal cancels itself), mods call
    /// <see cref="ChassisApi.SetIndicator"/>. The side goes through the host to every peer; each peer flashes the lamps
    /// on its own clock, switching each lamp between its own materials and amber glowing copies of them.
    /// A profile with empty lamp lists still signals: the mod draws its own lamps from <see cref="ChassisApi.IndicatorLit"/>.
    /// </summary>
    internal static class Indicators
    {
        internal const KeyCode LeftKey = KeyCode.LeftArrow, RightKey = KeyCode.RightArrow;
        /// <summary>The side value of the hazard lights: both sides flash.</summary>
        internal const int Hazard = 2;
        /// <summary>One flash on and off, in seconds: about 90 flashes a minute.</summary>
        private const float Period = 0.66f;
        /// <summary>Steering this many degrees the signal's way is a turn; back under the second value, the turn is done.</summary>
        private const float InTurnDegrees = 12f, OutOfTurnDegrees = 3f;

        private sealed class Lamp
        {
            internal Renderer R;
            internal Material[] Off, On;
        }

        private sealed class Lamps
        {
            internal readonly List<Lamp> Left = new List<Lamp>(), Right = new List<Lamp>();
            internal bool Lit;
            internal int Shown;
        }

        private static readonly Dictionary<int, int> _sides = new Dictionary<int, int>();
        private static readonly Dictionary<int, Lamps> _lamps = new Dictionary<int, Lamps>();
        private static readonly Dictionary<string, Material> _glow = new Dictionary<string, Material>();

        // The signal the local driver set with a key, so it can cancel itself after the turn.
        private static LandVehicle _keyVehicle;
        private static int _keySide;
        private static bool _inTurn;
        private static float _sentAt = -1f;

        /// <summary>True in the lit half of every flash.</summary>
        internal static bool Phase => Time.time % Period < Period * 0.5f;

        internal static int SideOf(LandVehicle lv)
        {
            if (lv == null) return 0;
            return _sides.TryGetValue(Ids.Of(lv), out int s) ? s : 0;
        }

        internal static void Request(LandVehicle lv, int side, bool fromKey)
        {
            if (lv == null) { Core.Log.Warning("[Indicators] signal asked for a vehicle that is gone."); return; }
            side = Clean(side);
            if (fromKey) { _keyVehicle = side != 0 ? lv : null; _keySide = side; _inTurn = false; }
            else if (_keyVehicle == lv) { _keyVehicle = null; _keySide = 0; }
            _sentAt = Time.time;
            ChassisNet.ToHost("req.blink", Ids.Of(lv).ToString(CultureInfo.InvariantCulture), side.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>-1, 0, 1, or <see cref="Hazard"/>; anything else is its sign.</summary>
        internal static int Clean(int side) => side == Hazard ? Hazard : Math.Sign(side);

        private static string Word(int side) => side == Hazard ? "hazard" : side < 0 ? "left" : side > 0 ? "right" : "off";

        /// <summary>Everyone: the host passed on a vehicle's signal.</summary>
        internal static void OnBlink(int vehicle, int side)
        {
            _sides.TryGetValue(vehicle, out int old);
            if (side == 0) _sides.Remove(vehicle); else _sides[vehicle] = side;
            if (old == side) return;
            var lv = Registry.ById(vehicle);
            Core.Log.Msg("[Indicators] " + (lv != null ? Registry.Name(lv) : "net " + vehicle) + " signals " + Word(side) + ".");
            if (lv != null) ChassisApi.RaiseIndicator(lv, side);
        }

        /// <summary>Host: every signal that is on, for a peer that joined late.</summary>
        internal static void HostReplay()
        {
            foreach (var kv in _sides)
                ChassisNet.ToAll("blink", kv.Key.ToString(CultureInfo.InvariantCulture), kv.Value.ToString(CultureInfo.InvariantCulture));
        }

        internal static void Tick()
        {
            Keys();
            AutoCancel();
            Flash();
            Relay();
        }

        /// <summary>The relay ticks on each half of a flash, in the car the local player sits in.</summary>
        private static void Relay()
        {
            bool phase = Phase;
            if (phase == _relayPhase) return;
            _relayPhase = phase;
            var lp = Player.Local;
            var lv = lp != null && lp.CurrentVehicle != null ? lp.CurrentVehicle.GetComponent<LandVehicle>() : null;
            if (lv == null || SideOf(lv) == 0) return;
            Camera cam = Camera.main;
            if (cam == null) { if (!_noCamera) Core.Log.Warning("[Indicators] no main camera - the relay does not tick."); _noCamera = true; return; }
            _noCamera = false;
            try
            {
                Sounds.InCabin(lv, cam.transform.position + lv.transform.forward * 0.5f - lv.transform.up * 0.2f, phase ? Sounds.Tick : Sounds.Tock, 0.45f);
                if (_relayFailed || !_relayLogged) Core.Log.Msg("[Indicators] relay tick played in " + Registry.Name(lv) + "; later ticks are not logged.");
                _relayFailed = false;
                _relayLogged = true;
            }
            catch (Exception e)
            {
                if (!_relayFailed) Core.Log.Error("[Indicators] the relay tick could not play, logged once until one plays: " + e);
                _relayFailed = true;
            }
        }

        private static bool _relayPhase, _relayFailed, _relayLogged, _noCamera;

        private static void Keys()
        {
            bool left = Input.GetKeyDown(LeftKey), right = Input.GetKeyDown(RightKey);
            if ((!left && !right) || GameInput.IsTyping) return;
            var lp = Player.Local;
            var lv = lp != null && lp.CurrentVehicle != null ? lp.CurrentVehicle.GetComponent<LandVehicle>() : null;
            if (lv == null || !lv.LocalPlayerIsDriver) return;
            if (lv.State != null && !lv.State.IsAcceptingInput) return;
            if (ChassisApi.For(lv)?.Lamps == null)
            {
                Core.Log.Msg("[Indicators] " + Registry.Name(lv) + " has no indicator lamps in its profile - the arrow key does nothing.");
                return;
            }
            int want = left ? -1 : 1;
            Request(lv, SideOf(lv) == want ? 0 : want, true);
        }

        /// <summary>A signal set with a key goes off once the vehicle steered into the turn and straightened again.</summary>
        private static void AutoCancel()
        {
            if (_keyVehicle == null || _keySide == 0) return;
            // The host's answer has not arrived yet; a signal not shown yet cannot be cancelled.
            if (SideOf(_keyVehicle) != _keySide) { if (Time.time - _sentAt > 2f) { _keyVehicle = null; _keySide = 0; } return; }
            float steer = _keyVehicle.CurrentSteerAngle * _keySide;
            if (!_inTurn) { if (steer > InTurnDegrees) _inTurn = true; return; }
            if (steer > OutOfTurnDegrees) return;
            Core.Log.Msg("[Indicators] " + Registry.Name(_keyVehicle) + " is out of the turn - signal off.");
            var lv = _keyVehicle;
            _keyVehicle = null; _keySide = 0; _inTurn = false;
            Request(lv, 0, false);
        }

        private static void Flash()
        {
            bool phase = Phase;
            foreach (var lv in Registry.Profiled)
            {
                if (lv == null) continue;
                var profile = ChassisApi.For(lv);
                if (profile?.Lamps == null) continue;
                var lamps = LampsOf(lv, profile.Lamps);
                int side = SideOf(lv);
                bool lit = side != 0 && phase;
                if (lit == lamps.Lit && side == lamps.Shown) continue;
                lamps.Lit = lit; lamps.Shown = side;
                Paint(lamps.Left, lit && (side < 0 || side == Hazard));
                Paint(lamps.Right, lit && (side == 1 || side == Hazard));
            }
        }

        private static void Paint(List<Lamp> lamps, bool on)
        {
            foreach (var l in lamps)
            {
                if (l.R == null) continue;
                try { l.R.sharedMaterials = on ? l.On : l.Off; }
                catch (Exception e) { Core.Log.Error("[Indicators] could not switch a lamp on " + l.R.name + ", it stays as it is: " + e.Message); l.R = null; }
            }
        }

        private static Lamps LampsOf(LandVehicle lv, LampProfile profile)
        {
            int key = lv.GetInstanceID();
            if (_lamps.TryGetValue(key, out var lamps)) return lamps;
            lamps = new Lamps();
            Collect(lv.transform, profile.Left, profile, lamps.Left);
            Collect(lv.transform, profile.Right, profile, lamps.Right);
            if (profile.Left.Count + profile.Right.Count == 0)
                Core.Log.Msg("[Indicators] " + Registry.Name(lv) + ": no lamps in the profile, the mod draws its own.");
            else if (lamps.Left.Count == 0 && lamps.Right.Count == 0)
                Core.Log.Warning("[Indicators] " + Registry.Name(lv) + ": none of the lamps " + string.Join(", ", profile.Left) + " / " + string.Join(", ", profile.Right) + " found - the signal stays dark.");
            else
                Core.Log.Msg("[Indicators] " + Registry.Name(lv) + ": " + lamps.Left.Count + " left and " + lamps.Right.Count + " right lamp renderer(s).");
            _lamps[key] = lamps;
            return lamps;
        }

        private static void Collect(Transform root, List<string> groups, LampProfile profile, List<Lamp> into)
        {
            if (groups == null || groups.Count == 0) return;
            var seen = new HashSet<int>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                bool match = false;
                foreach (string g in groups) if (Frames.InGroup(t.name, g)) { match = true; break; }
                if (!match) continue;
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || !seen.Add(r.GetInstanceID())) continue;
                    var mats = r.sharedMaterials;
                    var off = new Material[mats.Length];
                    var on = new Material[mats.Length];
                    for (int i = 0; i < mats.Length; i++)
                    {
                        off[i] = mats[i];
                        on[i] = mats[i] != null ? Glow(mats[i], profile) : null;
                    }
                    into.Add(new Lamp { R = r, Off = off, On = on });
                }
            }
        }

        /// <summary>An amber glowing copy of a lamp material, made once per material and colour.</summary>
        private static Material Glow(Material baseMat, LampProfile profile)
        {
            string key = baseMat.GetInstanceID() + "|" + profile.Color + "|" + profile.UseTexture;
            if (_glow.TryGetValue(key, out var mat) && mat != null) return mat;
            mat = new Material(baseMat) { name = baseMat.name + "_ChassisIndicator" };
            mat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            Texture map = profile.UseTexture && baseMat.HasProperty("_BaseMap") ? baseMat.GetTexture("_BaseMap") : null;
            if (map != null) mat.SetTexture("_EmissionMap", map);
            else
            {
                mat.SetColor("_BaseColor", profile.Color);
                if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", null);
            }
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", profile.Color * 4f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            _glow[key] = mat;
            return mat;
        }

        internal static string Describe()
        {
            if (_sides.Count == 0) return "indicators: all off";
            var parts = new List<string>();
            foreach (var kv in _sides) parts.Add("net " + kv.Key + " " + Word(kv.Value));
            return "indicators: " + string.Join(", ", parts) + (_keyVehicle != null ? " (key signal" + (_inTurn ? ", in the turn)" : ")") : "");
        }

        internal static void Reset()
        {
            _sides.Clear();
            _lamps.Clear();
            _keyVehicle = null;
            _keySide = 0;
            _inTurn = false;
        }
    }
}
