using System;
using System.Collections.Generic;
using UnityEngine.Audio;

namespace Chassis.Vehicles
{
    /// <summary>
    /// Clips the vehicles play: the ones mods register by name, and built-in stand-ins made from sine waves and noise
    /// (a two-tone horn, a short lock chirp, a switch click and the indicator tick), so no recording ships with the mod.
    ///
    /// The game's sound settings apply as in vanilla's <c>AudioSourceController</c>: every source goes through
    /// <c>AudioManager.MainGameMixer</c>, every volume is multiplied by <c>AudioManager.GetVolume(FX)</c>.
    /// </summary>
    internal static class Sounds
    {
        internal const string Horn = "chassis.horn";
        internal const string Chirp = "chassis.chirp";
        /// <summary>A switch in the cabin moves.</summary>
        internal const string Click = "chassis.click";
        /// <summary>The indicator relay, on the lit and the dark half of a flash.</summary>
        internal const string Tick = "chassis.tick", Tock = "chassis.tock";
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
            switch (fallback)
            {
                case Chirp: builtIn = Synth(Chirp, 0.09f, new[] { 1760f }, 3); break;
                case Click: builtIn = Knock(Click, 0.035f, 2900f, 0.55f, 1); break;
                case Tick: builtIn = Knock(Tick, 0.025f, 2200f, 0.35f, 2); break;
                case Tock: builtIn = Knock(Tock, 0.025f, 1500f, 0.35f, 3); break;
                default: builtIn = Synth(Horn, 0.55f, new[] { 415f, 494f }, 7); break;
            }
            _clips[fallback] = builtIn;
            return builtIn;
        }

        /// <summary>
        /// A short mechanical knock: a decaying tone with a burst of noise on its front, like a switch or a relay. The
        /// noise is seeded, so the clip sounds the same on every peer and every start.
        /// </summary>
        private static AudioClip Knock(string name, float length, float tone, float noise, int seed)
        {
            int count = Mathf.RoundToInt(length * Rate);
            var data = new float[count];
            var rng = new System.Random(seed);
            float tau = 2f * Mathf.PI;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / Rate;
                float body = Mathf.Exp(-t / (length * 0.22f)), snap = Mathf.Exp(-t / 0.002f);
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                data[i] = 0.5f * (body * Mathf.Sin(tau * tone * t) + noise * snap * n);
            }
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Core.Log.Msg("[Sound] built-in '" + name + "' made, " + length.ToString("F3") + " s.");
            return clip;
        }

        /// <summary>
        /// Plays a cabin sound at a point in the vehicle: quiet, heard inside the car and next to it only. One source
        /// per vehicle, moved to the point before each sound.
        /// </summary>
        internal static void InCabin(LandVehicle v, Vector3 world, string clip, float level)
        {
            Transform t = v.transform.Find(CabinName);
            AudioSource source = t != null ? t.GetComponent<AudioSource>() : null;
            if (source == null)
            {
                source = On(v, CabinName, Vector3.zero);
                source.minDistance = 0.6f;
                source.maxDistance = 7f;
            }
            source.transform.position = world;
            source.PlayOneShot(Get(null, clip), level * Volume());
        }

        private const string CabinName = "ChassisCabin";

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
