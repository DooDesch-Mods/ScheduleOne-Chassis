#if DEBUG
using System;
using System.Collections.Generic;
using Chassis.Api;
using Chassis.Interior;
using Chassis.Tune;
using Chassis.Vehicles;

namespace Chassis.Debugging
{
    /// <summary>
    /// <c>chassis tune ...</c>: the tuning of the car the local player sits in, or else the nearest one within 15 m
    /// ("#id" before the command picks a car by network id at any distance). Goes through the public API, so a client
    /// is checked by the host like any other request.
    /// </summary>
    internal static class TuneCommands
    {
        internal static void Help()
        {
            Say("tune [#id]                   - the car's tuning keys and driving factors");
            Say("tune parts [slot]            - parts that fit the car");
            Say("tune part <id>               - install a part (replaces the slot)");
            Say("tune remove <slot>           - stock part in that slot");
            Say("tune set <key> <value>       - any key: paint C81E1E,metallic | rims 202020,chrome | xenon 9FB8FF | neon 00FFAA,lr | tint 10121480 | smoke FF2080 | horn <sound>");
            Say("tune unset <key>             - remove one key");
            Say("tune clear                   - everything back to stock");
            Say("tune preview <key> <value>   - show a key on this machine only (empty value: removed)");
            Say("tune endpreview [key]        - end one preview, or all");
            Say("tune stats                   - stock and current driving values");
            Say("tune demo                    - register demo parts (wing, bumper, exhaust, rims) fitted to this car's code");
            Say("tune marco open|<colour>|close - Marco's repaint menu for the car; a colour is picked and bought through the menu's own buttons");
            Say("tune shine [hexmask]         - chrome probe layers nearby, or render the probes with this layer mask");
        }

        internal static void Run(string[] p)
        {
            int at = 2;
            string target = "";
            if (p.Length > at && p[at].StartsWith("#")) { target = p[at].Substring(1); at++; }
            string op = p.Length > at ? p[at].ToLowerInvariant() : "";
            string a1 = p.Length > at + 1 ? p[at + 1] : "";
            string rest = p.Length > at + 2 ? string.Join(" ", p, at + 2, p.Length - at - 2) : "";
            if (op == "help") { Help(); return; }

            // "#id" wins over the car the player sits in, so one car can be tuned from inside another.
            LandVehicle lv = null;
            if (target.Length > 0 && int.TryParse(target, out int netId))
            {
                foreach (var v in Registry.All) if (v != null && Net.Ids.Of(v) == netId) { lv = v; break; }
                if (lv == null) { Say("tune: no car with net id " + target); return; }
            }
            else lv = VehicleCommands.Target(target);
            if (lv == null) { Say("tune: no car - sit in one, stand within 15 m, or give #<net id>"); return; }
            switch (op)
            {
                case "":
                    foreach (string line in Tune.Apply.Describe(lv).Split('\n')) Say(line);
                    var targets = Tune.Paint.TargetsOf(lv, out bool own);
                    Say("paint targets: " + targets.Count + " slot(s), " + targets.FindAll(t => t.Secondary).Count + " of them second paint, " + (own ? "set by a mod" : "the vanilla body"));
                    break;
                case "parts":
                    var list = ChassisApi.TuningParts(lv, a1.Length > 0 ? a1 : null);
                    Say("tune parts: " + list.Count + " fit " + ChassisApi.CodeOf(lv) + (a1.Length > 0 ? " in slot " + a1 : ""));
                    foreach (var part in list) Say("  " + part.Id + " [" + part.Slot + "] " + part.Label + ", " + (part.Build != null ? "model, " : "") + (part.Stats != null ? part.Stats.ToString() : "looks only"));
                    break;
                case "part":
                    Say("tune part: " + (ChassisApi.InstallPart(lv, a1) ? "asked for " + a1 : "refused, see the log"));
                    break;
                case "remove":
                    Say("tune remove: " + (ChassisApi.RemovePart(lv, a1) ? "asked for slot " + a1 : "refused, see the log"));
                    break;
                case "set":
                    Say("tune set: " + (ChassisApi.SetTuning(lv, a1, rest) ? "asked for " + a1 + " = " + rest : "refused, see the log"));
                    break;
                case "unset":
                    Say("tune unset: " + (ChassisApi.SetTuning(lv, a1, "") ? "asked to remove " + a1 : "refused, see the log"));
                    break;
                case "clear":
                    Say("tune clear: " + (ChassisApi.ClearTuning(lv) ? "asked" : "refused, see the log"));
                    break;
                case "preview":
                    Say("tune preview: " + (ChassisApi.PreviewTuning(lv, a1, rest) ? "showing " + a1 + " = '" + rest + "' here" : "refused, see the log"));
                    break;
                case "endpreview":
                    Say("tune endpreview: " + (ChassisApi.EndPreview(lv, a1.Length > 0 ? a1 : null) ? "ended" : "there was no preview"));
                    break;
                case "stats":
                    foreach (string line in Handling.Describe(lv).Split('\n')) Say(line);
                    break;
                case "demo":
                    Say("tune demo: " + Demo(lv));
                    break;
                case "marco":
                    Say("tune marco: " + Marco(lv, a1));
                    break;
                case "shine":
                    int? mask = null;
                    if (a1.Length > 0) mask = Convert.ToInt32(a1.StartsWith("0x") ? a1.Substring(2) : a1, 16);
                    Say("tune shine: " + Shine.Debug(mask));
                    break;
                default:
                    Say("tune: unknown '" + op + "', try 'chassis tune help'");
                    break;
            }
        }

