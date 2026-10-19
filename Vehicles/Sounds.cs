using System;
using System.Collections.Generic;
using UnityEngine.Audio;

namespace Chassis.Vehicles
{
    /// <summary>
    /// Clips the vehicles play: the ones mods register by name, and two built-in stand-ins made from sine waves
    /// (a two-tone horn and a short lock chirp), so every vehicle has a horn without shipping a recording.
    ///
    /// The game's sound settings apply as in vanilla's <c>AudioSourceController</c>: every source goes through
    /// <c>AudioManager.MainGameMixer</c>, every volume is multiplied by <c>AudioManager.GetVolume(FX)</c>.
    /// </summary>
    internal static class Sounds
    {
        internal const string Horn = "chassis.horn";
        internal const string Chirp = "chassis.chirp";
        private const int Rate = 44100;

        private static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static AudioMixerGroup _group;
        private static bool _noManager;

        internal static void Register(string name, AudioClip clip)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("a sound needs a name");
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            clip.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            _clips[name] = clip;
            _missing.Remove(name);
            Core.Log.Msg("[Sound] '" + name + "' registered, " + clip.length.ToString("F2") + " s.");
        }

        /// <summary>The clip by name; an empty or unknown name falls back to the built-in one (logged once).</summary>
        internal static AudioClip Get(string name, string fallback)
        {
            if (!string.IsNullOrEmpty(name))
            {
                if (_clips.TryGetValue(name, out var clip) && clip != null) return clip;
                if (_missing.Add(name)) Core.Log.Warning("[Sound] '" + name + "' is not registered, the built-in " + fallback + " plays instead.");
            }
            if (_clips.TryGetValue(fallback, out var builtIn) && builtIn != null) return builtIn;
            builtIn = fallback == Chirp ? Synth(Chirp, 0.09f, new[] { 1760f }, 3) : Synth(Horn, 0.55f, new[] { 415f, 494f }, 7);
            _clips[fallback] = builtIn;
            return builtIn;
        }

        /// <summary>Odd harmonics over 1/n give the reedy edge of a car horn without turning harsh.</summary>
        private static AudioClip Synth(string name, float length, float[] tones, int harmonics)
        {
            int count = Mathf.RoundToInt(length * Rate);
            var data = new float[count];
            float tau = 2f * Mathf.PI;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Clamp01(t / 0.01f) * Mathf.Clamp01((length - t) / Mathf.Min(0.06f, length * 0.4f));
                float s = 0f;
                foreach (float f in tones)
                    for (int n = 1; n <= harmonics; n += 2) s += Mathf.Sin(tau * f * n * t) / n;
                data[i] = 0.22f * env * s;
            }
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Core.Log.Msg("[Sound] built-in '" + name + "' made, " + length.ToString("F2") + " s.");
            return clip;
        }

        /// <summary>The game's master times effects volume, 0 to 1; 1 with a warning when the audio manager is missing.</summary>
        internal static float Volume()
        {
            try
            {
                if (Singleton<GameAudioManager>.InstanceExists)
                {
                    _noManager = false;
                    return Mathf.Clamp01(Singleton<GameAudioManager>.Instance.GetVolume(EAudioType.FX, true));
                }
            }
            catch (Exception e)
            {
                if (!_noManager) Core.Log.Warning("[Sound] could not read the game's volume: " + e.Message);
                _noManager = true;
                return 1f;
            }
            if (!_noManager) Core.Log.Warning("[Sound] no audio manager - vehicle sounds play at full volume.");
            _noManager = true;
            return 1f;
        }

        /// <summary>A 3D audio source on the vehicle, made once per name.</summary>
        internal static AudioSource On(LandVehicle v, string name, Vector3 local)
        {
            Transform t = v.transform.Find(name);
            if (t != null)
            {
                var existing = t.GetComponent<AudioSource>();
                if (existing != null) return existing;
            }
            var go = t != null ? t.gameObject : new GameObject(name);
            go.transform.SetParent(v.transform, false);
            go.transform.localPosition = local;
            var source = go.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.minDistance = 6f;
            source.maxDistance = 120f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.playOnAwake = false;
            source.dopplerLevel = 0f;
            source.outputAudioMixerGroup = Group();
            return source;
        }

        /// <summary>The mixer group every in-world game sound plays through; it carries the pause menu's muting.</summary>
        private static AudioMixerGroup Group()
        {
            if (_group != null) return _group;
            try
            {
                if (Singleton<GameAudioManager>.InstanceExists) _group = Singleton<GameAudioManager>.Instance.MainGameMixer;
                if (_group == null) Core.Log.Warning("[Sound] no game mixer group yet - this sound skips the mixer.");
            }
            catch (Exception e) { Core.Log.Warning("[Sound] could not read the game mixer group: " + e.Message); }
            return _group;
        }
    }
}
