using Assets.Scripts.Objects.Structures;
using HarmonyLib;
using UnityEngine;

namespace FailClosedBlastDoors.Patches
{
    /// <summary>
    /// Shows the recharge colour on the status lights while an open door recharges. Vanilla
    /// calls <c>ToggleStatusLight</c> from several places (animation end, lock and power
    /// changes) and each would otherwise put its own colour back.
    ///
    /// The lights take any colour, but the emissive panel's material changer only knows
    /// vanilla's, so it shows its plain "powered" look. The charge is the host's, so a client
    /// never sees this colour.
    /// </summary>
    [HarmonyPatch(typeof(Door), nameof(Door.ToggleStatusLight))]
    internal static class StatusLightPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Door __instance, bool toggle, ref Color color)
        {
            if (toggle
                && BlastDoorTracker.TryGet(__instance, out var data)
                && BlastDoorTracker.Compute(data) == BlastDoorState.Recharging)
            {
                color = Settings.RechargeColor.Value;
            }
        }
    }
}
