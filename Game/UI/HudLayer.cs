using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Resources;
using RtsNaGodote.Core.Simulation.Units;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Game.UI;

public partial class HudLayer : CanvasLayer
{
    [Signal]
    public delegate void BuildRequestedEventHandler(int kind);

    [Signal]
    public delegate void TrainRequestedEventHandler(int kind);

    [Signal]
    public delegate void CancelQueuedUnitRequestedEventHandler();

    [Signal]
    public delegate void StopRequestedEventHandler();

    [Signal]
    public delegate void AttackMoveRequestedEventHandler();

    [Signal]
    public delegate void CenterRequestedEventHandler();

    [Signal]
    public delegate void RestartRequestedEventHandler();

    [Signal]
    public delegate void ReturnToMenuRequestedEventHandler();

    [Signal]
    public delegate void PauseResumeRequestedEventHandler();

    [Signal]
    public delegate void DebugModeChangedEventHandler(bool enabled);

    [Signal]
    public delegate void MinimapMoveRequestedEventHandler(Vector2 worldPosition);

    private ResourcePanel _resourcePanel = null!;
    private SelectionPanel _selectionPanel = null!;
    private ActionPanel _actionPanel = null!;
    private StatusHintPanel _statusPanel = null!;
    private PanelContainer _minimapPanel = null!;
    private MinimapControl _minimap = null!;
    private Label _flashMessage = null!;
    private Label _debugOverlay = null!;
    private GameOverPanel _gameOverPanel = null!;
    private PanelContainer _pausePanel = null!;
    private VBoxContainer _pauseMenuColumn = null!;
    private VBoxContainer _pauseSettingsColumn = null!;
    private CheckBox _debugModeCheckBox = null!;
    private HudViewState? _lastViewState;

    public override void _Ready()
    {
        Layer = 5;
        BuildUi();
        HideHud();
    }

    public void ShowHud()
    {
        Show();
        _flashMessage.Hide();
        _debugOverlay.Hide();
        _gameOverPanel.UpdateWinner(null);
        _pausePanel.Hide();
    }

    public void HideHud()
    {
        Hide();
        _flashMessage.Hide();
        _debugOverlay.Hide();
        _gameOverPanel.UpdateWinner(null);
        _pausePanel.Hide();
        _lastViewState = null;
    }

    public bool IsPauseVisible() => _pausePanel.Visible;

    public void ShowPauseMenu()
    {
        ShowPauseMain();
        _pausePanel.Show();
    }

    public void HidePauseMenu()
    {
        ShowPauseMain();
        _pausePanel.Hide();
    }

    public void SetDebugMode(bool enabled)
    {
        _debugModeCheckBox.ButtonPressed = enabled;
    }

    public void UpdateDebugOverlay(bool enabled, string text)
    {
        if (!enabled)
        {
            _debugOverlay.Hide();
            return;
        }

        _debugOverlay.Text = text;
        _debugOverlay.Show();
    }

    public void UpdateState(
        PlayerState player,
        IReadOnlyList<SimUnit> selectedUnits,
        SimBuilding? selectedBuilding,
        IReadOnlyList<SimBuilding> playerBuildings,
        string lastCommand,
        BuildingKind? placementKind,
        bool attackMoveMode,
        SimUnit? hoveredUnit,
        SimBuilding? hoveredBuilding,
        SimResourceNode? hoveredResource,
        Vector2I hoveredTile,
        GameSide? winner,
        Race playerRace,
        MinimapState minimapState)
    {
        var presentation = HudStateBuilder.Build(new HudPresenterInput(
            player,
            selectedUnits,
            selectedBuilding,
            playerBuildings,
            lastCommand,
            placementKind,
            attackMoveMode,
            hoveredUnit,
            hoveredBuilding,
            hoveredResource,
            hoveredTile,
            winner,
            playerRace,
            minimapState));

        _resourcePanel.UpdateValues(player.Gold, player.Lumber, player.Food, player.FoodCap);
        _minimap.SetState(minimapState);

        if (_lastViewState is null || _lastViewState.SelectionSignature != presentation.ViewState.SelectionSignature)
        {
            _selectionPanel.UpdateContent(presentation.SelectionModel);
        }

        if (_lastViewState is null ||
            _lastViewState.HintText != presentation.ViewState.HintText ||
            _lastViewState.ActivityText != presentation.ViewState.ActivityText ||
            _lastViewState.StatusSignature != presentation.ViewState.StatusSignature)
        {
            _statusPanel.UpdateContent(presentation.HintText, presentation.StatusText, presentation.ActivityText);
        }

        if (_lastViewState is null || _lastViewState.ActionsSignature != presentation.ViewState.ActionsSignature)
        {
            _actionPanel.UpdateActions(presentation.Actions, HandleActionPressed);
        }

        if (_lastViewState is null || _lastViewState.Winner != winner)
        {
            _gameOverPanel.UpdateWinner(winner);
        }

        _lastViewState = presentation.ViewState;
    }

