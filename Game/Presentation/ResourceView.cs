using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Resources;

namespace RtsNaGodote.Game.Presentation;

public partial class ResourceView : Node2D
{
    private SimResourceNode? _resource;
    private RememberedResourceState? _snapshot;
    private bool _fogVisible = true;
    private bool _fogExplored = true;

    public int ResourceId => _resource?.Id ?? -1;

    public void Bind(SimResourceNode resource)
    {
        _resource = resource;
        _snapshot = null;
        SyncFromSimulation();
    }

    public void ApplyRememberedState(RememberedResourceState snapshot)
    {
        _resource = null;
        _snapshot = snapshot;
        GlobalPosition = snapshot.Center;
        Visible = snapshot.Alive && _fogExplored;
        QueueRedraw();
    }

    public void SyncFromSimulation()
    {
        if (_resource is null)
        {
            return;
        }

        GlobalPosition = _resource.Center;
        Visible = _resource.Alive && _fogExplored;
        QueueRedraw();
    }

    public void ApplyFogState(bool visible, bool explored)
    {
        _fogVisible = visible;
        _fogExplored = explored;
        Visible = (_resource?.Alive ?? false) && explored;
        Modulate = visible ? Colors.White : new Color(0.6f, 0.6f, 0.66f, 0.92f);
    }

    public override void _Draw()
    {
        if (!_fogExplored)
        {
            return;
        }

        var alive = _snapshot?.Alive ?? _resource?.Alive ?? false;
        if (!alive)
        {
            return;
        }

        var type = _snapshot?.Type ?? _resource?.Type ?? ResourceType.Gold;
        var radius = _snapshot?.Radius ?? _resource?.Radius ?? 0f;
        var tileWidth = _snapshot?.TileWidth ?? _resource?.TileWidth ?? 1;
        var tileHeight = _snapshot?.TileHeight ?? _resource?.TileHeight ?? 1;

        if (type == ResourceType.Gold)
        {
            var size = new Vector2(tileWidth * GameConstants.TileSize, tileHeight * GameConstants.TileSize);
            var rect = new Rect2(new Vector2(-size.X / 2f, -size.Y * 0.42f), size);
            DrawRect(rect, GameColors.GoldMine);
            DrawRect(rect.Grow(-6f), new Color(1f, 0.93f, 0.63f, 0.35f));
            return;
        }

        DrawCircle(Vector2.Zero + new Vector2(0f, 8f), radius * 0.92f, new Color(0f, 0f, 0f, 0.18f));
        DrawCircle(Vector2.Zero, radius * 0.95f, new Color(0.11f, 0.31f, 0.12f));
        DrawCircle(Vector2.Zero + new Vector2(-3f, -4f), radius * 0.45f, new Color(0.2f, 0.45f, 0.18f));
    }
}
