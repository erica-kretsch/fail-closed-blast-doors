using System;
using System.Collections.Concurrent;
using System.Threading;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Structures;

namespace FailClosedBlastDoors
{
    /// <summary>What a blast door is doing, for mods that charge power by activity.</summary>
    public enum BlastDoorState
    {
        /// <summary>The door is not a registered blast door.</summary>
        NotBlastDoor,

        /// <summary>
        /// Not moving and not recharging. Also reported while the door is moved without power
        /// (a crowbar), since nothing is drawing power then.
        /// </summary>
        Standby,

        /// <summary>Opening under power. The motor runs and the accumulator charges with it.</summary>
        Opening,

        /// <summary>Open and powered with the accumulator not yet full (after a manual open).</summary>
        Recharging,

        /// <summary>Closing. This is hydraulic release, so there is no motor load.</summary>
        Closing,
    }

    /// <summary>
    /// The public surface of Fail-Closed Blast Doors: which prefabs it applies to, and what a
    /// door is doing. Everything here is safe to call from any thread: the state is computed
    /// on the main thread each frame and cached, and the readers only read the cache.
    /// </summary>
    public static class BlastDoorApi
    {
        private static readonly ConcurrentDictionary<string, byte> Prefabs =
            new ConcurrentDictionary<string, byte>();

        static BlastDoorApi()
        {
            Register("StructureBlastDoor");
        }

        /// <summary>
        /// Applies the mod's behaviour to every door built from this prefab. The prefab must be
        /// a <see cref="Door"/> whose animation is a <c>BinaryAnimComponent</c>, as the blast
        /// door's is. Call it during plugin Awake or later; it can be repeated safely.
        /// </summary>
        public static void Register(string prefabName)
        {
            if (!string.IsNullOrEmpty(prefabName))
            {
                Prefabs[prefabName] = 0;
            }
        }

        public static bool IsRegistered(string prefabName)
        {
            return prefabName != null && Prefabs.ContainsKey(prefabName);
        }

        /// <summary>True when this thing was built from a registered prefab.</summary>
        public static bool IsBlastDoor(Thing thing)
        {
            return thing != null && IsRegistered(thing.PrefabName);
        }

        public static BlastDoorState GetState(Door door)
        {
            if (!IsBlastDoor(door))
            {
                return BlastDoorState.NotBlastDoor;
            }
            return BlastDoorTracker.TryGet(door, out var data) ? data.State : BlastDoorState.Standby;
        }

        /// <summary>The accumulator's stored energy from 0 (empty) to 1 (full).</summary>
        public static float GetCharge(Door door)
        {
            return BlastDoorTracker.TryGet(door, out var data) ? data.Charge : 0f;
        }

        /// <summary>How far open the door is, 0 (closed) to 1 (open).</summary>
        public static float GetOpenFraction(Door door)
        {
            return BlastDoorTracker.TryGet(door, out var data) ? data.OpenFraction : 0f;
        }

        /// <summary>
        /// Raised on the main thread, on the host or single player only, when a door's state
        /// changes: (door, old state, new state).
        /// </summary>
        public static event Action<Door, BlastDoorState, BlastDoorState> StateChanged;

        internal static void RaiseStateChanged(Door door, BlastDoorState from, BlastDoorState to)
        {
            Volatile.Read(ref StateChanged)?.Invoke(door, from, to);
        }
    }
}
