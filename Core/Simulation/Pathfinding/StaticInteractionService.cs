using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Resources;
using RtsNaGodote.Core.Simulation.Units;

namespace RtsNaGodote.Core.Simulation.Pathfinding;

public static class StaticInteractionService
{
    public static float GetInteractionClearance(SimUnit unit)
    {
        return unit.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles * (GameConstants.StaticInteractionReachScale + 0.12f);
    }

    public static bool IsWithinInteractionRange(SimUnit unit, Vector2 point, SimBuilding building)
    {
        return DistanceToFootprint(point, building.TilePosition, building.SizeTiles, building.SizeTiles) <= GetInteractionClearance(unit);
    }

    public static bool IsWithinInteractionRange(SimUnit unit, Vector2 point, SimResourceNode resource)
    {
        return DistanceToFootprint(point, resource.TilePosition, resource.TileWidth, resource.TileHeight) <= GetInteractionClearance(unit);
    }

    public static float DistanceToFootprint(Vector2 point, Vector2I tilePosition, int widthTiles, int heightTiles)
    {
        var minX = tilePosition.X * GameConstants.TileSize;
        var minY = tilePosition.Y * GameConstants.TileSize;
        var maxX = minX + widthTiles * GameConstants.TileSize;
        var maxY = minY + heightTiles * GameConstants.TileSize;
        var closestX = Mathf.Clamp(point.X, minX, maxX);
        var closestY = Mathf.Clamp(point.Y, minY, maxY);
        return point.DistanceTo(new Vector2(closestX, closestY));
    }

    public static float GetDirectionalSupportDistance(Vector2 direction, int widthTiles, int heightTiles)
    {
        if (direction.LengthSquared() <= 0.0001f)
        {
            direction = Vector2.Down;
        }

        direction = direction.Normalized();
        var halfExtents = new Vector2(
            widthTiles * GameConstants.TileSize * 0.5f,
            heightTiles * GameConstants.TileSize * 0.5f);
        return Mathf.Abs(direction.X) * halfExtents.X + Mathf.Abs(direction.Y) * halfExtents.Y;
    }

    public static Vector2 BuildApproachTarget(SimUnit unit, SimBuilding building)
    {
        return BuildApproachTarget(unit, building.Center, building.TilePosition, building.SizeTiles, building.SizeTiles);
    }

    public static Vector2 BuildApproachTarget(SimUnit unit, SimResourceNode resource)
    {
        return BuildApproachTarget(unit, resource.Center, resource.TilePosition, resource.TileWidth, resource.TileHeight);
    }

    private static Vector2 BuildApproachTarget(
        SimUnit unit,
        Vector2 center,
        Vector2I tilePosition,
        int widthTiles,
        int heightTiles)
    {
        var relative = unit.Position - center;
        if (relative.LengthSquared() <= 1f)
        {
            var fallbackAngle = Mathf.Tau * (Mathf.PosMod(unit.Id, 8) / 8f);
            relative = new Vector2(Mathf.Cos(fallbackAngle), Mathf.Sin(fallbackAngle));
        }

        var halfWidth = widthTiles * GameConstants.TileSize * 0.5f;
        var halfHeight = heightTiles * GameConstants.TileSize * 0.5f;
        Vector2 normal;
        Vector2 tangent;
        float tangentHalfLength;

        var normalizedX = halfWidth <= 0.01f ? 0f : Mathf.Abs(relative.X) / halfWidth;
        var normalizedY = halfHeight <= 0.01f ? 0f : Mathf.Abs(relative.Y) / halfHeight;
        if (normalizedX >= normalizedY)
        {
            normal = relative.X >= 0f ? Vector2.Right : Vector2.Left;
            tangent = Vector2.Down;
            tangentHalfLength = halfHeight;
        }
        else
        {
            normal = relative.Y >= 0f ? Vector2.Down : Vector2.Up;
            tangent = Vector2.Right;
            tangentHalfLength = halfWidth;
        }

        var lane = CombatApproachService.CenteredSlotIndex(Mathf.PosMod(unit.Id, 3));
        var rank = Mathf.PosMod(unit.Id / 3, 2);
        var laneSpacing = float.Max(unit.Radius * 1.15f + 2f, GameConstants.TileSize * 0.26f);
        var rankSpacing = float.Max(unit.Radius * 0.8f, 2f);
        var tangentSlack = float.Max(2f, unit.Radius * 0.2f);
        var maxTangentOffset = float.Max(0f, tangentHalfLength - tangentSlack);
        var tangentOffset = Mathf.Clamp(lane * laneSpacing, -maxTangentOffset, maxTangentOffset);
        var supportDistance = GetDirectionalSupportDistance(normal, widthTiles, heightTiles);
        var outwardOffset = supportDistance + GetInteractionClearance(unit) * 0.92f + rank * rankSpacing;
        return center + normal * outwardOffset + tangent * tangentOffset;
    }
}
