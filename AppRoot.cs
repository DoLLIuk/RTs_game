using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Game.Presentation;
using RtsNaGodote.Game.UI;

namespace RtsNaGodote;

public partial class AppRoot : Node
{
    private RtsGame _game = null!;
    private CanvasLayer _menuLayer = null!;
    private PanelContainer _menuPanel = null!;
    private Button _allianceButton = null!;
    private Button _hordeButton = null!;
    private readonly Button[] _difficultyButtons = new Button[3];
    private readonly Button[] _aiProfileButtons = new Button[2];
    private Race _selectedRace = Race.Alliance;
    private Difficulty _selectedDifficulty = Difficulty.Normal;
    private AiProfile _selectedAiProfile = AiProfile.Push;
    private int _seedCounter = GameConstants.DefaultSeed;

    public override void _Ready()
    {
        var window = GetWindow();
        window.Mode = Window.ModeEnum.Windowed;
        window.Borderless = false;
        window.Size = new Vector2I(GameConstants.ViewWidth, GameConstants.ViewHeight);

        _game = GetNode<RtsGame>("Game");
        _game.StopGame();
        _game.RestartRequested += HandleRestartRequested;
        _game.ReturnToMenuRequested += HandleReturnToMenuRequested;

        BuildMenuUi();
        ShowMenu();
    }

    private void BuildMenuUi()
    {
        _menuLayer = new CanvasLayer();
        AddChild(_menuLayer);

        var blocker = new ColorRect
        {
            Color = new Color(0.05f, 0.06f, 0.08f, 0.98f),
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        blocker.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _menuLayer.AddChild(blocker);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _menuLayer.AddChild(center);

        _menuPanel = new PanelContainer();
        _menuPanel.CustomMinimumSize = new Vector2(520f, 470f);
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.1f, 0.12f, 0.14f, 0.96f),
            CornerRadiusTopLeft = 18,
            CornerRadiusTopRight = 18,
            CornerRadiusBottomLeft = 18,
            CornerRadiusBottomRight = 18
        };
        _menuPanel.AddThemeStyleboxOverride("panel", panelStyle);
        center.AddChild(_menuPanel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 26);
        margin.AddThemeConstantOverride("margin_top", 26);
        margin.AddThemeConstantOverride("margin_right", 26);
        margin.AddThemeConstantOverride("margin_bottom", 26);
        _menuPanel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 16);
        margin.AddChild(column);

        var title = new Label
        {
            Text = GameUiText.MenuTitle,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 36);
        column.AddChild(title);

        var subtitle = new Label
        {
            Text = GameUiText.MenuSubtitle,
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.9f, 0.84f, 0.64f)
        };
        subtitle.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(subtitle);

        column.AddChild(BuildSectionLabel(GameUiText.MenuRaceLabel));
        var raceRow = new HBoxContainer();
        raceRow.AddThemeConstantOverride("separation", 14);
        column.AddChild(raceRow);

        _allianceButton = BuildMenuButton(GameUiText.MenuAlliance);
        _allianceButton.Pressed += () => SetRace(Race.Alliance);
        raceRow.AddChild(_allianceButton);

        _hordeButton = BuildMenuButton(GameUiText.MenuHorde);
        _hordeButton.Pressed += () => SetRace(Race.Horde);
        raceRow.AddChild(_hordeButton);

        column.AddChild(BuildSectionLabel(GameUiText.MenuDifficultyLabel));
        var difficultyRow = new HBoxContainer();
        difficultyRow.AddThemeConstantOverride("separation", 14);
        column.AddChild(difficultyRow);

        var difficulties = new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard };
        for (var i = 0; i < difficulties.Length; i++)
        {
            var difficulty = difficulties[i];
            var button = BuildMenuButton(GameSettings.GetDifficulty(difficulty).Label);
            button.Pressed += () => SetDifficulty(difficulty);
            difficultyRow.AddChild(button);
            _difficultyButtons[i] = button;
        }

        column.AddChild(BuildSectionLabel(GameUiText.MenuAiProfileLabel));
        var aiProfileRow = new HBoxContainer();
        aiProfileRow.AddThemeConstantOverride("separation", 14);
        column.AddChild(aiProfileRow);

        var aiProfiles = new[] { AiProfile.Push, AiProfile.Harass };
        for (var i = 0; i < aiProfiles.Length; i++)
        {
            var profile = aiProfiles[i];
            var text = profile == AiProfile.Push ? GameUiText.MenuAiPush : GameUiText.MenuAiHarass;
            var button = BuildMenuButton(text);
            button.Pressed += () => SetAiProfile(profile);
            aiProfileRow.AddChild(button);
            _aiProfileButtons[i] = button;
        }

        var hint = new Label
        {
            Text = GameUiText.MenuHint,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        hint.AddThemeFontSizeOverride("font_size", 15);
        column.AddChild(hint);

        var startButton = BuildMenuButton(GameUiText.MenuStartBattle, false);
        startButton.CustomMinimumSize = new Vector2(0f, 52f);
        startButton.Pressed += StartSelectedGame;
        column.AddChild(startButton);

        RefreshMenuSelection();
    }

    private Label BuildSectionLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 18);
        label.Modulate = new Color(0.94f, 0.88f, 0.68f);
        return label;
    }

    private Button BuildMenuButton(string text, bool toggle = true)
    {
        var button = new Button
        {
            Text = text,
            ToggleMode = toggle,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(120f, 44f)
        };
        return button;
    }

    private void SetRace(Race race)
    {
        _selectedRace = race;
        RefreshMenuSelection();
    }

    private void SetDifficulty(Difficulty difficulty)
    {
        _selectedDifficulty = difficulty;
        RefreshMenuSelection();
    }

    private void SetAiProfile(AiProfile profile)
    {
        _selectedAiProfile = profile;
        RefreshMenuSelection();
    }

    private void RefreshMenuSelection()
    {
        _allianceButton.ButtonPressed = _selectedRace == Race.Alliance;
        _hordeButton.ButtonPressed = _selectedRace == Race.Horde;
        var difficulties = new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard };
        for (var i = 0; i < difficulties.Length; i++)
        {
            _difficultyButtons[i].ButtonPressed = difficulties[i] == _selectedDifficulty;
        }

        var aiProfiles = new[] { AiProfile.Push, AiProfile.Harass };
        for (var i = 0; i < aiProfiles.Length; i++)
        {
            _aiProfileButtons[i].ButtonPressed = aiProfiles[i] == _selectedAiProfile;
        }
    }

    private void StartSelectedGame()
    {
        _seedCounter++;
        var init = new GameInit(_selectedRace, _selectedDifficulty, _seedCounter, _selectedAiProfile);
        _game.StartGame(init);
        _game.Show();
        _menuLayer.Hide();
    }

    private void HandleRestartRequested()
    {
        StartSelectedGame();
    }

    private void HandleReturnToMenuRequested()
    {
        ShowMenu();
    }

    private void ShowMenu()
    {
        _game.StopGame();
        _menuLayer.Show();
    }
}
