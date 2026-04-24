using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Game.Presentation;

public partial class BuildingView : Node2D
{
    private SimBuilding? _building;
    private RememberedBuildingState? _snapshot;
    private bool _selected;
    private bool _fogVisible = true;
    private bool _fogExplored = true;

    public void Bind(SimBuilding building)
    {
        _building = building;
        _snapshot = null;
        SyncFromSimulation(false);
    }

    public void ApplyRememberedState(RememberedBuildingState snapshot, bool selected)
    {
        _building = null;
        _snapshot = snapshot;
        _selected = selected;
        GlobalPosition = snapshot.Center;
        Visible = snapshot.Alive && _fogExplored;
        QueueRedraw();
    }

    public void SyncFromSimulation(bool selected)
    {
        if (_building is null)
        {
            return;
        }

        _selected = selected;
        GlobalPosition = _building.Center;
        Visible = _building.Alive && _fogExplored;
        QueueRedraw();
    }

    public void ApplyFogState(bool visible, bool explored)
    {
        _fogVisible = visible;
        _fogExplored = explored;
        Visible = (_building?.Alive ?? false) && explored;
        Modulate = visible ? Colors.White : new Color(0.62f, 0.62f, 0.68f, 0.9f);
    }

    public override void _Draw()
    {
        if (!_fogExplored)
        {
            return;
        }

        var alive = _snapshot?.Alive ?? _building?.Alive ?? false;
        if (!alive)
        {
            return;
        }

        var sizeTiles = _snapshot?.SizeTiles ?? _building?.SizeTiles ?? 0;
        var completed = _snapshot?.Completed ?? _building?.Completed ?? false;
        var side = _snapshot?.Side ?? _building?.Side ?? GameSide.Player;
        var kind = _snapshot?.Kind ?? _building?.Kind ?? BuildingKind.TownHall;
        var hp = _snapshot?.Hp ?? _building?.Hp ?? 0;
        var maxHp = _snapshot?.MaxHp ?? _building?.MaxHp ?? 1;
        var progress = _snapshot?.ProgressFraction ?? _building?.ProgressFraction() ?? 0f;

        var size = sizeTiles * GameConstants.TileSize;
        var rect = new Rect2(-size / 2f, -size / 2f, size, size);
        var fill = side == GameSide.Player ? new Color(0.31f, 0.49f, 0.71f) : new Color(0.63f, 0.27f, 0.22f);
        if (!completed)
        {
            fill = fill.Darkened(0.35f);
        }

        DrawRect(rect, new Color(0f, 0f, 0f, 0.18f), false, 6f);
        DrawRect(rect, fill);
        DrawBuildingDetails(rect, kind, completed);
        DrawHpBar(rect, hp, maxHp);
        DrawProgressBar(rect, progress);
        DrawRallyFlag();

        if (_selected && _fogVisible)
        {
            DrawRect(rect.Grow(7f), GameColors.SelectionShadow, false, 5f);
            DrawRect(rect.Grow(5f), GameColors.Selection, false, 2f);
        }
    }

    private void DrawBuildingDetails(Rect2 rect, BuildingKind kind, bool completed)
    {
        if (!completed)
        {
            DrawRect(rect.Grow(-8f), new Color(0.82f, 0.73f, 0.48f, 0.22f));
            for (var offset = -rect.Size.X / 2f; offset <= rect.Size.X / 2f; offset += 10f)
            {
                DrawLine(
                    new Vector2(rect.Position.X + offset, rect.Position.Y),
                    new Vector2(rect.Position.X + offset + rect.Size.Y, rect.End.Y),
                    new Color(0.96f, 0.87f, 0.55f, 0.42f),
                    1.5f);
            }
        }

        switch (kind)
        {
            case BuildingKind.TownHall:
                DrawRect(rect.Grow(-12f), new Color(0.78f, 0.71f, 0.52f), false, 3f);
                DrawLine(rect.Position + new Vector2(12f, 12f), rect.End - new Vector2(12f, 12f), Colors.Black, 2f);
                break;
            case BuildingKind.Farm:
                DrawRect(new Rect2(rect.Position + new Vector2(8f, 8f), rect.Size - new Vector2(16f, 16f)), new Color(0.46f, 0.35f, 0.2f));
                DrawLine(rect.Position + new Vector2(12f, rect.Size.Y / 2f), rect.Position + new Vector2(rect.Size.X - 12f, rect.Size.Y / 2f), Colors.Wheat, 2f);
                break;
            case BuildingKind.Barracks:
                DrawRect(rect.Grow(-10f), new Color(0.15f, 0.18f, 0.2f, 0.28f));
                DrawLine(new Vector2(rect.Position.X + 10f, rect.Position.Y + 18f), new Vector2(rect.End.X - 10f, rect.Position.Y + 18f), Colors.Wheat, 3f);
                break;
            case BuildingKind.Workshop:
                DrawCircle(new Vector2(0f, 0f), rect.Size.X * 0.18f, Colors.SaddleBrown);
                DrawLine(new Vector2(-rect.Size.X * 0.18f, rect.Size.Y * 0.15f), new Vector2(rect.Size.X * 0.18f, -rect.Size.Y * 0.15f), Colors.Wheat, 3f);
                break;
            case BuildingKind.Tower:
                DrawPolygon(
                    [new Vector2(0f, rect.Position.Y + 8f), new Vector2(rect.End.X - 10f, rect.End.Y - 8f), new Vector2(rect.Position.X + 10f, rect.End.Y - 8f)],
                    [new Color(0.8f, 0.77f, 0.67f)]);
                break;
        }
    }

