using System;
using System.Linq;
using System.Reflection;
using Chassis.Compat;
using Chassis.Net;
using Chassis.Vehicles;
using HarmonyLib;

namespace Chassis.Tune
{
    /// <summary>
    /// Free paint colour and finish on the body. Vanilla paints one material slot per <c>VehicleColor.BodyMeshes</c>
    /// entry with one of 16 library colours; Chassis puts its own material copy into those slots and puts it back after
    /// every vanilla <c>ApplyColor</c>, except while Marco's repaint menu previews a colour on this car. Buying a repaint
    /// there clears the custom paint (<see cref="RepaintPatch"/>).
    /// </summary>
    internal static class Paint
    {
        internal sealed class Slot
        {
            internal MeshRenderer R;
            internal int Index;
            internal Material Original;
            internal Material Mine;
        }

        internal static void Apply(Tune.Apply.Car c, string value)
        {
            string key = value ?? "";
            if (key == c.PaintKey) return;
            var vc = c.V.Color;
            if (vc == null || vc.BodyMeshes == null) { c.PaintKey = key; Core.Log.Warning("[Tuning] " + Registry.Name(c.V) + " has no paintable body - paint ignored."); return; }
            if (value == null)
            {
                Restore(c);
                c.PaintKey = key;
                return;
            }
            Values.Paint(value, out Color colour, out string finish);
            Values.Finish(finish, out float metallic, out float smooth);
            foreach (var bm in vc.BodyMeshes)
            {
                if (bm == null || bm.Renderer == null) continue;
                Slot slot = null;
                foreach (var s in c.PaintSlots) if (Interop.Same(s.R, bm.Renderer) && s.Index == bm.MaterialIndex) { slot = s; break; }
                if (slot == null)
                {
                    var mats = bm.Renderer.sharedMaterials;
                    if (bm.MaterialIndex < 0 || bm.MaterialIndex >= mats.Length || mats[bm.MaterialIndex] == null)
                    {
                        Core.Log.Warning("[Tuning] " + Registry.Name(c.V) + ": paint slot " + bm.MaterialIndex + " of " + bm.Renderer.name + " does not exist - skipped.");
                        continue;
                    }
                    slot = new Slot { R = bm.Renderer, Index = bm.MaterialIndex, Original = mats[bm.MaterialIndex] };
                    slot.Mine = new Material(slot.Original) { name = "Chassis_Paint" };
                    c.PaintSlots.Add(slot);
                }
                Colour(slot.Mine, colour, metallic, smooth);
                Put(slot);
            }
            c.PaintKey = key;
            Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + " painted " + Values.Hex(colour) + " " + finish + " on " + c.PaintSlots.Count + " slot(s).");
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
            foreach (var s in c.PaintSlots)
            {
                if (s.R == null) continue;
                var mats = s.R.sharedMaterials;
                if (s.Index < mats.Length) { mats[s.Index] = s.Original; s.R.sharedMaterials = mats; }
            }
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
            if (c.PaintSlots.Count == 0 || c.PaintKey.Length == 0 || Previewing(c.V)) return;
            foreach (var s in c.PaintSlots) Put(s);
        }

        /// <summary>Vanilla painted a car: put the custom paint back unless Marco is previewing.</summary>
        internal static void AfterVanilla(VehicleColor vc)
        {
            foreach (var c in Tune.Apply.Cars)
            {
                if (c.V == null || c.PaintSlots.Count == 0 || c.PaintKey.Length == 0 || !Interop.Same(c.V.Color, vc)) continue;
                if (Previewing(c.V)) return;
                foreach (var s in c.PaintSlots) Put(s);
                return;
            }
        }

        /// <summary>The body colour the car shows now (custom or vanilla), for parts painted like the body.</summary>
        internal static Color BodyColour(LandVehicle v, out float metallic, out float smooth)
        {
            metallic = 0.1f; smooth = 0.6f;
            string p = State.GetShown(v, "paint");
            if (p != null && Values.Paint(p, out Color c, out string finish)) { Values.Finish(finish, out metallic, out smooth); return c; }
            var vc = v.Color;
            if (vc != null && vc.BodyMeshes != null)
                foreach (var bm in vc.BodyMeshes)
                {
                    if (bm?.Renderer == null) continue;
                    var mats = bm.Renderer.sharedMaterials;
                    if (bm.MaterialIndex >= 0 && bm.MaterialIndex < mats.Length && mats[bm.MaterialIndex] != null) return mats[bm.MaterialIndex].color;
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
