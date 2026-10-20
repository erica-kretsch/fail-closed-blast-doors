using Assets.Scripts.Objects;
using HarmonyLib;

namespace FailClosedBlastDoors.Patches
{
    /// <summary>
    /// A <see cref="ThingDamageState"/> that also accepts stun. It holds no logic of its own:
    /// the stock state refuses stun on structures, and this is the smallest change that lets
    /// the tracker store the recharge deficit there, where the game saves and syncs it.
    /// </summary>
    internal sealed class StunAllowedDamageState : ThingDamageState
    {
        public StunAllowedDamageState(Thing parent, float maxDamage)
            : base(parent, maxDamage)
        {
        }

        // Protected in the game; the publicized reference shows it as public, so this is too.
        // The runtime allows an override to be more accessible than what it overrides.
        public override bool DamageAllowed(DamageUpdateType updateType)
        {
            return updateType == DamageUpdateType.Stun || base.DamageAllowed(updateType);
        }
    }

    /// <summary>
    /// Gives a registered door a damage state that accepts stun, unless it already has one
    /// that does (another mod's, or a later game version's), which is kept as it is.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.InitializeDamageState))]
    internal static class DamageStatePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Thing __instance)
        {
            if (!BlastDoorApi.IsBlastDoor(__instance)
                || BlastDoorTracker.AcceptsStun(__instance.DamageState))
            {
                return;
            }
            __instance.DamageState = new StunAllowedDamageState(__instance, __instance.ThingHealth);
        }
    }
}