    private void DrawHpBar(Rect2 rect, int hp, int maxHp)
    {
        var width = rect.Size.X;
        var position = new Vector2(rect.Position.X, rect.Position.Y - 10f);
        DrawRect(new Rect2(position, new Vector2(width, 6f)), new Color(0f, 0f, 0f, 0.55f));
        DrawRect(new Rect2(position, new Vector2(width * (hp / (float)maxHp), 6f)), new Color(0.24f, 0.84f, 0.29f));
    }

    private void DrawProgressBar(Rect2 rect, float progress)
    {
        if (progress <= 0f)
        {
            return;
        }

        var width = rect.Size.X;
        var position = new Vector2(rect.Position.X, rect.End.Y + 4f);
        DrawRect(new Rect2(position, new Vector2(width, 5f)), new Color(0f, 0f, 0f, 0.5f));
        DrawRect(new Rect2(position, new Vector2(width * progress, 5f)), new Color(1f, 0.84f, 0.42f));
    }

    private void DrawRallyFlag()
    {
        if (_building is null || !_building.Alive || !_building.Completed || _building.Side != GameSide.Player || !_building.RallyPoint.HasValue || !_fogVisible)
        {
            return;
        }

        var flagColor = RallyFlagColor(_building.Id);
        var target = _building.RallyPoint.Value - GlobalPosition;
        var direction = (target == Vector2.Zero ? Vector2.Right : target.Normalized());
        var basePoint = target;
        var poleTop = basePoint + new Vector2(0f, -28f);
        var notch = poleTop + direction * 10f;
        var bannerBottom = poleTop + new Vector2(0f, 12f);

        DrawDashedLine(Vector2.Zero, basePoint, flagColor with { A = 0.65f }, 3f, 12f, 7f);
        DrawLine(basePoint, poleTop, new Color(0.22f, 0.14f, 0.08f, 0.95f), 3f);
        DrawCircle(basePoint, 4f, new Color(0.1f, 0.1f, 0.12f, 0.85f));
        DrawPolygon(
            [poleTop, notch, bannerBottom, poleTop + new Vector2(0f, 6f)],
            [flagColor]);
        DrawCircle(poleTop, 3f, Colors.WhiteSmoke);
    }

    private void DrawDashedLine(Vector2 start, Vector2 end, Color color, float width, float dashLength, float gapLength)
    {
        var direction = end - start;
        var length = direction.Length();
        if (length < 0.001f)
        {
            return;
        }

        direction /= length;
        for (var distance = 0f; distance < length; distance += dashLength + gapLength)
        {
            var segmentStart = start + direction * distance;
            var segmentEnd = start + direction * Mathf.Min(distance + dashLength, length);
            DrawLine(segmentStart, segmentEnd, color, width);
        }
    }

    private static Color RallyFlagColor(int buildingId)
    {
        return (buildingId % 5) switch
        {
            0 => new Color(1f, 0.82f, 0.28f),
            1 => new Color(0.45f, 0.95f, 0.52f),
            2 => new Color(0.42f, 0.78f, 1f),
            3 => new Color(1f, 0.54f, 0.42f),
            _ => new Color(0.86f, 0.62f, 1f)
        };
    }
}
