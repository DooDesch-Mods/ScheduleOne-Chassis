using System;
using System.Collections.Generic;
using System.Globalization;
using Chassis.Api;
using Chassis.Interior;
using Chassis.Vehicles;

namespace Chassis.Instruments
{
    /// <summary>
    /// Draws the gauges of every profiled vehicle near the camera, on every peer: needles turn, lamps glow, digits
    /// count. Each gauge finds its transform once per vehicle and interior; a path that is not there is logged once per
    /// vehicle code and path, and the gauge stays still.
    /// </summary>
    internal static class Gauges
    {
        /// <summary>Gauges are not drawn beyond this distance from the camera.</summary>
        private const float Range = 30f;

        private sealed class Bound
        {
            internal GaugeProfile P;
            internal string Kind;
            internal Transform T;
            internal Quaternion Rest;
            internal float Shown;
            internal bool Started;
            internal float LastAngle = float.NaN;
            // lamp
            internal Renderer[] Lamps;
            internal Material[][] Off, On;
            internal bool Lit;
            // digits
            internal GameText Text;
            internal string LastText;
        }

        private sealed class Cab
        {
            internal LandVehicle V;
            internal ChassisProfile Profile;
            internal readonly List<Bound> Items = new List<Bound>();
        }

        private static readonly Dictionary<int, Cab> _cabs = new Dictionary<int, Cab>();
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Material> _glow = new Dictionary<string, Material>();
        private static readonly List<int> _gone = new List<int>();
        private static GameFontAsset _font;
        private static bool _fontSought;
        private static float _nextPrune;

        internal static void Tick()
        {
            Camera cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            float dt = Time.deltaTime;
            foreach (var lv in Registry.Profiled)
            {
                if (lv == null) continue;
                var profile = ChassisApi.For(lv);
                if (profile == null || profile.Gauges.Count == 0) continue;
                if (cam != null && (lv.transform.position - eye).sqrMagnitude > Range * Range) continue;
                if (!BaseInterior.Ready(lv, profile)) continue;
                var cab = Of(lv, profile);
                foreach (var b in cab.Items) Draw(lv, b, dt);
            }
            if (Time.unscaledTime < _nextPrune) return;
            _nextPrune = Time.unscaledTime + 2f;
            _gone.Clear();
            foreach (var kv in _cabs) if (kv.Value.V == null) _gone.Add(kv.Key);
            foreach (int k in _gone) _cabs.Remove(k);
        }

        private static Cab Of(LandVehicle lv, ChassisProfile profile)
        {
            int key = lv.GetInstanceID();
            if (_cabs.TryGetValue(key, out var cab) && cab.Profile == profile) return cab;
            if (cab != null) Release(cab);
            cab = new Cab { V = lv, Profile = profile };
            _cabs[key] = cab;
            Transform frame = Frames.Resolve(lv.transform, profile.Frame);
            int found = 0;
            foreach (var g in profile.Gauges)
            {
                if (g == null) continue;
                string kind = (g.Kind ?? "").ToLowerInvariant();
                if (kind != "needle" && kind != "lamp" && kind != "digits")
                {
                    Warn(profile.Code, "kind|" + g.Name, "gauge '" + g.Name + "' has the kind '" + g.Kind + "' - needle, lamp or digits; it is skipped.");
                    continue;
                }
                if (!Sources.Exists(g.Source))
                    Warn(profile.Code, "source|" + g.Source, "gauge '" + g.Name + "' reads the source '" + g.Source + "', which does not exist (yet) - it stays still until a mod registers it.");
                Transform t = string.IsNullOrEmpty(g.Path) ? null : Frames.Resolve(frame, g.Path);
                if (t == null)
                {
                    Warn(profile.Code, "path|" + g.Path, "gauge '" + g.Name + "' path '" + g.Path + "' not found below the frame - it stays still.");
                    continue;
                }
                var b = new Bound { P = g, Kind = kind, T = t, Rest = t.localRotation };
                if (kind == "lamp") BindLamp(b);
                else if (kind == "digits") BindDigits(b, profile.Code);
                cab.Items.Add(b);
                found++;
            }
            Core.Log.Msg("[Gauges] " + Registry.Name(lv) + ": " + found + " of " + profile.Gauges.Count + " gauge(s) bound.");
            return cab;
        }

