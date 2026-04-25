using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Units;

namespace RtsNaGodote.Core.Simulation.Pathfinding;

public readonly record struct CombatApproachSlot(Vector2 Target, float ArrivalRadius);

public static class CombatApproachService
{
    public static bool TryBuildCombatApproachTarget(SimUnit unit, ICombatTarget target, out Vector2 approachTarget, out float arrivalRadius)
    {
        if (unit.IsRanged() || unit.IsSiege())
        {
            var rangedSlot = BuildRangedCombatApproachSlot(unit, target);
            approachTarget = rangedSlot.Target;
            arrivalRadius = rangedSlot.ArrivalRadius;
            return true;
        }

        var meleeSlot = BuildMeleeCombatApproachSlot(unit, target);
        approachTarget = meleeSlot.Target;
        arrivalRadius = meleeSlot.ArrivalRadius;
        return true;
    }

    public static int CenteredSlotIndex(int ordinal)
    {
        if (ordinal == 0)
        {
            return 0;
        }

        var step = (ordinal + 1) / 2;
        return ordinal % 2 == 1 ? -step : step;
    }

    private static CombatApproachSlot BuildMeleeCombatApproachSlot(SimUnit unit, ICombatTarget target)
    {
        var forward = GetApproachDirection(unit, target);
        var lateral = new Vector2(-forward.Y, forward.X);
        var contactDistance = target.Radius + unit.Radius + Mathf.Max(unit.Range * 0.3f, 4f);
        var contactCenter = target.Position + forward * contactDistance;
        var contactSlots = target.IsBuilding
            ? Mathf.Clamp(Mathf.RoundToInt(target.Radius / 12f) + 2, 3, 6)
            : Mathf.Clamp(Mathf.RoundToInt(target.Radius / 10f) + 1, 2, 4);
        var rows = target.IsBuilding ? 3 : 2;
        var assignment = Mathf.PosMod(unit.Id, contactSlots * rows);
        var lane = CenteredSlotIndex(assignment % contactSlots);
        var rank = assignment / contactSlots;
        var laneSpacing = unit.Radius * 2f + 4f;
        var followSpacing = unit.Radius * 2.1f + 6f;
        var offset = lateral * (lane * laneSpacing) + forward * (rank * followSpacing);
        var targetPoint = contactCenter + offset;
        var arrival = Mathf.Max(unit.Radius * 0.6f, 7f);
        return new CombatApproachSlot(targetPoint, arrival);
    }

    private static CombatApproachSlot BuildRangedCombatApproachSlot(SimUnit unit, ICombatTarget target)
    {
        var forward = GetApproachDirection(unit, target);
        var baseAngle = Mathf.Atan2(forward.Y, forward.X);
        var desiredRadius = target.Radius + unit.Radius + Mathf.Max(unit.Range * 0.52f, GameConstants.TileSize * 0.35f);
        var slotCount = target.IsBuilding ? 4 : 3;
        var spread = target.IsBuilding ? 0.5f : 0.32f;
        var ordinal = Mathf.PosMod(unit.Id, slotCount);
        var t = slotCount == 1 ? 0.5f : ordinal / (float)(slotCount - 1);
        var angle = baseAngle - spread * 0.5f + spread * t;
        var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * desiredRadius;
        var targetPoint = target.Position + offset;
        var arrival = Mathf.Max(unit.Radius * 0.7f, 8f);
        return new CombatApproachSlot(targetPoint, arrival);
    }

    private static Vector2 GetApproachDirection(SimUnit unit, ICombatTarget target)
    {
        var direction = unit.Position - target.Position;
        if (direction.LengthSquared() <= 1f)
        {
            var fallbackAngle = Mathf.Tau * (Mathf.PosMod(unit.Id, 8) / 8f);
            return new Vector2(Mathf.Cos(fallbackAngle), Mathf.Sin(fallbackAngle));
        }

        return direction.Normalized();
    }
}
