using System;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace FailClosedBlastDoors
{
    internal static class Settings
    {
        public const float MinOpenSeconds = 0.5f;
        public const float MaxOpenSeconds = 120f;

        public static ConfigEntry<float> OpenSeconds;

        // Read this, not OpenSeconds.Value: a hand-edited config file or a settings screen that
        // ignores the declared range can still hold 0 (which skips the animation) or a
        // negative number (which would run it backwards).
        public static float OpenSecondsClamped =>
            Mathf.Clamp(OpenSeconds.Value, MinOpenSeconds, MaxOpenSeconds);
        public static ConfigEntry<Color> RechargeColor;
    }

    // LaunchPad calls Awake on the splash screen, before any world exists. Only patch and bind
    // config here; nothing may touch game state until a world is running.
    [BepInPlugin(ThisModInfo.ModID, ThisModInfo.AssemblyName, ThisModInfo.Version)]
    public class FailClosedBlastDoorsPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            try
            {
                Settings.OpenSeconds = Config.Bind(
                    "Blast door", "OpenSeconds", 10f,
                    new ConfigDescription(
                        "Seconds a powered blast door takes to open, and to recharge from empty. "
                        + "Closing and opening by crowbar keep the vanilla speed.",
                        new AcceptableValueRange<float>(Settings.MinOpenSeconds, Settings.MaxOpenSeconds)));
                Settings.RechargeColor = Config.Bind(
                    "Blast door", "RechargeColor", new Color(1f, 0.65f, 0f, 1f),
                    "Status light colour while an open door recharges its accumulator.");

                new Harmony(ThisModInfo.ModID).PatchAll(typeof(FailClosedBlastDoorsPlugin).Assembly);
                Log.LogInfo($"Fail-Closed Blast Doors {ThisModInfo.Version} initialized");
            }
            catch (Exception ex)
            {
                Log.LogError($"Error during Fail-Closed Blast Doors init: {ex}");
            }
        }

        private void Update()
        {
            try
            {
                BlastDoorTracker.Prune();
                if (GameManager.GameState == GameState.Running)
                {
                    if (GameManager.RunSimulation)
                    {
                        BlastDoorTracker.Tick(Time.deltaTime);
                    }
                    else
                    {
                        BlastDoorTracker.TickClient();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Blast door update failed: {ex}");
            }
        }
    }
}
