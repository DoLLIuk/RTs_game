using Godot;
using RtsNaGodote.Core.Data;

namespace RtsNaGodote.Game.Presentation;

public partial class FogOverlayView : Node2D
{
    private FogOfWar? _fog;

    public void SetFog(FogOfWar fog)
    {
        _fog = fog;
        QueueRedraw();
    }

    public void Refresh()
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_fog is null)
        {
            return;
        }

        for (var y = 0; y < _fog.Height; y++)
        {
            for (var x = 0; x < _fog.Width; x++)
            {
                var state = _fog.GetState(x, y);
                if (state == 2)
                {
                    continue;
                }

                var alpha = state == 1 ? 0.55f : 1.0f;
                DrawRect(
                    new Rect2(x * GameConstants.TileSize, y * GameConstants.TileSize, GameConstants.TileSize, GameConstants.TileSize),
                    new Color(0.04f, 0.05f, 0.07f, alpha));
            }
        }
    }
}
