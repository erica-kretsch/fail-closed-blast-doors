using Assets.Scripts;
using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.Objects.Structures;
using HarmonyLib;
using UnityEngine;

namespace FailClosedBlastDoors.Patches
{
    /// <summary>
    /// Refuses a logic close that the accumulator cannot cover. Blast doors have no keypad, so
    /// logic is the only way to operate them (the crowbar path is deliberately not gated).
    ///
    /// A refused close is stored in <c>Setting</c>, the door's desired state: it reads 0 while
    /// the door is open. <c>Error</c> goes to 1, and the tick closes the door once the
    /// accumulator can cover the distance.
    /// </summary>
    [HarmonyPatch(typeof(Door), nameof(Door.SetLogicValue))]
    internal static class CloseGatePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Door __instance, LogicType logicType, double value)
        {
            if (logicType != LogicType.Open
                || !GameManager.RunSimulation
                || !BlastDoorApi.IsBlastDoor(__instance)
                || !__instance.IsStructureCompleted)
            {
                return true;
            }

            // Mirror Device.SetLogicValue's own clamp-and-truncate.
            var state = (int)Mathf.Clamp((float)value, 0f, 1f);
            if (state != 0 || !BlastDoorTracker.IsOpenNow(__instance))
            {
                return true;
            }

            var anim = __instance.doorAnimComponent;
            var data = BlastDoorTracker.Get(__instance);
            var t = anim != null ? anim.t : 1f;
            if (data.Charge >= t - BlastDoorTracker.Epsilon)
            {
                return true;
            }

            __instance.Setting = 0;
            BlastDoorTracker.SetError(data, true);
            return false;
        }
    }

    /// <summary>
    /// Reports the refused close through the Error logic value even on a prefab with no error
    /// state, where assigning <c>Thing.Error</c> does nothing.
    /// </summary>
    [HarmonyPatch(typeof(Door), nameof(Door.CanLogicRead))]
    internal static class ErrorCanReadPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Door __instance, LogicType logicType, ref bool __result)
        {
            if (logicType == LogicType.Error && BlastDoorApi.IsBlastDoor(__instance))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.GetLogicValue))]
    internal static class ErrorGetValuePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Door __instance, LogicType logicType, ref double __result)
        {
            if (logicType == LogicType.Error
                && BlastDoorTracker.TryGet(__instance, out var data)
                && data.ErrorFlag)
            {
                __result = 1.0;
            }
        }
    }
}
