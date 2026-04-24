using Godot;
using RtsNaGodote.Core.Data;

namespace RtsNaGodote.Game.UI;

internal static class HudUiFactory
{
    public static PanelContainer CreatePanel(Vector2 position, Vector2 size)
    {
        var panel = new PanelContainer
        {
            Position = position,
            Size = size,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
        return panel;
    }

    public static MarginContainer AddMargin(Control parent, int left = 14, int top = 14, int right = 14, int bottom = 14)
    {
        var margin = new MarginContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddThemeConstantOverride("margin_right", right);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
        parent.AddChild(margin);
        return margin;
    }

    public static Label CreateLabel(int fontSize, Color? color = null)
    {
        var label = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.Modulate = color ?? Colors.White;
        return label;
    }

    public static Button CreateActionButton(string text)
    {
        var button = new Button
        {
            Text = text,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 52f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        return button;
    }

    public static PanelContainer CreateCard()
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0f, 74f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.11f, 0.13f, 0.16f, 0.96f),
            BorderColor = new Color(0.4f, 0.44f, 0.5f, 0.82f),
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10
        };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private static StyleBoxFlat CreatePanelStyle()
    {
        return new StyleBoxFlat
        {
            BgColor = GameColors.PanelBackground,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            BorderColor = new Color(0.45f, 0.5f, 0.56f, 0.76f),
            BorderWidthBottom = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2
        };
    }
}
