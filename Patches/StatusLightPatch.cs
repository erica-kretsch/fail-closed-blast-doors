using Assets.Scripts.Objects.Structures;
using Assets.Scripts.Util;
using HarmonyLib;
using UnityEngine;

namespace FailClosedBlastDoors.Patches
{
    /// <summary>
    /// Shows the recharge colour on the status lights while an open door recharges. Vanilla
    /// calls <c>ToggleStatusLight</c> from several places (animation end, lock and power
    /// changes) and each would otherwise put its own colour back.
    ///
    /// Two things change. The prefix swaps the colour the <c>Light</c> components get. The
    /// emissive panel is harder: <c>MaterialChanger</c> only swaps between preset materials,
    /// and the one it picks for an unknown colour is the white "powered" one, which drowns the
    /// light out. So the postfix applies the "active" (green) state, finds the materials that
    /// state changed, and puts tinted copies of them on the renderer. The next vanilla
    /// <c>ChangeState</c> replaces those copies, so leaving Recharging needs no cleanup.
    ///
    /// The charge is the host's, so a client never sees this colour.
    /// </summary>
    [HarmonyPatch(typeof(Door), nameof(Door.ToggleStatusLight))]
    internal static class StatusLightPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Door __instance, bool toggle, ref Color color)
        {
            if (toggle && IsRecharging(__instance))
            {
                color = Settings.RechargeColor.Value;
            }
        }

        [HarmonyPostfix]
        private static void Postfix(Door __instance, bool toggle)
        {
            if (toggle && IsRecharging(__instance))
            {
                TintPanel(__instance, Settings.RechargeColor.Value);
            }
        }

        private static bool IsRecharging(Door door)
        {
            return BlastDoorTracker.TryGet(door, out var data)
                && BlastDoorTracker.Compute(data) == BlastDoorState.Recharging;
        }

        private static void TintPanel(Door door, Color colour)
        {
            var changer = door.statusLightsMaterialChanger;
            var renderer = changer != null ? changer.renderer : null;
            if (renderer == null)
            {
                return;
            }

            changer.ChangeState(Defines.Animator.Powered);
            var powered = renderer.sharedMaterials;
            changer.ChangeState(Defines.Animator.Active);
            var active = renderer.sharedMaterials;

            for (var i = 0; i < active.Length && i < powered.Length; i++)
            {
                if (active[i] == null || ReferenceEquals(active[i], powered[i]))
                {
                    continue;
                }
                var tinted = new Material(active[i]);
                if (tinted.HasProperty("_Color"))
                {
                    tinted.SetColor("_Color", colour);
                }
                if (tinted.HasProperty("_EmissionColor"))
                {
                    var strength = Mathf.Max(1f, tinted.GetColor("_EmissionColor").maxColorComponent);
                    tinted.EnableKeyword("_EMISSION");
                    tinted.SetColor("_EmissionColor", colour * strength);
                }
                active[i] = tinted;
            }
            renderer.sharedMaterials = active;
        }
    }
}
