using System;
using System.Collections.Generic;
using System.Linq;
using Chassis.Api;
using System.Reflection;
using Chassis.Compat;
using Chassis.Net;
using Chassis.Vehicles;
using HarmonyLib;

namespace Chassis.Tune
{
    /// <summary>
    /// Free paint colour and finish on the body: the targets a mod set (<see cref="ChassisApi.SetPaintTargets(LandVehicle, IEnumerable{PaintTarget})"/>),
    /// else the vanilla body. Vanilla paints one material slot per <c>VehicleColor.BodyMeshes</c>
    /// entry with one of 16 library colours; Chassis puts its own material copy into those slots and puts it back after
    /// every vanilla <c>ApplyColor</c>, except while Marco's repaint menu previews a colour on this car. Buying a repaint
    /// there clears the custom paint (<see cref="RepaintPatch"/>). Targets marked <see cref="PaintTarget.Secondary"/> take
    /// the second paint ("paint2") and keep their own material without one.
    /// </summary>
    internal static class Paint
    {
        internal sealed class Slot
        {
            internal Renderer R;
            internal int Index;
            internal Material Original;
            internal Material Mine;
        }

        // Every material slot Chassis paints, by renderer and index. A vehicle's paint and a paint without a vehicle
        // (PaintRenderers) share the slot, so the original material is remembered once, whoever painted first.
        private static readonly Dictionary<long, Slot> _slots = new Dictionary<long, Slot>();

        private sealed class Targets
        {
            internal LandVehicle V;
            internal List<PaintTarget> List;
        }

        private static readonly Dictionary<int, Targets> _byVehicle = new Dictionary<int, Targets>();
        private static readonly Dictionary<string, Func<LandVehicle, IEnumerable<PaintTarget>>> _byCode =
            new Dictionary<string, Func<LandVehicle, IEnumerable<PaintTarget>>>(StringComparer.OrdinalIgnoreCase);

        private const string Retargeted = "\u0001retarget";

        private static long KeyOf(Renderer r, int index) => ((long)r.GetInstanceID() << 8) ^ index;

        // ---- targets --------------------------------------------------------------------------------------------

        internal static void SetTargets(LandVehicle v, IEnumerable<PaintTarget> targets)
        {
            if (v == null) { Core.Log.Warning("[Tuning] SetPaintTargets: no vehicle - ignored."); return; }
            int key = v.GetInstanceID();
            if (targets == null) _byVehicle.Remove(key);
            else _byVehicle[key] = new Targets { V = v, List = Clean(targets) };
            Core.Log.Msg("[Tuning] " + Registry.Name(v) + ": paint targets " + (targets == null ? "back to the body meshes." : "set, " + _byVehicle[key].List.Count + " slot(s)."));
            Retarget(Tune.Apply.Find(v));
        }

        internal static void SetTargets(string code, Func<LandVehicle, IEnumerable<PaintTarget>> targets)
        {
            if (string.IsNullOrEmpty(code)) { Core.Log.Warning("[Tuning] SetPaintTargets: no vehicle code - ignored."); return; }
            if (targets == null) _byCode.Remove(code); else _byCode[code] = targets;
            Core.Log.Msg("[Tuning] paint targets for code '" + code + "' " + (targets == null ? "removed." : "set."));
            foreach (var c in Tune.Apply.Cars) if (c.V != null && string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)) Retarget(c);
        }

        /// <summary>The targets changed: take the paint off the old slots and paint the new ones on the next apply.</summary>
        private static void Retarget(Tune.Apply.Car c)
        {
            if (c == null) return;
            foreach (var s in c.PaintSlots) Release(s);
            c.PaintSlots.Clear();
            c.PaintKey = Retargeted;
            foreach (var s in c.Paint2Slots) Release(s);
            c.Paint2Slots.Clear();
            c.Paint2Key = Retargeted;
            c.Seen = -1;
        }