        // ---- Marco's repaint ------------------------------------------------------------------------------------------

        /// <summary>
        /// Drives the vanilla repaint menu: <c>open</c> shows it for the car, a colour name goes through the menu's
        /// <c>ColorClicked</c> and <c>ConfirmButtonClicked</c> (the buy button), <c>close</c> closes it.
        /// </summary>
        private static string Marco(LandVehicle lv, string arg)
        {
            if (!Singleton<GameVehicleModMenu>.InstanceExists) return "the repaint menu does not exist in this scene";
            var menu = Singleton<GameVehicleModMenu>.Instance;
            string a = arg.ToLowerInvariant();
            if (a == "open") { menu.Open(lv); return "opened for " + Registry.Name(lv) + ", owned colour " + lv.OwnedColor; }
            if (a == "close") { menu.Close(); return "closed, owned colour " + lv.OwnedColor; }
            if (!Enum.TryParse(arg, true, out GameVehicleColor col)) return "unknown colour '" + arg + "' - " + string.Join(", ", Enum.GetNames(typeof(GameVehicleColor)));
            if (!menu.IsOpen || !Chassis.Compat.Interop.Same(menu.currentVehicle, lv)) return "open the menu for " + Registry.Name(lv) + " first ('chassis tune marco open')";
            menu.ColorClicked(col);
            menu.ConfirmButtonClicked();
            return "bought " + col + " for " + Registry.Name(lv) + ", owned colour now " + lv.OwnedColor;
        }

        // ---- demo parts --------------------------------------------------------------------------------------------