        private static void Draw(LandVehicle lv, Bound b, float dt)
        {
            if (b.T == null) return;
            var r = Sources.Read(lv, b.P.Source);
            switch (b.Kind)
            {
                case "needle":
                {
                    if (!r.Valid) return;
                    float target = Mathf.InverseLerp(b.P.Min, b.P.Max, r.Value);
                    b.Shown = Follow(b, target, dt);
                    float angle = Mathf.Lerp(b.P.From, b.P.To, b.Shown);
                    if (!float.IsNaN(b.LastAngle) && Mathf.Abs(angle - b.LastAngle) < 0.02f) return;
                    b.LastAngle = angle;
                    b.T.localRotation = b.Rest * Quaternion.AngleAxis(angle, Axis(b.P.Axis));
                    return;
                }
                case "lamp":
                {
                    bool lit = r.Valid && r.Value > b.P.Threshold;
                    if (lit == b.Lit) return;
                    b.Lit = lit;
                    for (int i = 0; i < b.Lamps.Length; i++)
                    {
                        if (b.Lamps[i] == null) continue;
                        try { b.Lamps[i].sharedMaterials = lit ? b.On[i] : b.Off[i]; }
                        catch (Exception e) { Core.Log.Error("[Gauges] lamp '" + b.P.Name + "' could not switch " + b.Lamps[i].name + ", it stays as it is: " + e.Message); b.Lamps[i] = null; }
                    }
                    return;
                }
                case "digits":
                {
                    if (b.Text == null) return;
                    string s;
                    if (!r.Valid) s = "--";
                    else
                    {
                        b.Shown = Follow(b, r.Value, dt);
                        s = Format(b.P, b.Shown);
                    }
                    if (s == b.LastText) return;
                    b.LastText = s;
                    b.Text.text = s;
                    return;
                }
            }
        }

        /// <summary>The shown value moves towards the target with the gauge's smoothing time.</summary>
        private static float Follow(Bound b, float target, float dt)
        {
            if (!b.Started || b.P.Smooth <= 0f) { b.Started = true; return target; }
            return Mathf.Lerp(b.Shown, target, 1f - Mathf.Exp(-dt / b.P.Smooth));
        }

        private static Vector3 Axis(Vector3 a) => a.sqrMagnitude > 1e-6f ? a.normalized : Vector3.back;

        internal static string Format(GaugeProfile p, float v)
        {
            if (string.Equals(p.Source, "clock", StringComparison.OrdinalIgnoreCase))
            {
                int t = Mathf.RoundToInt(v);
                return (t / 100 % 24).ToString("00", CultureInfo.InvariantCulture) + ":" + (t % 100).ToString("00", CultureInfo.InvariantCulture);
            }
            try { return v.ToString(string.IsNullOrEmpty(p.Format) ? "0" : p.Format, CultureInfo.InvariantCulture); }
            catch (FormatException) { return v.ToString("0", CultureInfo.InvariantCulture); }
        }

        // ---- lamps -------------------------------------------------------------------------------------------------

        private static void BindLamp(Bound b)
        {
            var rs = b.T.GetComponentsInChildren<Renderer>(true);
            b.Lamps = rs;
            b.Off = new Material[rs.Length][];
            b.On = new Material[rs.Length][];
            for (int i = 0; i < rs.Length; i++)
            {
                var mats = rs[i].sharedMaterials;
                b.Off[i] = mats;
                var on = new Material[mats.Length];
                for (int k = 0; k < mats.Length; k++) on[k] = mats[k] != null ? Glow(mats[k], b.P.Color) : null;
                b.On[i] = on;
            }
            if (rs.Length == 0) Core.Log.Warning("[Gauges] lamp '" + b.P.Name + "' at '" + b.P.Path + "' has no renderer to light.");
        }

        /// <summary>A glowing copy of a lamp material, made once per material and colour.</summary>
        private static Material Glow(Material baseMat, Color c)
        {
            string key = baseMat.GetInstanceID() + "|" + c;
            if (_glow.TryGetValue(key, out var mat) && mat != null) return mat;
            mat = new Material(baseMat) { name = baseMat.name + "_ChassisGauge" };
            mat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", null);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", c * 3f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            _glow[key] = mat;
            return mat;
        }

        // ---- digits ------------------------------------------------------------------------------------------------

        private const string DigitsName = "Chassis_Digits";

