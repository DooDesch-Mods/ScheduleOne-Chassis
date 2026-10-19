namespace Chassis.Vehicles
{
    /// <summary>Finds the transforms a profile names: the model frame under the vehicle and parts under the frame.</summary>
    internal static class Frames
    {
        /// <summary>
        /// The frame of a profile: the vehicle root for an empty path, the exact path below the root, or else the first
        /// transform anywhere below the root named like the path's last segment. Mods that move their model under a
        /// vanilla vehicle at run time cannot always give the exact path.
        /// </summary>
        internal static Transform Resolve(Transform root, string path)
        {
            if (root == null) return null;
            if (string.IsNullOrEmpty(path)) return root;
            var t = root.Find(path);
            if (t != null) return t;
            int slash = path.LastIndexOf('/');
            return Deep(root, slash >= 0 ? path.Substring(slash + 1) : path);
        }

        internal static Transform Deep(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == name) return t;
            return null;
        }

        /// <summary>"Light_IndicatorL" matches itself and Blender's "Light_IndicatorL.001".</summary>
        internal static bool InGroup(string name, string group)
        {
            if (name == null || string.IsNullOrEmpty(group)) return false;
            return name == group || (name.Length > group.Length && name[group.Length] == '.' && name.StartsWith(group, System.StringComparison.Ordinal));
        }
    }
}
