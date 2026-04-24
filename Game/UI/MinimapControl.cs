using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.World;
using RtsNaGodote.Game.Presentation;
using WorldTileMap = RtsNaGodote.Core.Simulation.World.TileMap;

namespace RtsNaGodote.Game.UI;

public partial class MinimapControl : Control
{
    [Signal]
    public delegate void WorldPointRequestedEventHandler(Vector2 worldPosition);

    private MinimapState? _state;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(220f, 220f);
    }

    public void SetState(MinimapState state)
    {
        _state = state;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_state is null || @event is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        var world = new Vector2(
            Mathf.Clamp(mouseEvent.Position.X / Size.X, 0f, 1f) * _state.Map.Width * GameConstants.TileSize,
            Mathf.Clamp(mouseEvent.Position.Y / Size.Y, 0f, 1f) * _state.Map.Height * GameConstants.TileSize);
        EmitSignal(SignalName.WorldPointRequested, world);
    }

    public override void _Draw()
    {
        if (_state is null)
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.03f, 0.04f, 0.05f));
            return;
        }

        DrawMap(_state.Map, _state.Fog);
        DrawMarkers();
        DrawPings();
        DrawViewport(_state.CameraWorldRect);
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.87f, 0.8f, 0.62f, 0.9f), false, 2f);
    }

    private void DrawMap(WorldTileMap map, FogOfWar fog)
    {
        var scaleX = Size.X / (map.Width * GameConstants.TileSize);
        var scaleY = Size.Y / (map.Height * GameConstants.TileSize);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                if (!fog.IsExplored(x, y))
                {
                    DrawRect(new Rect2(x * GameConstants.TileSize * scaleX, y * GameConstants.TileSize * scaleY, GameConstants.TileSize * scaleX + 1f, GameConstants.TileSize * scaleY + 1f), new Color(0.02f, 0.02f, 0.03f));
                    continue;
                }

                var color = map.Get(x, y) switch
                {
                    TileType.Grass => GameColors.Grass,
                    TileType.Grass2 => GameColors.Grass2,
                    TileType.Forest => GameColors.Forest,
                    TileType.Stone => GameColors.Stone,
                    TileType.Water => GameColors.Water,
                    TileType.Dirt => GameColors.Dirt,
                    _ => Colors.Magenta
                };

                if (!fog.IsVisible(x, y))
                {
                    color = color.Darkened(0.55f);
                }

                DrawRect(new Rect2(x * GameConstants.TileSize * scaleX, y * GameConstants.TileSize * scaleY, GameConstants.TileSize * scaleX + 1f, GameConstants.TileSize * scaleY + 1f), color);
            }
        }
    }

    private void DrawMarkers()
    {
        if (_state is null)
        {
            return;
        }

        var scaleX = Size.X / (_state.Map.Width * GameConstants.TileSize);
        var scaleY = Size.Y / (_state.Map.Height * GameConstants.TileSize);
        foreach (var marker in _state.Markers)
        {
            var point = new Vector2(marker.Position.X * scaleX, marker.Position.Y * scaleY);
            var radius = marker.IsBuilding ? 4.6f : 2.9f;
            DrawCircle(point, radius, marker.Color);
            if (marker.IsBuilding)
            {
                DrawArc(point, radius + 1.5f, 0f, Mathf.Tau, 14, marker.Color.Lightened(0.12f), 1.1f);
            }
        }
    }

    private void DrawPings()
    {
        if (_state is null)
        {
            return;
        }

        var scaleX = Size.X / (_state.Map.Width * GameConstants.TileSize);
        var scaleY = Size.Y / (_state.Map.Height * GameConstants.TileSize);
        foreach (var ping in _state.Pings)
        {
            var point = new Vector2(ping.Position.X * scaleX, ping.Position.Y * scaleY);
            var radius = 7f + (1f - ping.NormalizedLife) * 16f;
            var color = ping.Color;
            color.A = 0.25f + ping.NormalizedLife * 0.55f;
            DrawCircle(point, radius * 0.3f, color);
            DrawArc(point, radius, 0f, Mathf.Tau, 22, color, 2.1f);
        }
    }

    private void DrawViewport(Rect2 worldRect)
    {
        if (_state is null)
        {
            return;
        }

        var scaleX = Size.X / (_state.Map.Width * GameConstants.TileSize);
        var scaleY = Size.Y / (_state.Map.Height * GameConstants.TileSize);
        var rect = new Rect2(
            worldRect.Position.X * scaleX,
            worldRect.Position.Y * scaleY,
            worldRect.Size.X * scaleX,
            worldRect.Size.Y * scaleY);
        DrawRect(rect, new Color(1f, 1f, 1f, 0.16f));
        DrawRect(rect.Grow(1f), new Color(0f, 0f, 0f, 0.3f), false, 3f);
        DrawRect(rect, Colors.White, false, 2.2f);
    }
}
