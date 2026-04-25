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

    public static InteractionZone GetInteractionZone(SimUnit unit, SimBuilding building)
    {
        return BuildInteractionZone(unit, building.Center, building.TilePosition, building.SizeTiles, building.SizeTiles);
    }

    public static InteractionZone GetInteractionZone(SimUnit unit, SimResourceNode resource)
    {
        return BuildInteractionZone(unit, resource.Center, resource.TilePosition, resource.TileWidth, resource.TileHeight);
    }

    public static Vector2 GetClosestInteractionPoint(SimUnit unit, Vector2 fromPoint, SimBuilding building)
    {
        return GetClosestInteractionPoint(unit, fromPoint, building.TilePosition, building.SizeTiles, building.SizeTiles);
    }

    public static Vector2 GetClosestInteractionPoint(SimUnit unit, Vector2 fromPoint, SimResourceNode resource)
    {
        return GetClosestInteractionPoint(unit, fromPoint, resource.TilePosition, resource.TileWidth, resource.TileHeight);
    }

    private static InteractionZone BuildInteractionZone(
        SimUnit unit,
        Vector2 center,
        Vector2I tilePosition,
        int widthTiles,
        int heightTiles)
    {
        var halfWidth = widthTiles * GameConstants.TileSize * 0.5f;
        var halfHeight = heightTiles * GameConstants.TileSize * 0.5f;
        var clearance = GetInteractionClearance(unit);
        var interactionRadius = Mathf.Max(halfWidth, halfHeight) + clearance * 0.82f;
        return new InteractionZone(center, interactionRadius, center);
    }

    private static Vector2 GetClosestInteractionPoint(
        SimUnit unit,
        Vector2 fromPoint,
        Vector2I tilePosition,
        int widthTiles,
        int heightTiles)
    {
        var minX = tilePosition.X * GameConstants.TileSize;
        var minY = tilePosition.Y * GameConstants.TileSize;
        var maxX = minX + widthTiles * GameConstants.TileSize;
        var maxY = minY + heightTiles * GameConstants.TileSize;
        var closestX = Mathf.Clamp(fromPoint.X, minX, maxX);
        var closestY = Mathf.Clamp(fromPoint.Y, minY, maxY);
        var closest = new Vector2(closestX, closestY);
        var outward = fromPoint - closest;
        if (outward.LengthSquared() <= 0.01f)
        {
            var center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            outward = fromPoint - center;
            if (outward.LengthSquared() <= 0.01f)
            {
                outward = Vector2.Down;
            }
        }

        return closest + outward.Normalized() * GetInteractionClearance(unit);
    }
}
