using System;
using System.Globalization;
using Chassis.Instruments;
using Chassis.Seating;
using Chassis.Tune;
using Chassis.Vehicles;

namespace Chassis.Net
{
    /// <summary>
    /// What travels over <see cref="ChassisNet"/>. Requests go to the host, which checks them and answers with state
    /// for every peer:
    ///   req.drive vehicle sign        -> drive vehicle sign           (driver's stroke direction for the hands)
    ///   req.honk vehicle player flash -> honk vehicle                 (host: player sits inside or owns it)
    ///   req.blink vehicle side        -> blink vehicle side           (-1 left, 1 right, 0 off)
    ///   req.part vehicle name 1|0     -> part vehicle name 1|0        (lid held open)
    ///   req.side vehicle 1|0          -> side vehicle 1|0             (storage opened at the front)
    ///   req.lock vehicle owner 1|0    -> chirp vehicle 1|0, own ...   (host: only the owner)
    ///   req.bought buyer code         -> own ...                      (the next new vehicle of that code is the buyer's)
    ///   req.pedal vehicle thr hb      -> pedal vehicle thr hb         (driver's throttle and handbrake, Pedals)
    ///   req.ctl vehicle name player v -> ctl vehicle name v player    (a switch was used, Controls)
    ///   req.trip vehicle              -> odo ...                      (trip back to 0)
    ///                                    odo id,odo,trip;...           (host: odometers once a second, Odometer)
    ///   req.tune vehicle player k v   -> tune vehicle k v             (host: only the owner, Tune.State)
    ///                                    tunes vehicle k=v;k=v         (host: a car's whole tuning)
    ///   req.sync player               -> own, blink, part, side, odo, ctls, tunes (a client that joined late)
    /// Ids are FishNet object ids; numbers are invariant culture.
    /// </summary>
    internal static class ChassisSync
    {
        internal static void Init()
        {
            ChassisNet.OnRequest = OnRequest;
            ChassisNet.OnState = OnState;
        }

        private static int I(string s) => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        private static float Fl(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        private static void OnRequest(ChassisNet.Msg m)
        {
            switch (m.Op)
            {
                case "req.drive": ChassisNet.ToAll("drive", m.Arg(0), m.Arg(1)); break;
                case "req.honk": Horn.HostHonk(I(m.Arg(0)), I(m.Arg(1)), m.Arg(2) == "1"); break;
                case "req.blink": ChassisNet.ToAll("blink", m.Arg(0), Indicators.Clean(I(m.Arg(1))).ToString(CultureInfo.InvariantCulture)); break;
                case "req.part": ChassisNet.ToAll("part", m.Arg(0), m.Arg(1), m.Arg(2) == "1" ? "1" : "0"); break;
                case "req.side": ChassisNet.ToAll("side", m.Arg(0), m.Arg(1) == "1" ? "1" : "0"); break;
                case "req.lock": Fleet.HostLock(I(m.Arg(0)), m.Arg(1), m.Arg(2) == "1"); break;
                case "req.bought": Fleet.Announce(m.Arg(0), m.Arg(1)); break;
                case "req.pedal": ChassisNet.ToAll("pedal", m.Arg(0), m.Arg(1), m.Arg(2) == "1" ? "1" : "0"); break;
                case "req.ctl": Controls.HostUse(I(m.Arg(0)), m.Arg(1), I(m.Arg(2)), Fl(m.Arg(3))); break;
                case "req.trip": Controls.HostTrip(I(m.Arg(0))); break;
                case "req.tune": State.HostRequest(I(m.Arg(0)), m.Arg(1), m.Arg(2), m.Arg(3)); break;
                case "req.sync":
                    Core.Log.Msg("[Net] " + m.Arg(0) + " asked for the current vehicle state.");
                    Fleet.Broadcast(true);
                    Indicators.HostReplay();
                    Parts.HostReplay();
                    Odometer.Broadcast(true);
                    Controls.HostReplay();
                    State.HostReplay();
                    break;
                default: Core.Log.Warning("[Net] unknown request " + m + ", dropped."); break;
            }
        }

        private static void OnState(ChassisNet.Msg m)
        {
            switch (m.Op)
            {
                case "drive": Occupants.OnDrive(I(m.Arg(0)), Fl(m.Arg(1))); break;
                case "honk": Horn.OnHonk(I(m.Arg(0))); break;
                case "chirp": Horn.OnChirp(I(m.Arg(0)), m.Arg(1) == "1"); break;
                case "blink": Indicators.OnBlink(I(m.Arg(0)), I(m.Arg(1))); break;
                case "part": Parts.OnPart(I(m.Arg(0)), m.Arg(1), m.Arg(2) == "1"); break;
                case "side": Parts.OnStorageEnd(I(m.Arg(0)), m.Arg(1) == "1"); break;
                case "own": Fleet.ApplyState(m.Arg(0)); break;
                case "pedal": Pedals.OnPedal(I(m.Arg(0)), Fl(m.Arg(1)), m.Arg(2) == "1"); break;
                case "odo": Odometer.OnOdo(m.Arg(0)); break;
                case "ctl": Controls.OnUse(I(m.Arg(0)), m.Arg(1), Fl(m.Arg(2)), I(m.Arg(3))); break;
                case "ctls": Controls.OnStates(m.Arg(0)); break;
                case "tune": State.OnTune(I(m.Arg(0)), m.Arg(1), m.Arg(2)); break;
                case "tunes": State.OnTunes(I(m.Arg(0)), m.Arg(1)); break;
                default: Core.Log.Warning("[Net] unknown state " + m + ", dropped."); break;
            }
        }
    }
}
