using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;

namespace RtsNaGodote.Core.Simulation.World;

public static class MapValidation
{
    public static bool HasWalkablePath(TileMap map, Vector2I start, Vector2I goal)
    {
        if (!map.IsWalkable(start.X, start.Y) || !map.IsWalkable(goal.X, goal.Y))
        {
            return false;
        }

        var seen = new bool[GameConstants.MapWidth * GameConstants.MapHeight];
        var queue = new Queue<Vector2I>();
        queue.Enqueue(start);
        seen[Index(start.X, start.Y)] = true;

        var directions = new[]
        {
            new Vector2I(1, 0),
            new Vector2I(-1, 0),
            new Vector2I(0, 1),
            new Vector2I(0, -1)
        };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == goal)
            {
                return true;
            }

            foreach (var direction in directions)
            {
                var next = current + direction;
                if (!map.InBounds(next.X, next.Y) || !map.IsWalkable(next.X, next.Y))
                {
                    continue;
                }

                var index = Index(next.X, next.Y);
                if (seen[index])
                {
                    continue;
                }

                seen[index] = true;
                queue.Enqueue(next);
            }
        }

        return false;
    }

    private static int Index(int tx, int ty) => ty * GameConstants.MapWidth + tx;
}
