using Assets.Scripts;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Structures;
using HarmonyLib;

namespace FailClosedBlastDoors.Patches
{
    /// <summary>
    /// Keeps <c>Setting</c> equal to the door's open state on every open or close. Vanilla only
    /// does this in the keypad's Logic mode, and blast doors have no keypad, so without it
    /// <c>Setting</c> would sit at its default of 0 on an open door, which is how a refused
    /// close is recorded. Any real open or close, including by crowbar, overwrites it and so
    /// cancels a pending close.
    /// </summary>
    [HarmonyPatch(typeof(Door), nameof(Door.OnInteractableUpdated))]
    internal static class SettingSyncPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Door __instance, Interactable interactable)
        {
            if (!GameManager.RunSimulation
                || interactable.Action != InteractableType.Open
                || !BlastDoorApi.IsBlastDoor(__instance))
            {
                return;
            }
            if (__instance.Setting != interactable.State)
            {
                __instance.Setting = interactable.State;
            }
        }
    }
}
