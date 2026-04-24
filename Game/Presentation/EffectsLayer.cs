using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;

namespace RtsNaGodote.Game.Presentation;

public partial class EffectsLayer : Node2D
{
    private readonly List<RingEffect> _rings = [];
    private readonly List<TextEffect> _texts = [];
    private readonly List<ProjectileEffect> _projectiles = [];

    public override void _Process(double delta)
    {
        TickEffects((float)delta);
    }

    public void CommandMarker(Vector2 position, Color color, string? label = null)
    {
        _rings.Add(new RingEffect(position, color, 0.55f, 12f, 54f, false));
        if (!string.IsNullOrWhiteSpace(label))
        {
            FloatingText(position + new Vector2(0f, -28f), label!, color, 0.8f);
        }

        QueueRedraw();
    }

    public void FloatingText(Vector2 position, string text, Color color, float duration = 0.9f)
    {
        _texts.Add(new TextEffect(position, text, color, duration));
        QueueRedraw();
    }

    public void HitImpact(Vector2 position, bool heavy)
    {
        _rings.Add(new RingEffect(position, heavy ? new Color(1f, 0.82f, 0.42f) : new Color(1f, 0.45f, 0.45f), 0.35f, 6f, heavy ? 42f : 24f, heavy));
        QueueRedraw();
    }

    public void BuildPulse(Vector2 position)
    {
        _rings.Add(new RingEffect(position, new Color(0.54f, 0.95f, 0.54f), 0.5f, 10f, 46f, false));
        QueueRedraw();
    }

    public void SpawnProjectile(Vector2 start, Vector2 end, Color color, float radius = 4f)
    {
        _projectiles.Add(new ProjectileEffect(start, end, color, radius, 0.28f));
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var ring in _rings)
        {
            var progress = ring.Elapsed / ring.Duration;
            var radius = Mathf.Lerp(ring.StartRadius, ring.EndRadius, progress);
            var alpha = 1f - progress;
            DrawArc(ring.Position, radius, 0f, Mathf.Tau, 36, ring.Color with { A = alpha }, ring.Heavy ? 4f : 2f);
        }

        foreach (var projectile in _projectiles)
        {
            var progress = projectile.Elapsed / projectile.Duration;
            var current = projectile.Start.Lerp(projectile.End, progress);
            DrawLine(projectile.Start, current, projectile.Color with { A = 0.45f }, 2f);
            DrawCircle(current, projectile.Radius, projectile.Color);
        }

        foreach (var text in _texts)
        {
            var progress = text.Elapsed / text.Duration;
            var position = text.Position + new Vector2(0f, -progress * 26f);
            var alpha = 1f - progress;
            DrawString(ThemeDB.FallbackFont, position, text.Text, HorizontalAlignment.Center, -1f, 16, text.Color with { A = alpha });
        }
    }

    private void TickEffects(float delta)
    {
        var redraw = false;

        for (var i = _rings.Count - 1; i >= 0; i--)
        {
            _rings[i].Elapsed += delta;
            if (_rings[i].Elapsed >= _rings[i].Duration)
            {
                _rings.RemoveAt(i);
            }
            redraw = true;
        }

        for (var i = _texts.Count - 1; i >= 0; i--)
        {
            _texts[i].Elapsed += delta;
            if (_texts[i].Elapsed >= _texts[i].Duration)
            {
                _texts.RemoveAt(i);
            }
            redraw = true;
        }

        for (var i = _projectiles.Count - 1; i >= 0; i--)
        {
            _projectiles[i].Elapsed += delta;
            if (_projectiles[i].Elapsed >= _projectiles[i].Duration)
            {
                _rings.Add(new RingEffect(_projectiles[i].End, _projectiles[i].Color, 0.25f, 4f, 22f, false));
                _projectiles.RemoveAt(i);
            }
            redraw = true;
        }

        if (redraw)
        {
            QueueRedraw();
        }
    }

    private sealed record RingEffect(Vector2 Position, Color Color, float Duration, float StartRadius, float EndRadius, bool Heavy)
    {
        public float Elapsed { get; set; }
    }

    private sealed record TextEffect(Vector2 Position, string Text, Color Color, float Duration)
    {
        public float Elapsed { get; set; }
    }

    private sealed record ProjectileEffect(Vector2 Start, Vector2 End, Color Color, float Radius, float Duration)
    {
        public float Elapsed { get; set; }
    }
}
