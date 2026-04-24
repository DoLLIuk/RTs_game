using System;
using Godot;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Game.UI;

public sealed partial class GameOverPanel : PanelContainer
{
    private Label _titleLabel = null!;

    public event Action? RestartPressed;
    public event Action? ReturnToMenuPressed;

    public GameOverPanel()
    {
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready()
    {
        var margin = HudUiFactory.AddMargin(this);
        var column = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        column.AddThemeConstantOverride("separation", 18);
        margin.AddChild(column);

        _titleLabel = HudUiFactory.CreateLabel(30);
        _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_titleLabel);

        var restartButton = HudUiFactory.CreateActionButton(GameUiText.GameOverRestart);
        restartButton.Pressed += () => RestartPressed?.Invoke();
        column.AddChild(restartButton);

        var menuButton = HudUiFactory.CreateActionButton(GameUiText.GameOverMenu);
        menuButton.Pressed += () => ReturnToMenuPressed?.Invoke();
        column.AddChild(menuButton);
    }

    public void UpdateWinner(GameSide? winner)
    {
        if (!winner.HasValue)
        {
            Hide();
            return;
        }

        _titleLabel.Text = GameUiText.GameOverTitle(winner.Value);
        _titleLabel.Modulate = winner == GameSide.Player ? new Color(0.35f, 1f, 0.54f) : new Color(1f, 0.4f, 0.4f);
        Show();
    }
}
