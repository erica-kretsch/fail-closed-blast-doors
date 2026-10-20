using System.Runtime.CompilerServices;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Structures;
using HarmonyLib;

namespace FailClosedBlastDoors.Patches
{
    /// <summary>
    /// Opens a powered blast door slowly and leaves every other move at the prefab's speed.
    ///
    /// <c>InteractableAnimComponent.time</c> is one number for both directions:
    /// <c>BinaryAnimComponent.Animate</c> adds <c>dt / time</c> opening and subtracts it
    /// closing, re-reading it every frame. <c>RefreshState</c> starts every animation, so
    /// setting <c>time</c> just before it picks the speed for that move. A manual (unpowered)
    /// open and every close use the prefab's own value, captured the first time we see it.
    /// </summary>
    [HarmonyPatch(typeof(InteractableAnimComponent), nameof(InteractableAnimComponent.RefreshState))]
    internal static class AnimationSpeedPatch
    {
        private static readonly ConditionalWeakTable<InteractableAnimComponent, StrongBox<float>>
            OriginalTimes = new ConditionalWeakTable<InteractableAnimComponent, StrongBox<float>>();

        [HarmonyPrefix]
        private static void Prefix(InteractableAnimComponent __instance)
        {
            if (!(__instance is DoorAnimComponent))
            {
                return;
            }
            var thing = __instance.parentThing != null
                ? __instance.parentThing
                : __instance.GetComponentInParent<Thing>();
            if (!(thing is Door door) || !BlastDoorApi.IsBlastDoor(door))
            {
                return;
            }

            BlastDoorTracker.Get(door);
            var original = OriginalTimes.GetValue(__instance, a => new StrongBox<float>(a.time));
            var opening = __instance.InteractableState == 1;
            // RefreshState also runs for changes that start no animation, such as power
            // dropping mid-open. An open already under way keeps the speed it began at;
            // re-deciding it would snap the rest of a slow open to vanilla speed.
            if (opening && __instance is BinaryAnimComponent binary
                && binary.CurrentState == BinaryAnimState.OffToOn)
            {
                return;
            }
            __instance.time = opening && door.Powered ? Settings.OpenSeconds.Value : original.Value;
        }
    }
}
