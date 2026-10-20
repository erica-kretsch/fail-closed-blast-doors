using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Assets.Scripts;
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

        // The smallest change of the stun value, as a share of the damage state's maximum, that
        // is written while recharging.
        private const float StunStep = 0.02f;

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

        // Any damage state that allows stun will do, ours or another mod's. The recharge
        // deficit is kept there because the game saves and syncs it, so a door keeps its charge
        // across a reload and clients can see it.
        public static bool AcceptsStun(IndestructableDamageState state)
        {
            return state != null && state.DamageAllowed(DamageUpdateType.Stun);
        }

        private static IndestructableDamageState StunState(Door door)
        {
            var state = door.DamageState;
            return AcceptsStun(state) ? state : null;
        }

        // The stored stun as a share of the state's maximum, 0 when the door has none.
        private static float StunFraction(Door door)
        {
            var state = StunState(door);
            if (state == null || state.MaxDamage <= 0f)
            {
                return 0f;
            }
            return Mathf.Clamp01(state.GetDamageValue(DamageUpdateType.Stun).Value / state.MaxDamage);
        }

        // The host holds the charge. A client has only the stun the host synced, which reads as
        // "armed" when the door has nowhere to keep it.
        public static float EffectiveCharge(DoorData data)
        {
            if (GameManager.RunSimulation)
            {
                return data.Charge;
            }
            return StunState(data.Door) == null ? 1f : 1f - StunFraction(data.Door);
        }

        // The stun is the deficit while the door sits open and not full, so stun above zero is
        // exactly "recharging". It is written in steps to keep network updates down.
        private static void SyncStun(DoorData data, bool open, BinaryAnimState motion, float charge)
        {
            var state = StunState(data.Door);
            if (state == null || state.MaxDamage <= 0f)
            {
                return;
            }
            var target = open && motion == BinaryAnimState.On
                ? (1f - Mathf.Clamp01(charge)) * state.MaxDamage
                : 0f;
            var current = state.GetDamageValue(DamageUpdateType.Stun).Value;
            var endpoint = target <= 0f || target >= state.MaxDamage;
            if (target != current
                && (endpoint || Mathf.Abs(target - current) >= StunStep * state.MaxDamage))
            {
                state.Damage(ChangeDamageType.Set, target, DamageUpdateType.Stun);
            }
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
            if (door.Powered && IsOpenNow(door) && EffectiveCharge(data) < 1f - Epsilon)
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

        // Clients have the door data too (the animation patch makes it) but never run the
        // accumulator. They take the state from what the host synced: the animation and the stun.
        public static void TickClient()
        {
            foreach (var pair in Doors)
            {
                var door = pair.Key;
                var anim = door != null ? door.doorAnimComponent : null;
                if (anim == null)
                {
                    continue;
                }
                var data = pair.Value;
                data.OpenFraction = anim.t;
                data.Charge = EffectiveCharge(data);
                UpdateState(data);
            }
        }

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
                // The saved stun is the recharge deficit of a door that was open and still, so
                // such a door gets its charge back. With no stun state to read, an open powered
                // door is taken as armed and an unpowered one, opened by hand, as empty.
                // Vanilla saves hold Setting 0 for open doors, so a pending close is lost on
                // load.
                data.Initialised = true;
                data.PrevT = t;
                data.PrevPowered = powered;
                if (open && t >= 1f && StunState(door) != null)
                {
                    data.Charge = 1f - StunFraction(door);
                }
                else
                {
                    data.Charge = powered ? t : 0f;
                }
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

            SyncStun(data, open, motion, charge);
            UpdateState(data);
        }

        private static void UpdateState(DoorData data)
        {
            var door = data.Door;
            var old = data.State;
            var now = Compute(data);
            if (now == old)
            {
                return;
            }
            data.State = now;
            // The status light's colour depends on Recharging; nudge it when that changes.
            if ((old == BlastDoorState.Recharging || now == BlastDoorState.Recharging)
                && (now == BlastDoorState.Recharging || now == BlastDoorState.Standby))
            {
                door.ToggleStatusLight(
                    door.Powered, door.IsLocked ? Door.StatusLightsLock : Door.StatusLightsUnlock);
            }
            BlastDoorApi.RaiseStateChanged(door, old, now);
        }
    }
}
