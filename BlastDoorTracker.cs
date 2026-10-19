using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Structures;
using UnityEngine;

namespace FailClosedBlastDoors
{
    /// <summary>
    /// One blast door's accumulator and bookkeeping. The charge model: stored energy runs from
    /// 0 to 1, a door open by a fraction t needs t of it to get back to closed. A powered open
    /// raises the charge with t; a manual open does not; a powered, fully open door recharges;
    /// every close spends it.
    /// </summary>
    internal sealed class DoorData
    {
        public readonly Door Door;

        // Written on the main thread, read from any thread through BlastDoorApi.
        private float _charge;
        private float _openFraction;
        private int _state;

        public float PrevT;
        public bool PrevPowered;
        public bool Initialised;

        // Whether a refused close is being reported through the Error logic value.
        public bool ErrorFlag;

        public DoorData(Door door)
        {
            Door = door;
        }

        public float Charge
        {
            get => Volatile.Read(ref _charge);
            set => Volatile.Write(ref _charge, value);
        }

        public float OpenFraction
        {
            get => Volatile.Read(ref _openFraction);
            set => Volatile.Write(ref _openFraction, value);
        }

        public BlastDoorState State
        {
            get => (BlastDoorState)Volatile.Read(ref _state);
            set => Volatile.Write(ref _state, (int)value);
        }
    }

    internal static class BlastDoorTracker
    {
        // How close the charge must be to the open fraction to count as enough (float error).
        public const float Epsilon = 0.001f;

        private static readonly ConcurrentDictionary<Door, DoorData> Doors =
            new ConcurrentDictionary<Door, DoorData>();

        public static DoorData Get(Door door)
        {
            return Doors.GetOrAdd(door, d => new DoorData(d));
        }

        public static bool TryGet(Door door, out DoorData data)
        {
            data = null;
            return door != null && Doors.TryGetValue(door, out data);
        }

        // The interactable is the truth the animation follows. Thing.IsOpen is cached per frame.
        public static bool IsOpenNow(Door door)
        {
            var open = door.InteractOpen;
            return open != null ? open.State == 1 : door.IsOpen;
        }

        public static BlastDoorState Compute(DoorData data)
        {
            var door = data.Door;
            var anim = door.doorAnimComponent;
            var motion = anim != null ? anim.CurrentState : BinaryAnimState.None;
            if (motion == BinaryAnimState.OffToOn)
            {
                return door.Powered ? BlastDoorState.Opening : BlastDoorState.Standby;
            }
            if (motion == BinaryAnimState.OnToOff)
            {
                return BlastDoorState.Closing;
            }
            if (door.Powered && IsOpenNow(door) && data.Charge < 1f - Epsilon)
            {
                return BlastDoorState.Recharging;
            }
            return BlastDoorState.Standby;
        }

        public static void SetError(DoorData data, bool on)
        {
            data.ErrorFlag = on;
            var door = data.Door;
            // Some prefabs have no error state, and then the setter does nothing. The logic
            // patches report the flag either way.
            if (door.HasErrorState && (door.Error == 1) != on)
            {
                door.Error = on ? 1 : 0;
            }
        }

        private static void Close(DoorData data)
        {
            SetError(data, false);
            data.Door.Setting = 0;
            OnServer.Interact(data.Door.InteractOpen, 0);
        }

        // Runs every frame on the host or single player, once a world is running.
        public static void Tick(float deltaTime)
        {
            List<Door> gone = null;
            foreach (var pair in Doors)
            {
                if (pair.Key == null)
                {
                    (gone ?? (gone = new List<Door>())).Add(pair.Key);
                    continue;
                }
                TickDoor(pair.Value, deltaTime);
            }
            if (gone != null)
            {
                foreach (var door in gone)
                {
                    Doors.TryRemove(door, out _);
                }
            }
        }

        // Clients have the data too (the animation patch makes it) but never run it.
        public static void Prune()
        {
            foreach (var door in Doors.Keys)
            {
                if (door == null)
                {
                    Doors.TryRemove(door, out _);
                }
            }
        }

        private static void TickDoor(DoorData data, float dt)
        {
            var door = data.Door;
            var anim = door.doorAnimComponent;
            if (anim == null || !door.IsStructureCompleted)
            {
                return;
            }

            var t = anim.t;
            var powered = door.Powered;
            var open = IsOpenNow(door);

            if (!data.Initialised)
            {
                // A door loaded open and powered is taken to be armed. One loaded open without
                // power was opened by hand, or could not close, so it starts empty. Vanilla
                // saves hold Setting 0 for open doors, so a pending close is lost on load.
                data.Initialised = true;
                data.PrevT = t;
                data.PrevPowered = powered;
                data.Charge = powered ? t : 0f;
                door.Setting = open ? 1 : 0;
            }

            var dT = t - data.PrevT;
            data.PrevT = t;
            var charge = data.Charge;
            if (dT > 0f && powered)
            {
                charge = Mathf.Min(1f, charge + dT);
            }
            else if (dT < 0f)
            {
                charge = Mathf.Max(0f, charge + dT);
            }
            var motion = anim.CurrentState;
            if (powered && motion == BinaryAnimState.On && charge < 1f)
            {
                charge = Mathf.Min(1f, charge + dt / Mathf.Max(0.01f, Settings.OpenSeconds.Value));
            }
            if (t <= 0f)
            {
                charge = 0f;
            }
            data.Charge = charge;
            data.OpenFraction = t;

            var affordable = charge >= t - Epsilon;
            var lostPower = data.PrevPowered && !powered;
            data.PrevPowered = powered;

            if (open && motion != BinaryAnimState.OnToOff)
            {
                if (lostPower && affordable)
                {
                    Close(data);
                }
                else if (door.Setting < 0.5)
                {
                    // A refused close waits in Setting; see CloseGatePatch.
                    if (affordable)
                    {
                        Close(data);
                    }
                    else
                    {
                        SetError(data, true);
                    }
                }
            }
            if (data.ErrorFlag && (!open || door.Setting >= 0.5))
            {
                SetError(data, false);
            }

            var old = data.State;
            var now = Compute(data);
            if (now != old)
            {
                data.State = now;
                // The status light's colour depends on Recharging; nudge it when that changes.
                if ((old == BlastDoorState.Recharging || now == BlastDoorState.Recharging)
                    && (now == BlastDoorState.Recharging || now == BlastDoorState.Standby))
                {
                    door.ToggleStatusLight(
                        powered, door.IsLocked ? Door.StatusLightsLock : Door.StatusLightsUnlock);
                }
                BlastDoorApi.RaiseStateChanged(door, old, now);
            }
        }
    }
}