    public void ShowMessage(string text)
    {
        _flashMessage.Text = text;
        _flashMessage.Modulate = Colors.White;
        _flashMessage.Show();
        var tween = CreateTween();
        tween.TweenProperty(_flashMessage, "modulate:a", 1f, 0.01f);
        tween.TweenInterval(1.4f);
        tween.TweenProperty(_flashMessage, "modulate:a", 0f, 0.35f);
        tween.Finished += () => _flashMessage.Hide();
    }

    private void BuildUi()
    {
        _resourcePanel = new ResourcePanel
        {
            Size = new Vector2(560f, 82f)
        };
        PinTopLeft(_resourcePanel, 18f, 16f);
        _selectionPanel = new SelectionPanel
        {
            Size = new Vector2(620f, 358f)
        };
        PinBottomLeft(_selectionPanel, 18f, 76f);
        _selectionPanel.CancelLastQueuedUnitRequested += () => EmitSignal(SignalName.CancelQueuedUnitRequested);
        _actionPanel = new ActionPanel
        {
            Size = new Vector2(452f, 358f)
        };
        PinBottomLeft(_actionPanel, 656f, 76f);
        _statusPanel = new StatusHintPanel
        {
            Size = new Vector2(620f, 156f)
        };
        PinTopLeft(_statusPanel, 18f, 114f);

        AddChild(_resourcePanel);
        AddChild(_selectionPanel);
        AddChild(_actionPanel);
        AddChild(_statusPanel);

        _minimapPanel = HudUiFactory.CreatePanel(Vector2.Zero, new Vector2(252f, 294f));
        PinTopRight(_minimapPanel, 18f, 16f);
        var minimapMargin = HudUiFactory.AddMargin(_minimapPanel);
        var minimapColumn = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        minimapColumn.AddThemeConstantOverride("separation", 10);
        minimapMargin.AddChild(minimapColumn);
        var minimapTitle = HudUiFactory.CreateLabel(18, new Color(0.93f, 0.86f, 0.68f));
        minimapTitle.Text = GameUiText.MinimapTitle;
        minimapColumn.AddChild(minimapTitle);
        _minimap = new MinimapControl();
        _minimap.WorldPointRequested += point => EmitSignal(SignalName.MinimapMoveRequested, point);
        minimapColumn.AddChild(_minimap);
        AddChild(_minimapPanel);

        _flashMessage = HudUiFactory.CreateLabel(24);
        _flashMessage.HorizontalAlignment = HorizontalAlignment.Center;
        _flashMessage.Size = new Vector2(640f, 42f);
        PinTopCenter(_flashMessage, 34f);
        _flashMessage.Hide();
        AddChild(_flashMessage);

        _debugOverlay = HudUiFactory.CreateLabel(18, new Color(0.98f, 0.92f, 0.62f));
        _debugOverlay.Size = new Vector2(420f, 54f);
        _debugOverlay.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _debugOverlay.HorizontalAlignment = HorizontalAlignment.Right;
        PinTopRight(_debugOverlay, 18f, 320f);
        _debugOverlay.Hide();
        AddChild(_debugOverlay);

        _gameOverPanel = new GameOverPanel
        {
            Size = new Vector2(420f, 240f)
        };
        CenterControl(_gameOverPanel);
        _gameOverPanel.RestartPressed += () => EmitSignal(SignalName.RestartRequested);
        _gameOverPanel.ReturnToMenuPressed += () => EmitSignal(SignalName.ReturnToMenuRequested);
        _gameOverPanel.Hide();
        AddChild(_gameOverPanel);

        _pausePanel = HudUiFactory.CreatePanel(Vector2.Zero, new Vector2(460f, 290f));
        CenterControl(_pausePanel);
        var pauseMargin = HudUiFactory.AddMargin(_pausePanel);
        _pauseMenuColumn = new VBoxContainer();
        _pauseMenuColumn.AddThemeConstantOverride("separation", 16);
        pauseMargin.AddChild(_pauseMenuColumn);

        var pauseTitle = HudUiFactory.CreateLabel(30);
        pauseTitle.Text = GameUiText.PauseTitle;
        pauseTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _pauseMenuColumn.AddChild(pauseTitle);

        var resumeButton = HudUiFactory.CreateActionButton(GameUiText.PauseResume);
        resumeButton.Pressed += () => EmitSignal(SignalName.PauseResumeRequested);
        _pauseMenuColumn.AddChild(resumeButton);

        var settingsButton = HudUiFactory.CreateActionButton(GameUiText.PauseSettings);
        settingsButton.Pressed += ShowPauseSettings;
        _pauseMenuColumn.AddChild(settingsButton);

        var mainMenuButton = HudUiFactory.CreateActionButton(GameUiText.PauseMainMenu);
        mainMenuButton.Pressed += () => EmitSignal(SignalName.ReturnToMenuRequested);
        _pauseMenuColumn.AddChild(mainMenuButton);

        _pauseSettingsColumn = new VBoxContainer();
        _pauseSettingsColumn.AddThemeConstantOverride("separation", 12);
        _pauseSettingsColumn.Hide();
        pauseMargin.AddChild(_pauseSettingsColumn);

        var settingsTitle = HudUiFactory.CreateLabel(30);
        settingsTitle.Text = GameUiText.PauseSettingsTitle;
        settingsTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _pauseSettingsColumn.AddChild(settingsTitle);

        _debugModeCheckBox = new CheckBox
        {
            Text = GameUiText.PauseDebugMode,
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _debugModeCheckBox.Toggled += enabled => EmitSignal(SignalName.DebugModeChanged, enabled);
        _pauseSettingsColumn.AddChild(_debugModeCheckBox);

        var debugHint = HudUiFactory.CreateLabel(14, new Color(0.78f, 0.82f, 0.88f));
        debugHint.Text = GameUiText.PauseDebugModeHint;
        debugHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pauseSettingsColumn.AddChild(debugHint);

        var backButton = HudUiFactory.CreateActionButton(GameUiText.PauseSettingsBack);
        backButton.Pressed += ShowPauseMain;
        _pauseSettingsColumn.AddChild(backButton);

        _pausePanel.Hide();
        AddChild(_pausePanel);
    }

    private void ShowPauseMain()
    {
        _pauseMenuColumn.Show();
        _pauseSettingsColumn.Hide();
    }

    private void ShowPauseSettings()
    {
        _pauseMenuColumn.Hide();
        _pauseSettingsColumn.Show();
    }

    private static void PinTopLeft(Control control, float left, float top)
    {
        control.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        control.Position = new Vector2(left, top);
    }

    private static void PinTopRight(Control control, float right, float top)
    {
        control.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        control.Position = new Vector2(-right - control.Size.X, top);
    }

    private static void PinBottomLeft(Control control, float left, float bottom)
    {
        control.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        control.Position = new Vector2(left, -bottom - control.Size.Y);
    }

    private static void PinTopCenter(Control control, float top)
    {
        control.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        control.Position = new Vector2(-control.Size.X / 2f, top);
    }

    private static void CenterControl(Control control)
    {
        control.SetAnchorsPreset(Control.LayoutPreset.Center);
        control.Position = new Vector2(-control.Size.X / 2f, -control.Size.Y / 2f);
    }

    private void HandleActionPressed(HudActionModel action)
    {
        switch (action.Kind)
        {
            case HudActionKind.Build:
                EmitSignal(SignalName.BuildRequested, action.Payload);
                break;
            case HudActionKind.Train:
                EmitSignal(SignalName.TrainRequested, action.Payload);
                break;
            case HudActionKind.CancelQueue:
                EmitSignal(SignalName.CancelQueuedUnitRequested);
                break;
            case HudActionKind.AttackMove:
                EmitSignal(SignalName.AttackMoveRequested);
                break;
            case HudActionKind.Stop:
                EmitSignal(SignalName.StopRequested);
                break;
            case HudActionKind.Center:
                EmitSignal(SignalName.CenterRequested);
                break;
        }
    }
}
