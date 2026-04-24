using System.Collections.Generic;
using Godot;

namespace RtsNaGodote.Game.UI;

public sealed partial class SelectionPanel : PanelContainer
{
    [Signal]
    public delegate void CancelLastQueuedUnitRequestedEventHandler();

    private Label _titleLabel = null!;
    private Label _statsLabel = null!;
    private Label _summaryLabel = null!;
    private GridContainer _cardsGrid = null!;
    private VBoxContainer _productionBlock = null!;
    private Label _productionCurrentLabel = null!;
    private Label _productionProgressLabel = null!;
    private HBoxContainer _productionSlotsRow = null!;
    private Button _cancelLastButton = null!;

    public SelectionPanel()
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

        _titleLabel = HudUiFactory.CreateLabel(22);
        _statsLabel = HudUiFactory.CreateLabel(16, new Color(0.89f, 0.85f, 0.68f));
        _summaryLabel = HudUiFactory.CreateLabel(15);
        _summaryLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        var cardsTitle = HudUiFactory.CreateLabel(14, new Color(0.72f, 0.78f, 0.85f));
        cardsTitle.Text = GameUiText.SelectionOverviewTitle;

        _cardsGrid = new GridContainer
        {
            Columns = 2,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _cardsGrid.AddThemeConstantOverride("h_separation", 8);
        _cardsGrid.AddThemeConstantOverride("v_separation", 8);

        _productionBlock = new VBoxContainer
        {
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _productionBlock.AddThemeConstantOverride("separation", 6);
        var productionTitle = HudUiFactory.CreateLabel(14, new Color(0.72f, 0.9f, 0.76f));
        productionTitle.Text = GameUiText.ProductionTitle;
        _productionCurrentLabel = HudUiFactory.CreateLabel(14);
        _productionCurrentLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _productionProgressLabel = HudUiFactory.CreateLabel(13, new Color(0.85f, 0.87f, 0.9f));
        _productionSlotsRow = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        _productionSlotsRow.AddThemeConstantOverride("separation", 6);
        _cancelLastButton = HudUiFactory.CreateActionButton(GameUiText.CancelLastButton);
        _cancelLastButton.CustomMinimumSize = new Vector2(0f, 40f);
        _cancelLastButton.Pressed += () => EmitSignal(SignalName.CancelLastQueuedUnitRequested);

        _productionBlock.AddChild(productionTitle);
        _productionBlock.AddChild(_productionCurrentLabel);
        _productionBlock.AddChild(_productionProgressLabel);
        _productionBlock.AddChild(_productionSlotsRow);
        _productionBlock.AddChild(_cancelLastButton);

        column.AddChild(_titleLabel);
        column.AddChild(_statsLabel);
        column.AddChild(_summaryLabel);
        column.AddChild(_productionBlock);
        column.AddChild(cardsTitle);
        column.AddChild(_cardsGrid);
    }

    public void UpdateContent(SelectionPanelModel model)
    {
        _titleLabel.Text = model.Title;
        _statsLabel.Text = model.Stats;
        _summaryLabel.Text = model.Summary;
        UpdateProduction(model.Production);
        RebuildCards(model.Cards);
    }

    private void UpdateProduction(ProductionPanelModel model)
    {
        _productionBlock.Visible = model.Visible;
        if (!model.Visible)
        {
            return;
        }

        _productionCurrentLabel.Text = model.CurrentText;
        _productionProgressLabel.Text = model.ProgressText;
        _cancelLastButton.Disabled = !model.CanCancelLast;
        _cancelLastButton.TooltipText = model.CancelHint;

        foreach (var child in _productionSlotsRow.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var slot in model.Slots)
        {
            var panel = HudUiFactory.CreateCard();
            panel.CustomMinimumSize = new Vector2(72f, 66f);
            var margin = HudUiFactory.AddMargin(panel, 8, 8, 8, 8);
            var column = new VBoxContainer
            {
                MouseFilter = MouseFilterEnum.Ignore
            };
            column.AddThemeConstantOverride("separation", 3);
            margin.AddChild(column);

            var title = HudUiFactory.CreateLabel(12, slot.Active ? new Color(0.62f, 0.92f, 0.72f) : new Color(0.82f, 0.84f, 0.88f));
            title.Text = slot.Label;
            var detail = HudUiFactory.CreateLabel(11, new Color(0.78f, 0.82f, 0.88f, 0.9f));
            detail.Text = slot.Detail;
            detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            column.AddChild(title);
            column.AddChild(detail);
            _productionSlotsRow.AddChild(panel);
        }
    }

    private void RebuildCards(IReadOnlyList<SelectionCardModel> cards)
    {
        foreach (var child in _cardsGrid.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var card in cards)
        {
            var panel = HudUiFactory.CreateCard();
            var accent = new ColorRect
            {
                Color = card.Accent,
                CustomMinimumSize = new Vector2(0f, 4f),
                MouseFilter = MouseFilterEnum.Ignore
            };

            var margin = HudUiFactory.AddMargin(panel, 10, 10, 10, 10);
            var column = new VBoxContainer
            {
                MouseFilter = MouseFilterEnum.Ignore
            };
            column.AddThemeConstantOverride("separation", 4);
            margin.AddChild(column);

            var title = HudUiFactory.CreateLabel(13, new Color(0.7f, 0.76f, 0.84f));
            title.Text = card.Title;
            var value = HudUiFactory.CreateLabel(20, Colors.White);
            value.Text = card.Value;
            var detail = HudUiFactory.CreateLabel(12, new Color(0.78f, 0.82f, 0.88f, 0.9f));
            detail.Text = card.Detail;
            detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            column.AddChild(accent);
            column.AddChild(title);
            column.AddChild(value);
            column.AddChild(detail);
            _cardsGrid.AddChild(panel);
        }
    }
}
