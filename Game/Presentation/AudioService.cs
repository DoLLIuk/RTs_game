using System;
using Godot;

namespace RtsNaGodote.Game.Presentation;

public partial class AudioService : Node
{
    private readonly RandomNumberGenerator _rng = new();
    private float _ambientTimer;

    public override void _Process(double delta)
    {
        _ambientTimer -= (float)delta;
        if (_ambientTimer > 0f)
        {
            return;
        }

        _ambientTimer = 6f + _rng.RandfRange(0f, 8f);
        PlayTone(220f + _rng.RandfRange(-15f, 15f), 0.22f, 0.02f, "sine");
        PlayTone(440f + _rng.RandfRange(-30f, 30f), 0.14f, 0.012f, "triangle");
    }

    public void PlaySelect() => PlayTone(640f, 0.05f, 0.04f, "square");
    public void PlayMove() => PlayTone(380f, 0.08f, 0.035f, "triangle");
    public void PlayAttack() => PlayTone(180f, 0.1f, 0.05f, "saw");
    public void PlayBuild() => PlayTone(260f, 0.15f, 0.04f, "triangle");
    public void PlayTrain() => PlayTone(620f, 0.12f, 0.04f, "sine");
    public void PlayGather() => PlayTone(520f, 0.07f, 0.03f, "triangle");
    public void PlayDeposit() => PlayTone(840f, 0.07f, 0.03f, "sine");
    public void PlayImpact() => PlayTone(120f, 0.06f, 0.05f, "noise");
    public void PlayAlert() => PlayTone(300f, 0.18f, 0.05f, "square");
    public void PlayVictory() => PlayChord([523f, 659f, 784f, 1047f], 0.16f, 0.04f);
    public void PlayDefeat() => PlayChord([392f, 349f, 311f, 262f], 0.22f, 0.04f);

    private void PlayChord(float[] frequencies, float duration, float volume)
    {
        foreach (var frequency in frequencies)
        {
            PlayTone(frequency, duration, volume, "triangle");
        }
    }

    private void PlayTone(float frequency, float duration, float volume, string kind)
    {
        var stream = new AudioStreamGenerator
        {
            BufferLength = 0.15f,
            MixRate = 44100
        };
        var player = new AudioStreamPlayer
        {
            Stream = stream,
            VolumeDb = Mathf.LinearToDb(Mathf.Max(volume, 0.0001f))
        };
        AddChild(player);
        player.Finished += () => player.QueueFree();
        player.Play();

        var playback = player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        if (playback is null)
        {
            player.QueueFree();
            return;
        }

        var sampleCount = Mathf.RoundToInt(stream.MixRate * duration);
        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / stream.MixRate;
            var env = 1f - (i / (float)sampleCount);
            var value = kind switch
            {
                "square" => MathF.Sin(MathF.Tau * frequency * t) >= 0f ? 0.8f : -0.8f,
                "triangle" => MathF.Asin(MathF.Sin(MathF.Tau * frequency * t)) * 2f / MathF.PI,
                "saw" => 2f * (t * frequency - MathF.Floor(0.5f + t * frequency)),
                "noise" => _rng.RandfRange(-1f, 1f),
                _ => MathF.Sin(MathF.Tau * frequency * t)
            };
            playback.PushFrame(new Vector2(value * env, value * env));
        }
    }
}
