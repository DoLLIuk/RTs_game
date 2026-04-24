using System;
using System.Collections.Generic;
using Godot;

namespace RtsNaGodote.Game.UI;

public sealed partial class ActionPanel : PanelContainer
{
    private readonly List<HudActionModel> _currentActions = [];
    private Label _headerLabel = null!;
    private Label _statusLabel = null!;
    private GridContainer _actionsGrid = null!;

    public ActionPanel()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        var margin = HudUiFactory.AddMargin(this);
        var column = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);

        _headerLabel = HudUiFactory.CreateLabel(20, new Color(0.93f, 0.86f, 0.68f));
        _headerLabel.Text = GameUiText.CommandsHeader;
        _statusLabel = HudUiFactory.CreateLabel(13, new Color(0.75f, 0.82f, 0.9f, 0.95f));
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _statusLabel.Text = GameUiText.CommandsIdleStatus;

        _actionsGrid = new GridContainer
        {
            Columns = 2,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _actionsGrid.AddThemeConstantOverride("h_separation", 10);
        _actionsGrid.AddThemeConstantOverride("v_separation", 10);

        column.AddChild(_headerLabel);
        column.AddChild(_statusLabel);
        column.AddChild(_actionsGrid);
    }

    public void UpdateActions(IReadOnlyList<HudActionModel> actions, Action<HudActionModel> onPressed)
    {
        if (MatchesCurrent(actions))
        {
            return;
        }

        _currentActions.Clear();
        _currentActions.AddRange(actions);

        foreach (var child in _actionsGrid.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var action in actions)
        {
            var model = action;
            var button = HudUiFactory.CreateActionButton(model.Text);
            button.Disabled = !model.Enabled;
            button.TooltipText = model.Enabled ? model.Description : GameUiText.TooltipWithReason(model.Description, model.DisabledReason);
            button.Pressed += () =>
            {
                if (model.Enabled)
                {
                    onPressed(model);
                }
            };
            button.MouseEntered += () =>
            {
                _statusLabel.Text = GameUiText.HoverStatusText(model.Description, model.Enabled, model.DisabledReason);
            };
            button.MouseExited += () =>
            {
                _statusLabel.Text = GameUiText.CommandsIdleStatus;
            };
            _actionsGrid.AddChild(button);
        }
    }

    public void SetStatusText(string text)
    {
        _statusLabel.Text = text;
    }

    private bool MatchesCurrent(IReadOnlyList<HudActionModel> actions)
    {
        if (_currentActions.Count != actions.Count)
        {
            return false;
        }

        for (var i = 0; i < actions.Count; i++)
        {
            if (_currentActions[i] != actions[i])
            {
                return false;
            }
        }

        return true;
    }
}
