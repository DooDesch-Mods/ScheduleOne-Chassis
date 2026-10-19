using System;
using System.Globalization;
using Chassis.Rideables;
using Chassis.Seating;

namespace Chassis.Net
{
    /// <summary>
    /// What travels over <see cref="ChassisNet"/>. Requests go to the host, which checks them and answers with state
    /// for every peer:
    ///   req.sit key player            -> swap key vehicle player      (prop becomes a vehicle, player enters)
    ///                                    unswap key x y z yaw          (vehicle empty, prop comes back)
    ///   req.drive vehicle sign        -> drive vehicle sign           (driver's stroke direction for the hands)
    ///   req.push vehicle player 1|0   -> push vehicle player|-1       (who pushes)
    ///   req.pushin vehicle fwd steer  -> pushin vehicle fwd steer     (pusher input, added on the driver's peer)
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
                case "req.sit": PropSwap.HostSit(m.Arg(0), I(m.Arg(1))); break;
                case "req.drive": ChassisNet.ToAll("drive", m.Arg(0), m.Arg(1)); break;
                case "req.push": Push.HostRequest(I(m.Arg(0)), I(m.Arg(1)), m.Arg(2) == "1"); break;
                case "req.pushin": Push.HostInput(I(m.Arg(0)), new Vector2(Fl(m.Arg(1)), Fl(m.Arg(2)))); break;
                default: Core.Log.Warning("[Net] unknown request " + m + ", dropped."); break;
            }
        }

        private static void OnState(ChassisNet.Msg m)
        {
            switch (m.Op)
            {
                case "swap": PropSwap.OnSwap(m.Arg(0), I(m.Arg(1)), I(m.Arg(2))); break;
                case "unswap":
                    PropSwap.Parse(m.Args, out Vector3 pos, out float yaw);
                    PropSwap.OnUnswap(m.Arg(0), pos, yaw);
                    break;
                case "drive": Occupants.OnDrive(I(m.Arg(0)), Fl(m.Arg(1))); break;
                case "push": Push.OnPush(I(m.Arg(0)), I(m.Arg(1))); break;
                case "pushin": Push.OnInput(I(m.Arg(0)), new Vector2(Fl(m.Arg(1)), Fl(m.Arg(2)))); break;
                default: Core.Log.Warning("[Net] unknown state " + m + ", dropped."); break;
            }
        }
    }
}
