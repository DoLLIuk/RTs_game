using Godot;

namespace RtsNaGodote.Game.UI;

public sealed partial class StatusHintPanel : PanelContainer
{
    private Label _hintLabel = null!;
    private Label _activityLabel = null!;
    private Label _stateLabel = null!;

    public StatusHintPanel()
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
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        _hintLabel = HudUiFactory.CreateLabel(15, new Color(0.77f, 0.84f, 0.92f));
        _hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _activityLabel = HudUiFactory.CreateLabel(13, new Color(0.68f, 0.74f, 0.8f, 0.86f));
        _activityLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _stateLabel = HudUiFactory.CreateLabel(13, new Color(0.86f, 0.86f, 0.72f, 0.95f));
        _stateLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        column.AddChild(_hintLabel);
        column.AddChild(_stateLabel);
        column.AddChild(_activityLabel);
    }

    public void UpdateContent(string hintText, string stateText, string activityText)
    {
        _hintLabel.Text = hintText;
        _stateLabel.Text = stateText;
        _activityLabel.Text = activityText;
    }
}
