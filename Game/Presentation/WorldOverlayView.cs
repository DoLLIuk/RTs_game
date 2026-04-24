using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Resources;
using RtsNaGodote.Core.Simulation.Units;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Game.Presentation;

public partial class WorldOverlayView : Node2D
{
    private GameSimulation? _simulation;
    private BuildingKind? _placementKind;
    private Vector2 _mouseWorld;
    private Vector2? _selectionStartWorld;
    private Vector2? _selectionCurrentWorld;
    private SimUnit? _hoveredUnit;
    private SimBuilding? _hoveredBuilding;
    private SimResourceNode? _hoveredResource;

    public override void _Ready()
    {
        ZAsRelative = false;
        ZIndex = 100;
    }

    public void SyncState(
        GameSimulation? simulation,
        BuildingKind? placementKind,
        Vector2 mouseWorld,
        Vector2? selectionStartWorld,
        Vector2? selectionCurrentWorld,
        SimUnit? hoveredUnit,
        SimBuilding? hoveredBuilding,
        SimResourceNode? hoveredResource)
    {
        _simulation = simulation;
        _placementKind = placementKind;
        _mouseWorld = mouseWorld;
        _selectionStartWorld = selectionStartWorld;
        _selectionCurrentWorld = selectionCurrentWorld;
        _hoveredUnit = hoveredUnit;
        _hoveredBuilding = hoveredBuilding;
        _hoveredResource = hoveredResource;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawSelectionRectangle();
        DrawPlacementPreview();
        DrawHoverIndicator();
    }

    private void DrawSelectionRectangle()
    {
        if (!_selectionStartWorld.HasValue || !_selectionCurrentWorld.HasValue)
        {
            return;
        }

        var rect = new Rect2(_selectionStartWorld.Value, _selectionCurrentWorld.Value - _selectionStartWorld.Value).Abs();
        if (rect.Size.Length() < GameConstants.SelectionDragThreshold)
        {
            return;
        }

        var pulse = 0.55f + (Mathf.Sin(Time.GetTicksMsec() / 110.0f) + 1f) * 0.12f;
        var fill = new Color(0.29f, 0.64f, 1f, 0.12f + pulse * 0.08f);
        var bright = new Color(0.62f, 0.84f, 1f, 0.95f);
        DrawRect(rect, fill);
        DrawRect(rect.Grow(3f), new Color(0f, 0f, 0f, 0.26f), false, 5f);
        DrawRect(rect, GameColors.Selection, false, 2f);
        DrawSelectionCorners(rect, bright);
        DrawCircle(rect.Position, 4f, bright);
        DrawCircle(rect.End, 4f, bright);
    }

    private void DrawSelectionCorners(Rect2 rect, Color color)
    {
        const float corner = 18f;
        const float width = 3f;

        DrawLine(rect.Position, rect.Position + new Vector2(corner, 0f), color, width);
        DrawLine(rect.Position, rect.Position + new Vector2(0f, corner), color, width);
        DrawLine(new Vector2(rect.End.X, rect.Position.Y), new Vector2(rect.End.X - corner, rect.Position.Y), color, width);
        DrawLine(new Vector2(rect.End.X, rect.Position.Y), new Vector2(rect.End.X, rect.Position.Y + corner), color, width);
        DrawLine(rect.End, rect.End + new Vector2(-corner, 0f), color, width);
        DrawLine(rect.End, rect.End + new Vector2(0f, -corner), color, width);
        DrawLine(new Vector2(rect.Position.X, rect.End.Y), new Vector2(rect.Position.X + corner, rect.End.Y), color, width);
        DrawLine(new Vector2(rect.Position.X, rect.End.Y), new Vector2(rect.Position.X, rect.End.Y - corner), color, width);
    }

