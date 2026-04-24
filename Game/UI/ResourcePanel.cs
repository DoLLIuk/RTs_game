using Godot;

namespace RtsNaGodote.Game.UI;

public sealed partial class ResourcePanel : PanelContainer
{
    private Label _goldValue = null!;
    private Label _lumberValue = null!;
    private Label _foodValue = null!;

    public ResourcePanel()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        var margin = HudUiFactory.AddMargin(this);
        var row = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        row.AddThemeConstantOverride("separation", 24);
        margin.AddChild(row);

        _goldValue = HudUiFactory.CreateLabel(22);
        _lumberValue = HudUiFactory.CreateLabel(22);
        _foodValue = HudUiFactory.CreateLabel(22);
        row.AddChild(_goldValue);
        row.AddChild(_lumberValue);
        row.AddChild(_foodValue);
    }

    public void UpdateValues(int gold, int lumber, int food, int foodCap)
    {
        _goldValue.Text = GameUiText.ResourceGold(gold);
        _lumberValue.Text = GameUiText.ResourceLumber(lumber);
        _foodValue.Text = GameUiText.ResourceFood(food, foodCap);
    }
}