        private static void BindDigits(Bound b, string code)
        {
            var font = Font();
            if (font == null)
            {
                Warn(code, "font", "no TextMeshPro font loaded - digit gauges stay empty.");
                return;
            }
            try
            {
                var old = b.T.Find(DigitsName);
                if (old != null) { old.SetParent(null, false); UnityEngine.Object.Destroy(old.gameObject); }
                var go = new GameObject(DigitsName);
                go.transform.SetParent(b.T, false);
                var text = go.AddComponent<GameText>();
                text.font = font;
                text.fontSize = 10f;
                text.alignment = GameTextAlign.Center;
                text.enableWordWrapping = false;
                text.color = b.P.Color;
                text.text = "0";
                text.ForceMeshUpdate();
                // TextMeshPro's size is in its own units; the line height of a digit is scaled to the profile's Size in metres.
                float h = text.preferredHeight;
                float parent = Mathf.Abs(b.T.lossyScale.y);
                float s = h > 1e-5f && parent > 1e-5f ? b.P.Size / (h * parent) : 0.0025f;
                go.transform.localScale = new Vector3(s, s, s);
                text.text = string.Empty;
                b.Text = text;
            }
            catch (Exception e) { Core.Log.Error("[Gauges] digits '" + b.P.Name + "' could not be made, they stay empty: " + e); }
        }

        private static GameFontAsset Font()
        {
            if (_font != null || _fontSought) return _font;
            _fontSought = true;
            try
            {
                foreach (var f in Resources.FindObjectsOfTypeAll<GameFontAsset>())
                {
                    if (f == null) continue;
                    if (_font == null) _font = f;
                    // The default TextMeshPro font has every digit; the game's own fonts are tried after it.
                    if (f.name.StartsWith("LiberationSans", StringComparison.OrdinalIgnoreCase)) { _font = f; break; }
                }
            }
            catch (Exception e) { Core.Log.Warning("[Gauges] looking for a TextMeshPro font failed: " + e.Message); }
            Core.Log.Msg("[Gauges] digits use the font " + (_font != null ? "'" + _font.name + "'" : "- none found") + ".");
            return _font;
        }

        // ---- bookkeeping -------------------------------------------------------------------------------------------

        private static void Warn(string code, string what, string text)
        {
            if (!_warned.Add(code + "|" + what)) return;
            Core.Log.Warning("[Gauges] " + code + ": " + text);
        }

        private static void Release(Cab cab)
        {
            foreach (var b in cab.Items)
            {
                try
                {
                    if (b.T != null && b.Kind == "needle") b.T.localRotation = b.Rest;
                    if (b.Lamps != null)
                        for (int i = 0; i < b.Lamps.Length; i++) if (b.Lamps[i] != null) b.Lamps[i].sharedMaterials = b.Off[i];
                    if (b.Text != null) UnityEngine.Object.Destroy(b.Text.gameObject);
                }
                catch (Exception e) { Core.Log.Warning("[Gauges] putting gauge '" + b.P.Name + "' back failed: " + e.Message); }
            }
            cab.Items.Clear();
        }

        /// <summary>The vehicle's interior was rebuilt or removed; its gauges bind again.</summary>
        internal static void Forget(LandVehicle lv)
        {
            if (lv == null || !_cabs.TryGetValue(lv.GetInstanceID(), out var cab)) return;
            Release(cab);
            _cabs.Remove(lv.GetInstanceID());
        }

        /// <summary>A profile changed; every vehicle binds its gauges again, and paths warn again.</summary>
        internal static void ForgetAll()
        {
            foreach (var cab in _cabs.Values) Release(cab);
            _cabs.Clear();
            _warned.Clear();
        }

        internal static IEnumerable<string> Describe(LandVehicle lv)
        {
            if (lv == null || !_cabs.TryGetValue(lv.GetInstanceID(), out var cab)) { yield return "gauges: not bound (no profile gauges, too far, or the interior is not built yet)"; yield break; }
            foreach (var b in cab.Items)
            {
                var r = Sources.Read(lv, b.P.Source);
                yield return "gauge " + b.P.Name + " (" + b.Kind + ", " + b.P.Source + ") = " + r
                    + (b.Kind == "needle" ? ", at " + Mathf.Lerp(b.P.From, b.P.To, b.Shown).ToString("F0", CultureInfo.InvariantCulture) + " deg" : b.Kind == "lamp" ? (b.Lit ? ", lit" : ", dark") : ", shows '" + b.LastText + "'");
            }
        }

        internal static void Reset()
        {
            _cabs.Clear();
            _warned.Clear();
            _nextPrune = 0f;
        }
    }
}