    private void DrawPlacementPreview()
    {
        if (!_placementKind.HasValue || _simulation is null)
        {
            return;
        }

        var definition = GameDefinitions.Buildings[_placementKind.Value];
        var tile = _simulation.Map.WorldToTile(_mouseWorld);
        var placement = _simulation.EvaluateBuildingPlacement(GameSide.Player, _placementKind.Value, tile);
        var valid = placement.CanPlace;
        var fillColor = valid ? new Color(0.34f, 1f, 0.45f, 0.16f) : new Color(1f, 0.28f, 0.28f, 0.16f);
        var border = valid ? new Color(0.34f, 1f, 0.45f, 0.95f) : new Color(1f, 0.28f, 0.28f, 0.95f);
        var gridColor = new Color(1f, 1f, 1f, 0.18f);

        for (var dy = 0; dy < definition.Size; dy++)
        {
            for (var dx = 0; dx < definition.Size; dx++)
            {
                var tx = tile.X + dx;
                var ty = tile.Y + dy;
                var cellValid = _simulation.Map.InBounds(tx, ty) && _simulation.Map.IsWalkable(tx, ty);
                var rect = new Rect2(
                    tx * GameConstants.TileSize,
                    ty * GameConstants.TileSize,
                    GameConstants.TileSize,
                    GameConstants.TileSize);

                DrawRect(rect, cellValid ? fillColor : new Color(1f, 0.28f, 0.28f, 0.24f));
                DrawRect(rect, gridColor, false, 1f);
                if (!cellValid)
                {
                    DrawLine(rect.Position, rect.End, border, 2f);
                    DrawLine(new Vector2(rect.End.X, rect.Position.Y), new Vector2(rect.Position.X, rect.End.Y), border, 2f);
                }
            }
        }

        var footprint = new Rect2(
            tile.X * GameConstants.TileSize,
            tile.Y * GameConstants.TileSize,
            definition.Size * GameConstants.TileSize,
            definition.Size * GameConstants.TileSize);
        DrawRect(footprint, border, false, 3f);

        var anchor = footprint.Position + footprint.Size / 2f;
        var anchorColor = valid ? new Color(1f, 0.92f, 0.54f, 0.92f) : new Color(1f, 0.6f, 0.5f, 0.92f);
        DrawCircle(anchor, 5f, anchorColor);
        DrawLine(anchor + new Vector2(-10f, 0f), anchor + new Vector2(10f, 0f), anchorColor, 2f);
        DrawLine(anchor + new Vector2(0f, -10f), anchor + new Vector2(0f, 10f), anchorColor, 2f);
    }

    private void DrawHoverIndicator()
    {
        if (_simulation is null || _selectionStartWorld.HasValue || _placementKind.HasValue)
        {
            return;
        }

        if (_hoveredUnit is not null)
        {
            var color = _hoveredUnit.Side == GameSide.Player ? new Color(0.72f, 0.9f, 1f, 0.9f) : new Color(1f, 0.62f, 0.56f, 0.9f);
            DrawHoverBracket(_hoveredUnit.Position, _hoveredUnit.Radius + 10f, color);
            return;
        }

        if (_hoveredBuilding is not null)
        {
            var color = _hoveredBuilding.Side == GameSide.Player ? new Color(0.72f, 0.9f, 1f, 0.9f) : new Color(1f, 0.62f, 0.56f, 0.9f);
            DrawHoverBracket(_hoveredBuilding.Center, _hoveredBuilding.Radius + 14f, color);
            return;
        }

        if (_hoveredResource is not null)
        {
            var color = _hoveredResource.Type == ResourceType.Gold ? new Color(1f, 0.88f, 0.42f, 0.9f) : new Color(0.58f, 0.9f, 0.56f, 0.9f);
            DrawHoverBracket(_hoveredResource.Center, _hoveredResource.Radius + 12f, color);
        }
    }

    private void DrawHoverBracket(Vector2 center, float radius, Color color)
    {
        var size = radius * 0.65f;
        const float width = 2f;
        var tl = center + new Vector2(-radius, -radius);
        var tr = center + new Vector2(radius, -radius);
        var br = center + new Vector2(radius, radius);
        var bl = center + new Vector2(-radius, radius);

        DrawLine(tl, tl + new Vector2(size, 0f), color, width);
        DrawLine(tl, tl + new Vector2(0f, size), color, width);
        DrawLine(tr, tr + new Vector2(-size, 0f), color, width);
        DrawLine(tr, tr + new Vector2(0f, size), color, width);
        DrawLine(br, br + new Vector2(-size, 0f), color, width);
        DrawLine(br, br + new Vector2(0f, -size), color, width);
        DrawLine(bl, bl + new Vector2(size, 0f), color, width);
        DrawLine(bl, bl + new Vector2(0f, -size), color, width);
    }
}