        /// <summary>The slots that take this car's body paint: a mod's targets first, else the vanilla body meshes.</summary>
        internal static List<PaintTarget> TargetsOf(LandVehicle v, out bool own)
        {
            own = true;
            if (v == null) return new List<PaintTarget>();
            foreach (var k in new List<int>(_byVehicle.Keys)) if (_byVehicle[k].V == null) _byVehicle.Remove(k);
            if (_byVehicle.TryGetValue(v.GetInstanceID(), out var t)) return Clean(t.List);
            string code = ChassisApi.CodeOf(v);
            if (code != null && _byCode.TryGetValue(code, out var f))
            {
                try { return Clean(f(v)); }
                catch (Exception e) { Core.Log.Error("[Tuning] the paint targets of code '" + code + "' failed - the body meshes are painted: " + e.Message); }
            }
            own = false;
            var list = new List<PaintTarget>();
            var vc = v.Color;
            if (vc != null && vc.BodyMeshes != null)
                foreach (var bm in vc.BodyMeshes)
                    if (bm != null && bm.Renderer != null) list.Add(new PaintTarget(bm.Renderer, bm.MaterialIndex));
            return list;
        }

        private static List<PaintTarget> Clean(IEnumerable<PaintTarget> targets)
        {
            var list = new List<PaintTarget>();
            if (targets == null) return list;
            foreach (var t in targets) if (t.Renderer != null) list.Add(t);
            return list;
        }

        // ---- slots ------------------------------------------------------------------------------------------------

        private static Slot Take(Renderer r, int index, bool flat, string owner)
        {
            long key = KeyOf(r, index);
            if (_slots.TryGetValue(key, out var slot) && slot.R != null) return slot;
            var mats = r.sharedMaterials;
            if (index < 0 || index >= mats.Length || mats[index] == null)
            {
                Core.Log.Warning("[Tuning] " + owner + ": paint slot " + index + " of " + r.name + " does not exist - skipped.");
                return null;
            }
            slot = new Slot { R = r, Index = index, Original = mats[index] };
            slot.Mine = new Material(slot.Original) { name = "Chassis_Paint" };
            if (flat)
            {
                if (slot.Mine.HasProperty("_BaseMap")) slot.Mine.SetTexture("_BaseMap", null);
                if (slot.Mine.HasProperty("_MainTex")) slot.Mine.SetTexture("_MainTex", null);
            }
            _slots[key] = slot;
            return slot;
        }

        /// <summary>The original material goes back into the slot, and the slot is forgotten.</summary>
        private static void Release(Slot s)
        {
            if (s.R == null) return;
            var mats = s.R.sharedMaterials;
            if (s.Index < mats.Length) { mats[s.Index] = s.Original; s.R.sharedMaterials = mats; }
            _slots.Remove(KeyOf(s.R, s.Index));
        }

