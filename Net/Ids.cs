
namespace Chassis.Net
{
    /// <summary>FishNet object ids: the name a vehicle or a player has on every peer.</summary>
    internal static class Ids
    {
        internal static int Of(Component c)
        {
            if (c == null) return -1;
            var nob = c.GetComponent<FishNetObject>();
            return nob != null ? nob.ObjectId : -1;
        }

        internal static T Find<T>(int objectId) where T : Component
        {
            if (objectId < 0) return null;
            foreach (var nob in UnityEngine.Object.FindObjectsOfType<FishNetObject>())
            {
                if (nob != null && nob.ObjectId == objectId)
                {
                    var t = nob.GetComponent<T>();
                    if (t != null) return t;
                }
            }
            return null;
        }
    }
}
