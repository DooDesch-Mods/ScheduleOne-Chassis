#if SNITCH
using Snitch.Api;
using Chassis.Vehicles;

namespace Chassis.Profiling
{
    /// <summary>DEBUG-only Snitch panel. The Snitch host finds SnitchProbe.Register() and shows the panel in its
    /// overlay. Compiled only under SNITCH; the Release DLL contains no Snitch types. The per-subsystem timing sits in
    /// Core.Guard.</summary>
    internal static class SnitchProbe
    {
        public static void Register()
        {
            Panel p = Profiler.RegisterPanel("Chassis", "Chassis");
            p.Counter("Vehicles", () => Registry.Profiled.Count, "cars");
            p.Counter("CabinStates", () => CabinLight.Tracked, "cars");
            p.Counter("CabinGlowing", () => { CabinLight.Count(out int g, out _); return g; }, "lights");
            p.Counter("CabinPointLights", () => { CabinLight.Count(out _, out int l); return l; }, "lights");
            p.Log();
        }
    }
}
#endif