        /// <summary>The body paint on the primary targets; null takes it off and lets vanilla paint its colour.</summary>
        internal static void Apply(Tune.Apply.Car c, string value)
        {
            string key = value ?? "";
            if (key == c.PaintKey) return;
            if (value == null)
            {
                Restore(c);
                c.PaintKey = key;
                return;
            }
            var targets = Of(TargetsOf(c.V, out bool own), false);
            if (targets.Count == 0) { c.PaintKey = key; Core.Log.Warning("[Tuning] " + Registry.Name(c.V) + " has no paintable body - paint ignored."); return; }
            Values.Paint(value, out Color colour, out string finish);
            PaintSlots(c, c.PaintSlots, targets, colour, finish);
            c.PaintKey = key;
            Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + " painted " + Values.Hex(colour) + " " + finish + " on " + c.PaintSlots.Count + " slot(s)" + (own ? ", targets of a mod." : "."));
        }

        /// <summary>
        /// The second paint on the secondary targets; null puts their own materials back. A car without secondary targets
        /// shows the second paint only on parts with a "paint2" material (<see cref="Body"/>).
        /// </summary>
        internal static void Apply2(Tune.Apply.Car c, string value)
        {
            string key = value ?? "";
            if (key == c.Paint2Key) return;
            c.Paint2Key = key;
            if (value == null)
            {
                if (c.Paint2Slots.Count == 0) return;
                foreach (var s in c.Paint2Slots) Release(s);
                c.Paint2Slots.Clear();
                Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + ": second paint removed.");
                return;
            }
            var targets = Of(TargetsOf(c.V, out _), true);
            if (targets.Count == 0) { Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + " has no second paint area - the second paint shows on parts only."); return; }
            Values.Paint(value, out Color colour, out string finish);
            PaintSlots(c, c.Paint2Slots, targets, colour, finish);
            Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + " second paint " + Values.Hex(colour) + " " + finish + " on " + c.Paint2Slots.Count + " slot(s).");
        }

        private static List<PaintTarget> Of(List<PaintTarget> all, bool secondary)
        {
            var list = new List<PaintTarget>();
            foreach (var t in all) if (t.Secondary == secondary) list.Add(t);
            return list;
        }

        private static void PaintSlots(Tune.Apply.Car c, List<Slot> slots, List<PaintTarget> targets, Color colour, string finish)
        {
            Values.Finish(finish, out float metallic, out float smooth);
            foreach (var t in targets)
            {
                Slot slot = null;
                foreach (var s in slots) if (Interop.Same(s.R, t.Renderer) && s.Index == t.MaterialIndex) { slot = s; break; }
                if (slot == null)
                {
                    slot = Take(t.Renderer, t.MaterialIndex, t.Flat, Registry.Name(c.V));
                    if (slot == null) continue;
                    slots.Add(slot);
                }
                Colour(slot.Mine, colour, metallic, smooth);
                Put(slot);
            }
        }

        /// <summary>
        /// Paints material slots without a vehicle, e.g. a model that stays when its vehicle is gone. Same value as the
        /// "paint" key; null or empty puts the original materials back.
        /// </summary>
        internal static bool PaintFree(IEnumerable<PaintTarget> targets, string value)
        {
            var list = Clean(targets);
            if (list.Count == 0) { Core.Log.Warning("[Tuning] PaintRenderers: no renderer given - nothing painted."); return false; }
            if (string.IsNullOrEmpty(value))
            {
                int n = 0;
                foreach (var t in list) if (_slots.TryGetValue(KeyOf(t.Renderer, t.MaterialIndex), out var s)) { Release(s); n++; }
                Core.Log.Msg("[Tuning] PaintRenderers: " + n + " slot(s) back to their own material.");
                return true;
            }
            if (!Values.Paint(value, out Color colour, out string finish)) { Core.Log.Warning("[Tuning] PaintRenderers: '" + value + "' is no paint (RRGGBB,finish) - nothing painted."); return false; }
            Values.Finish(finish, out float metallic, out float smooth);
            int done = 0;
            foreach (var t in list)
            {
                var slot = Take(t.Renderer, t.MaterialIndex, t.Flat, "PaintRenderers");
                if (slot == null) continue;
                Colour(slot.Mine, colour, metallic, smooth);
                Put(slot);
                done++;
            }
            Core.Log.Msg("[Tuning] PaintRenderers: " + Values.Hex(colour) + " " + finish + " on " + done + " of " + list.Count + " slot(s).");
            return done > 0;
        }

        /// <summary>Sets a material's colour and finish, on URP/Lit and on the built-in names.</summary>
        internal static void Colour(Material m, Color colour, float metallic, float smooth)
        {
            m.color = colour;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        }

        private static void Put(Slot s)
        {
            if (s.R == null || s.Mine == null) return;
            var mats = s.R.sharedMaterials;
            if (s.Index >= mats.Length || Interop.Same(mats[s.Index], s.Mine)) return;
            mats[s.Index] = s.Mine;
            s.R.sharedMaterials = mats;
        }

        /// <summary>Back to the vanilla colour: the original materials go back in and vanilla paints its owned colour.</summary>
        private static void Restore(Tune.Apply.Car c)
        {
            if (c.PaintSlots.Count == 0) return;
            foreach (var s in c.PaintSlots) Release(s);
            c.PaintSlots.Clear();
            var owned = c.V.OwnedColor;
            c.V.Color.ApplyColor(owned);
            Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + ": custom paint removed, back to vanilla " + owned + ".");
        }

        /// <summary>Marco's menu shows a colour on this car right now; Chassis keeps its paint off until it closes.</summary>
        internal static bool Previewing(LandVehicle v)
        {
            if (!Singleton<GameVehicleModMenu>.InstanceExists) return false;
            var menu = Singleton<GameVehicleModMenu>.Instance;
            return menu != null && menu.IsOpen && Interop.Same(menu.currentVehicle, v);
        }

        internal static void Verify(Tune.Apply.Car c)
        {
            if (Previewing(c.V)) return;
            if (c.PaintKey.Length > 0) foreach (var s in c.PaintSlots) Put(s);
            if (c.Paint2Key.Length > 0) foreach (var s in c.Paint2Slots) Put(s);
        }

        /// <summary>Vanilla painted a car: put the custom paint back unless Marco is previewing.</summary>
        internal static void AfterVanilla(VehicleColor vc)
        {
            foreach (var c in Tune.Apply.Cars)
            {
                if (c.V == null || !Interop.Same(c.V.Color, vc)) continue;
                if (Previewing(c.V)) return;
                if (c.PaintKey.Length > 0) foreach (var s in c.PaintSlots) Put(s);
                if (c.Paint2Key.Length > 0) foreach (var s in c.Paint2Slots) Put(s);
                return;
            }
        }

        /// <summary>The body colour the car shows now (custom or vanilla), for parts painted like the body.</summary>
        internal static Color BodyColour(LandVehicle v, out float metallic, out float smooth)
        {
            metallic = 0.1f; smooth = 0.6f;
            string p = State.GetShown(v, "paint");
            if (p != null && Values.Paint(p, out Color c, out string finish)) { Values.Finish(finish, out metallic, out smooth); return c; }
            foreach (var t in Of(TargetsOf(v, out _), false))
            {
                var mats = t.Renderer.sharedMaterials;
                if (t.MaterialIndex >= 0 && t.MaterialIndex < mats.Length && mats[t.MaterialIndex] != null) return mats[t.MaterialIndex].color;
            }
            return Color.gray;
        }
    }

    [HarmonyPatch(typeof(VehicleColor), nameof(VehicleColor.ApplyColor))]
    internal static class PaintPatch
    {
        private static void Postfix(VehicleColor __instance)
        {
            try { Paint.AfterVanilla(__instance); }
            catch (Exception e) { Core.Log.Error("[Tuning] putting the custom paint back after a vanilla repaint failed: " + e.Message); }
        }
    }

    /// <summary>
    /// A repaint bought at Marco's (<c>SendOwnedColor</c>, called by the menu's buy button on the buyer's machine) clears
    /// the custom paint, so the bought colour shows. The buyer asks like any tuning change: the host applies it, a client
    /// sends it to the host. The game's own load path uses <c>SetOwnedColor</c> and keeps the custom paint. The hook sits
    /// on <c>SendOwnedColor</c> because IL2CPP compiles its small <c>RpcLogic</c> body into the caller.
    /// </summary>
    internal static class RepaintPatch
    {
        internal static void Apply(HarmonyLib.Harmony h)
        {
            try
            {
                MethodInfo target = typeof(LandVehicle).GetMethod("SendOwnedColor", BindingFlags.Public | BindingFlags.Instance);
                if (target == null) { Core.Log.Error("[Tuning] LandVehicle.SendOwnedColor not found - a repaint at Marco's keeps the custom paint on top."); return; }
                h.Patch(target, postfix: new HarmonyMethod(typeof(RepaintPatch).GetMethod(nameof(Postfix), BindingFlags.NonPublic | BindingFlags.Static)));
                Core.Log.Msg("[Tuning] repaint hook on " + target.Name + ".");
            }
            catch (Exception e) { Core.Log.Error("[Tuning] patching the repaint failed - a repaint at Marco's keeps the custom paint on top: " + e.Message); }
        }

        private static void Postfix(LandVehicle __instance)
        {
            try
            {
                bool any = false;
                foreach (string key in new[] { "paint", "paint2" })
                {
                    if (State.Get(__instance, key) == null) continue;
                    any = true;
                    State.Request(__instance, key, "", "repainted at the shop");
                }
                if (!any) Core.Log.Msg("[Tuning] " + Registry.Name(__instance) + " repainted at the shop; it had no custom paint.");
            }
            catch (Exception e) { Core.Log.Error("[Tuning] clearing the custom paint after a repaint failed: " + e.Message); }
        }
    }
}