        /// <summary>Primitive parts placed from the body's bounds of this car, to test the paths without a mod.</summary>
        private static string Demo(LandVehicle lv)
        {
            string code = ChassisApi.CodeOf(lv);
            if (!Body.Measure(lv, out Bounds b)) return "no body mesh to measure on " + Registry.Name(lv);
            float w = b.size.x, deck = Deck(lv, b, b.min.z + 0.35f);

            var wing = Part("demo_wing", "spoiler", "Demo Wing", null);
            wing.Fits[code] = new PartFit { Position = new Vector3(b.center.x, deck, b.min.z + 0.25f) };
            wing.Build = v => Model("demo_wing", m =>
            {
                m.Box(new Vector3(0f, 0.22f, 0f), new Vector3(w * 0.85f, 0.03f, 0.3f));
                m.Box(new Vector3(-w * 0.3f, 0.1f, 0.05f), new Vector3(0.04f, 0.22f, 0.12f));
                m.Box(new Vector3(w * 0.3f, 0.1f, 0.05f), new Vector3(0.04f, 0.22f, 0.12f));
            }, "paint2_demo");
            wing.Stats = new TuningStats { Downforce = 1.5f };

            var bumper = Part("demo_bumper", "bumper_front", "Demo Bumper", null);
            float by = b.min.y + 0.28f;
            bumper.Fits[code] = new PartFit
            {
                Position = new Vector3(b.center.x, by, b.max.z - 0.1f),
                Cut = { new Bounds(new Vector3(b.center.x, by, b.max.z - 0.08f), new Vector3(w * 0.7f, 0.18f, 0.2f)) },
            };
            bumper.Build = v => Model("demo_bumper", m => m.Box(Vector3.zero, new Vector3(w * 1.02f, 0.2f, 0.3f)), "paint2_demo");

            var exhaust = Part("demo_exhaust", "exhaust", "Demo Exhaust", null);
            exhaust.Fits[code] = new PartFit { Position = new Vector3(b.max.x - 0.35f, b.min.y + 0.12f, b.min.z + 0.1f) };
            exhaust.Build = v => Model("demo_exhaust", m => m.Column(0.05f, -0.35f, 0f, 10), "demo_chrome");

            var rim = Part("demo_rim", "wheels", "Demo Rim", null);
            rim.Build = v => Model("demo_rim", m =>
            {
                m.Ring(0.42f, 0.06f, 20, 6);
                for (int i = 0; i < 5; i++)
                    m.Box(Vector3.zero, new Vector3(0.82f, 0.08f, 0.04f), Quaternion.Euler(0f, 0f, i * 36f), Vector3.zero);
                m.Disc(0f, 0f, 0.03f, 0.12f, 10, true);
            }, "rim_demo", Quaternion.Euler(0f, 90f, 0f));

            foreach (var part in new[] { wing, bumper, exhaust, rim }) ChassisApi.RegisterTuningPart(part);
            return "registered demo_wing (spoiler), demo_bumper (bumper_front, paint2, cuts the stock bumper), demo_exhaust, demo_rim (wheels) for '" + code + "', deck at y " + Tune.Apply.F(deck) + "; install with 'chassis tune part <id>'";
        }

        private static TuningPart Part(string id, string slot, string label, TuningStats stats)
            => new TuningPart { Id = id, Slot = slot, Label = label, Stats = stats };

        /// <summary>Height of the car's top surface at this depth, from its colliders; the body's middle if none is hit.</summary>
        private static float Deck(LandVehicle lv, Bounds b, float z)
        {
            Vector3 from = lv.transform.TransformPoint(new Vector3(b.center.x, b.max.y + 1f, z));
            foreach (var hit in Physics.RaycastAll(from, -lv.transform.up, b.size.y + 2f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null || !hit.collider.transform.IsChildOf(lv.transform)) continue;
                return lv.transform.InverseTransformPoint(hit.point).y;
            }
            Core.Log.Warning("[Tuning] demo: no collider of " + Registry.Name(lv) + " under the rear deck - the wing sits at the body's middle.");
            return b.center.y;
        }

        private static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();

        private static GameObject Model(string name, Action<KitMesh> shape, string material, Quaternion? turn = null)
        {
            var root = new GameObject(name);
            var child = new GameObject(name + "_mesh");
            child.transform.SetParent(root.transform, false);
            if (turn.HasValue) child.transform.localRotation = turn.Value;
            var m = new KitMesh();
            shape(m);
            child.AddComponent<MeshFilter>().sharedMesh = m.Build(name);
            child.AddComponent<MeshRenderer>().sharedMaterial = Mat(material);
            return root;
        }

        private static Material Mat(string name)
        {
            if (_mats.TryGetValue(name, out var m) && m != null) return m;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) { Core.Log.Error("[Tuning] demo: URP/Lit shader not found."); shader = Shader.Find("Standard"); }
            m = new Material(shader) { name = name };
            m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            bool chrome = name.Contains("chrome");
            m.SetColor("_BaseColor", chrome ? new Color(0.8f, 0.8f, 0.82f) : new Color(0.15f, 0.15f, 0.15f));
            m.SetFloat("_Metallic", chrome ? 1f : 0.3f);
            m.SetFloat("_Smoothness", chrome ? 0.9f : 0.5f);
            _mats[name] = m;
            return m;
        }

        private static void Say(string line) => DebugConsolePatch.Say(line);
    }
}
#endif
