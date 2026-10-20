using Chassis.Api;
using Il2CppScheduleOne.Vehicles;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(ChassisCockpitExample.Core), "ChassisCockpitExample", "1.0.0", "DooDesch")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonAdditionalDependencies("Chassis")]

namespace ChassisCockpitExample
{
    /// <summary>
    /// Gives the Shitbox a cockpit of its own through Chassis: a dash panel, a speedometer with a needle, the
    /// odometer as digits and a beacon switch the driver clicks in first person. The parts are primitives so the
    /// example needs no AssetBundle; a real mod would instantiate its prefab in <see cref="Build"/> instead.
    /// </summary>
    public sealed class Core : MelonMod
    {
        private const string Code = "shitbox";
        private static Shader _lit;

        public override void OnInitializeMelon()
        {
            // A source of our own: the beacon switch's state, so a lamp gauge can show it.
            ChassisApi.RegisterGaugeSource("example.beacon", v => ChassisApi.ControlValue(v, "beacon"));

            // The action behind the beacon switch. It runs on every peer once the host accepted the click.
            ChassisApi.RegisterControlAction("example.beacon", use =>
                LoggerInstance.Msg("Beacon " + (use.Value > 0.5f ? "on" : "off") + " in " + use.Vehicle.vehicleCode
                    + (use.By != null ? ", switched by a player" : ", switched by a mod") + "."));

            ChassisApi.SetInterior(Code, Build, Configure);

            ChassisApi.HeadlightsChanged += (v, on) => LoggerInstance.Msg("Headlights " + (on ? "on" : "off") + " in " + v.vehicleCode + ".");
            LoggerInstance.Msg("Shitbox cockpit registered.");
        }

        /// <summary>
        /// The interior for one vehicle, in the frame's space (x right, y up, z forward, metres). Chassis parents it
        /// to the frame and keeps the local pose.
        /// </summary>
        private GameObject Build(LandVehicle vehicle)
        {
            var profile = ChassisApi.Get(Code);
            Vector3 eyes = profile != null && profile.Seats.Count > 0 ? profile.Seats[0].Eyes : new Vector3(-0.35f, 1.1f, 0f);

            var root = new GameObject("ExampleCockpit");
            var dash = Part("Dash", PrimitiveType.Cube, root.transform, new Vector3(eyes.x, eyes.y - 0.3f, eyes.z + 0.6f), new Vector3(0.5f, 0.2f, 0.05f), new Color(0.12f, 0.12f, 0.13f));

            // The dial faces the driver. The needle object sits at the dial centre and turns about its local -Z;
            // the visible bar is a child shifted up, so it swings around the centre.
            var dial = Part("Dial", PrimitiveType.Cylinder, dash.transform.parent, dash.transform.localPosition + new Vector3(0f, 0.02f, -0.03f), new Vector3(0.14f, 0.005f, 0.14f), new Color(0.9f, 0.9f, 0.85f));
            dial.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var needle = new GameObject("ExampleSpeedNeedle");
            needle.transform.SetParent(root.transform, false);
            needle.transform.localPosition = dial.transform.localPosition + new Vector3(0f, 0f, -0.008f);
            Part("Bar", PrimitiveType.Cube, needle.transform, new Vector3(0f, 0.03f, 0f), new Vector3(0.004f, 0.06f, 0.002f), new Color(1f, 0.35f, 0f));

            // Empty anchor for the odometer digits; Chassis puts the text on it.
            var odo = new GameObject("ExampleOdometer");
            odo.transform.SetParent(root.transform, false);
            odo.transform.localPosition = dial.transform.localPosition + new Vector3(0f, -0.04f, -0.01f);

            Part("ExampleBeaconSwitch", PrimitiveType.Cube, root.transform, dash.transform.localPosition + new Vector3(0.18f, 0f, -0.04f), new Vector3(0.02f, 0.035f, 0.015f), new Color(0.2f, 0.2f, 0.22f));
            Part("ExampleBeaconLamp", PrimitiveType.Sphere, root.transform, dash.transform.localPosition + new Vector3(0.18f, 0.05f, -0.03f), Vector3.one * 0.015f, new Color(0.3f, 0.05f, 0.05f));
            return root;
        }

        /// <summary>
        /// Names the parts the gauges and the switch move. One name is searched in the whole frame, so the names carry
        /// a prefix that no vanilla part has. Runs once per profile object of the code.
        /// </summary>
        private static void Configure(ChassisProfile p)
        {
            p.Gauges.Add(new GaugeProfile { Name = "speedo", Kind = "needle", Path = "ExampleSpeedNeedle", Source = "speed", Max = ChassisApi.UsesMph ? 80f : 130f });
            p.Gauges.Add(new GaugeProfile { Name = "odo", Kind = "digits", Path = "ExampleOdometer", Source = "odometer", Format = "0.0", Size = 0.012f, Color = Color.white });
            p.Gauges.Add(new GaugeProfile { Name = "beacon_lamp", Kind = "lamp", Path = "ExampleBeaconLamp", Source = "example.beacon", Color = Color.red });
            p.Controls.Add(new ControlProfile { Name = "beacon", Kind = "toggle", Path = "ExampleBeaconSwitch", Action = "example.beacon", Axis = Vector3.right, Angle = 30f });
        }

        private static GameObject Part(string name, PrimitiveType shape, Transform parent, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            // Primitives come with a collider; inside a car it would push against the car's own body.
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            if (_lit == null) _lit = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(_lit);
            mat.color = color;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }
    }
}
