using System;
#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
#endif

namespace Chassis.Compat
{
    /// <summary>
    /// The few calls that differ between the IL2CPP interop and plain Mono: casts, runtime type names, game arrays and
    /// FishNet's optional despawn type.
    /// </summary>
    internal static class Interop
    {
        /// <summary>Runtime cast of a Unity object; null when it is not a <typeparamref name="T"/>.</summary>
        internal static T As<T>(this UnityEngine.Object o) where T : UnityEngine.Object
        {
            if (o == null) return null;
#if IL2CPP
            return o.TryCast<T>();
#else
            return o as T;
#endif
        }

        /// <summary>True when both wrappers stand for the same game object (IL2CPP wraps one object many times).</summary>
        internal static bool Same(UnityEngine.Object a, UnityEngine.Object b)
        {
            if (a == null || b == null) return false;
#if IL2CPP
            return a.Pointer == b.Pointer;
#else
            return ReferenceEquals(a, b);
#endif
        }

        /// <summary>Name of the object's game-side type (the IL2CPP class, not the interop wrapper).</summary>
        internal static string TypeName(UnityEngine.Object o, bool full = false)
        {
            if (o == null) return "null";
#if IL2CPP
            var t = o.GetIl2CppType();
#else
            var t = o.GetType();
#endif
            return full ? t.FullName : t.Name;
        }

#if IL2CPP
        /// <summary>A game-side array field value from managed elements.</summary>
        internal static Il2CppReferenceArray<T> Arr<T>(params T[] items) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
            => new Il2CppReferenceArray<T>(items);
#else
        /// <summary>A game-side array field value from managed elements.</summary>
        internal static T[] Arr<T>(params T[] items) => items;
#endif

        /// <summary>Copies a GPU buffer back into managed arrays, so a mesh that is not readable on the CPU can still be read.</summary>
        internal static float[] ReadFloats(GraphicsBuffer buffer, int count)
        {
#if IL2CPP
            var data = new Il2CppStructArray<float>(count);
            // GetData's unstripped body asks the array for its element type through a method the interop lacks; the
            // native call behind it takes the element size directly.
            buffer.InternalGetData(new Il2CppSystem.Array(data.Pointer), 0, 0, count, 4);
            return data;
#else
            var data = new float[count];
            buffer.GetData(data);
            return data;
#endif
        }

        /// <summary>An index buffer read back, widened to int.</summary>
        internal static int[] ReadIndices(GraphicsBuffer buffer, int count, bool wide)
        {
            var r = new int[count];
#if IL2CPP
            if (wide) { var d = new Il2CppStructArray<int>(count); buffer.InternalGetData(new Il2CppSystem.Array(d.Pointer), 0, 0, count, 4); for (int i = 0; i < count; i++) r[i] = d[i]; }
            else { var d = new Il2CppStructArray<ushort>(count); buffer.InternalGetData(new Il2CppSystem.Array(d.Pointer), 0, 0, count, 2); for (int i = 0; i < count; i++) r[i] = d[i]; }
#else
            if (wide) buffer.GetData(r);
            else { var d = new ushort[count]; buffer.GetData(d); for (int i = 0; i < count; i++) r[i] = d[i]; }
#endif
            return r;
        }

        /// <summary>Server despawn without a despawn type, as FishNet's optional parameter.</summary>
        internal static void Despawn(GameObject go)
        {
#if IL2CPP
            InstanceFinder.ServerManager.Despawn(go, new Il2CppSystem.Nullable<Il2CppFishNet.Object.DespawnType>());
#else
            InstanceFinder.ServerManager.Despawn(go, null);
#endif
        }
    }
}
