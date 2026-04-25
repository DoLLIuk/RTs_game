using System;
using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Resources;
using RtsNaGodote.Core.Simulation.Units;
using RtsNaGodote.Game.UI;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Game.Presentation;

public partial class RtsGame : Node2D
{
    private sealed record MinimapPingState(Vector2 Position, Color Color, double ExpiresAtMs);

    [Signal]
    public delegate void RestartRequestedEventHandler();

    [Signal]
    public delegate void ReturnToMenuRequestedEventHandler();

    private Camera2D _camera = null!;
    private MapView _mapView = null!;
    private FogOverlayView _fogOverlayView = null!;
    private EffectsLayer _effectsLayer = null!;
    private WorldOverlayView _overlay = null!;
    private Node2D _resourcesLayer = null!;
    private Node2D _buildingsLayer = null!;
    private Node2D _unitsLayer = null!;
    private HudLayer _hud = null!;
    private AudioService _audio = null!;

    private GameSimulation? _simulation;
    private FogOfWar? _fog;
    private GameInit _currentInit;
    private readonly Dictionary<int, UnitView> _unitViews = [];
    private readonly Dictionary<int, BuildingView> _buildingViews = [];
    private readonly Dictionary<int, ResourceView> _resourceViews = [];
    private readonly Dictionary<int, RememberedBuildingState> _rememberedBuildings = [];
    private readonly Dictionary<int, RememberedResourceState> _rememberedResources = [];
    private readonly List<SimUnit> _selectedUnits = [];
    private readonly List<MinimapPingState> _minimapPings = [];
    private SimBuilding? _selectedBuilding;
    private BuildingKind? _placementKind;
    private bool _attackMoveMode;
    private bool _isActive;
    private bool _isPaused;
    private bool _debugModeEnabled;
    private string _lastCommand = GameUiText.LastCommandNone;
    private Vector2? _selectionStartWorld;
    private Vector2? _selectionCurrentWorld;
    private bool _selectionAdditive;
    private double _lastUnderAttackNotificationMs = -99999d;
    private long _simulationTickCount;
    private int _tickCounterWindow;
    private double _tickCounterWindowMs;
    private int _ticksPerSecond;

    public override void _Ready()
    {
        _camera = GetNode<Camera2D>("Camera2D");
        _mapView = GetNode<MapView>("World/MapView");
        _fogOverlayView = GetNode<FogOverlayView>("World/FogOverlay");
        _effectsLayer = GetNode<EffectsLayer>("World/EffectsLayer");
        _overlay = GetNode<WorldOverlayView>("Overlay");
        _resourcesLayer = GetNode<Node2D>("World/Resources");
        _buildingsLayer = GetNode<Node2D>("World/Buildings");
        _unitsLayer = GetNode<Node2D>("World/Units");
        _hud = GetNode<HudLayer>("HUD");

        _audio = new AudioService();
        AddChild(_audio);

        _hud.BuildRequested += kind => BeginPlacement((BuildingKind)kind);
        _hud.TrainRequested += kind => TryQueueSelectedBuilding((UnitKind)kind);
        _hud.CancelQueuedUnitRequested += TryCancelSelectedQueuedUnit;
        _hud.StopRequested += StopSelectedUnits;
        _hud.AttackMoveRequested += () =>
        {
            if (GetControllableSelectedUnits().Count > 0)
            {
                _attackMoveMode = true;
                _hud.ShowMessage(GameUiText.MessageAttackMoveArmed);
            }
        };
        _hud.CenterRequested += CenterOnTownHall;
        _hud.RestartRequested += () => EmitSignal(SignalName.RestartRequested);
        _hud.ReturnToMenuRequested += () => EmitSignal(SignalName.ReturnToMenuRequested);
        _hud.PauseResumeRequested += TogglePause;
        _hud.DebugModeChanged += OnDebugModeChanged;
        _hud.MinimapMoveRequested += point =>
        {
            _camera.Position = point;
            ClampCamera(GetViewportRect().Size);
        };

        _effectsLayer.SetProcess(false);
        _audio.SetProcess(false);
        _overlay.Hide();
        _hud.HideHud();
        Hide();
        SetProcess(false);
        SetProcessUnhandledInput(false);
    }

    public void StartGame(GameInit init)
    {
        _currentInit = init;
        InitializeSimulation(init);
        Show();
        SetProcess(true);
        SetProcessUnhandledInput(true);
        _effectsLayer.SetProcess(true);
        _audio.SetProcess(true);
        _overlay.Show();
        _hud.ShowHud();
    }

    public void StopGame()
    {
        _isActive = false;
        _isPaused = false;
        _attackMoveMode = false;
        _selectionStartWorld = null;
        _selectionCurrentWorld = null;
        SetProcess(false);
        SetProcessUnhandledInput(false);
        _effectsLayer.SetProcess(false);
        _audio.SetProcess(false);
        _overlay.Hide();
        _overlay.SyncState(null, null, Vector2.Zero, null, null, null, null, null);
        _hud.HideHud();
        _simulationTickCount = 0;
        _tickCounterWindow = 0;
        _tickCounterWindowMs = 0d;
        _ticksPerSecond = 0;
        Hide();
    }

    public override void _Process(double delta)
    {
        if (!_isActive || _simulation is null || _fog is null)
        {
            return;
        }

        if (_isPaused)
        {
            UpdateDebugOverlay(delta, false);
            SyncHud();
            return;
        }

        UpdateFog();
        PushPlayerVisionSnapshot();
        _simulation.Update(delta);
        _simulationTickCount++;
        UpdateDebugOverlay(delta, true);
        UpdateFog();
        PushPlayerVisionSnapshot();
        UpdateCamera(delta);
        SyncViews();
        SyncHud();
        _fogOverlayView.Refresh();
        PruneMinimapPings();
        var mouseWorld = GetGlobalMousePosition();
        var hoveredUnit = FindUnitAt(mouseWorld);
        var hoveredBuilding = hoveredUnit is null ? FindBuildingAt(mouseWorld) : null;
        var hoveredResource = FindResourceAt(mouseWorld);
        _overlay.SyncState(_simulation, _placementKind, mouseWorld, _selectionStartWorld, _selectionCurrentWorld, hoveredUnit, hoveredBuilding, hoveredResource);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_isActive || _simulation is null)
        {
            return;
        }

        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            HandleKeyPress(keyEvent.Keycode);
            return;
        }

        if (_isPaused)
        {
            return;
        }

        if (_selectionStartWorld.HasValue)
        {
            if (@event is InputEventMouseMotion)
            {
                _selectionCurrentWorld = GetGlobalMousePosition();
                return;
            }

            if (@event is InputEventMouseButton selectionMouseEvent &&
                selectionMouseEvent.ButtonIndex == MouseButton.Left &&
                !selectionMouseEvent.Pressed)
            {
                HandleLeftMouse(selectionMouseEvent, GetGlobalMousePosition());
                return;
            }
        }

        if (@event is InputEventMouse && IsPointerOverHud())
        {
            return;
        }

        if (@event is not InputEventMouseButton mouseEvent)
        {
            return;
        }

        var worldPosition = GetGlobalMousePosition();
        if (mouseEvent.ButtonIndex == MouseButton.Left)
        {
            HandleLeftMouse(mouseEvent, worldPosition);
        }
        else if (mouseEvent.ButtonIndex == MouseButton.Right && mouseEvent.Pressed)
        {
            HandleRightMouse(worldPosition);
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!_isActive || _simulation is null || _isPaused)
        {
            return;
        }

        if (@event is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed)
        {
            return;
        }

        if (mouseEvent.ButtonIndex != MouseButton.WheelUp && mouseEvent.ButtonIndex != MouseButton.WheelDown)
        {
            return;
        }

        if (IsPointerOverHud())
        {
            return;
        }

        var delta = mouseEvent.ButtonIndex == MouseButton.WheelUp
            ? GameConstants.CameraZoomStep
            : -GameConstants.CameraZoomStep;
        AdjustZoom(delta);
        GetViewport().SetInputAsHandled();
    }

    private void InitializeSimulation(GameInit init)
    {
        UnsubscribeSimulationEvents();

        foreach (var view in _unitViews.Values)
        {
            view.QueueFree();
        }

        foreach (var view in _buildingViews.Values)
        {
            view.QueueFree();
        }

        foreach (var view in _resourceViews.Values)
        {
            view.QueueFree();
        }

        _unitViews.Clear();
        _buildingViews.Clear();
        _resourceViews.Clear();
        _rememberedBuildings.Clear();
        _rememberedResources.Clear();
        _selectedUnits.Clear();
        _selectedBuilding = null;
        _placementKind = null;
        _attackMoveMode = false;
        _isPaused = false;
        _lastCommand = GameUiText.LastCommandNone;
        _selectionStartWorld = null;
        _selectionCurrentWorld = null;
        _lastUnderAttackNotificationMs = -99999d;
        _minimapPings.Clear();
        _simulationTickCount = 0;
        _tickCounterWindow = 0;
        _tickCounterWindowMs = 0d;
        _ticksPerSecond = 0;

        _simulation = new GameSimulation(init);
        SubscribeSimulationEvents(_simulation);
        _fog = new FogOfWar(_simulation.Map);
        _mapView.SetMap(_simulation.Map);
        _fogOverlayView.SetFog(_fog);
        ConfigureCamera();
        AutoSelectFirstWorker();
        _hud.SetDebugMode(_debugModeEnabled);
        UpdateFog();
        PushPlayerVisionSnapshot();
        SyncViews();
        SyncHud();
        _isActive = true;
        _hud.ShowMessage(GameUiText.BattleStarted(init.PlayerRace, GameSettings.GetDifficulty(init.Difficulty).Label, init.AiProfile));
    }

    private void PushPlayerVisionSnapshot()
    {
        if (_simulation is null || _fog is null)
        {
            return;
        }

        var snapshot = new PlayerVisionSnapshot(_fog.Width, _fog.Height);
        for (var y = 0; y < _fog.Height; y++)
        {
            for (var x = 0; x < _fog.Width; x++)
            {
                snapshot.SetVisible(x, y, _fog.IsVisible(x, y));
            }
        }

        _simulation.UpdatePlayerVisionSnapshot(snapshot);
    }

    private void SubscribeSimulationEvents(GameSimulation simulation)
    {
        simulation.ProjectileLaunched += OnProjectileLaunched;
        simulation.HitOccurred += OnHitOccurred;
        simulation.EntityDestroyed += OnEntityDestroyed;
        simulation.BuildingCompleted += OnBuildingCompleted;
        simulation.UnitProduced += OnUnitProduced;
        simulation.ResourceGathered += OnResourceGathered;
        simulation.ResourceDeposited += OnResourceDeposited;
        simulation.UnderAttack += OnUnderAttack;
        simulation.GameOverResolved += OnGameOverResolved;
    }

    private void UnsubscribeSimulationEvents()
    {
        if (_simulation is null)
        {
            return;
        }

        _simulation.ProjectileLaunched -= OnProjectileLaunched;
        _simulation.HitOccurred -= OnHitOccurred;
        _simulation.EntityDestroyed -= OnEntityDestroyed;
        _simulation.BuildingCompleted -= OnBuildingCompleted;
        _simulation.UnitProduced -= OnUnitProduced;
        _simulation.ResourceGathered -= OnResourceGathered;
        _simulation.ResourceDeposited -= OnResourceDeposited;
        _simulation.UnderAttack -= OnUnderAttack;
        _simulation.GameOverResolved -= OnGameOverResolved;
    }

    private void OnProjectileLaunched(Vector2 start, Vector2 end, GameSide side, bool siege, bool tower)
    {
        if (!ShouldDisplayPosition(end))
        {
            return;
        }

        var color = tower
            ? new Color(0.58f, 0.85f, 1f)
            : siege
                ? new Color(0.88f, 0.7f, 0.4f)
                : new Color(0.94f, 0.84f, 0.55f);
        _effectsLayer.SpawnProjectile(start, end, color, siege ? 5f : 3.2f);
        _audio.PlayAttack();
    }

    private void OnHitOccurred(Vector2 position, bool isBuilding, int amount)
    {
        if (!ShouldDisplayPosition(position))
        {
            return;
        }

        _effectsLayer.HitImpact(position, isBuilding || amount >= 20);
        _effectsLayer.FloatingText(position + new Vector2(0f, -12f), $"-{amount}", new Color(1f, 0.72f, 0.58f));
        _audio.PlayImpact();
    }

    private void OnEntityDestroyed(Vector2 position, bool isBuilding, GameSide side)
    {
        if (!ShouldDisplayPosition(position))
        {
            return;
        }

        _effectsLayer.HitImpact(position, true);
        _effectsLayer.CommandMarker(position, side == GameSide.Player ? GameColors.Player : GameColors.AI, isBuilding ? GameUiText.MarkerBuildingDown : GameUiText.MarkerUnitDown);
    }

    private void OnBuildingCompleted(Vector2 position, GameSide side)
    {
        _effectsLayer.BuildPulse(position);
        if (side == GameSide.Player)
        {
            _hud.ShowMessage(GameUiText.MessageConstructionComplete);
            _audio.PlayBuild();
        }
    }

    private void OnUnitProduced(Vector2 position, GameSide side)
    {
        if (side != GameSide.Player || !_isActive)
        {
            return;
        }

        _effectsLayer.CommandMarker(position, GameColors.Player, GameUiText.MarkerUnitReady);
        _audio.PlayTrain();
    }

    private void OnResourceGathered(Vector2 position, ResourceType type, int amount, GameSide side)
    {
        if (side != GameSide.Player || !ShouldDisplayPosition(position))
        {
            return;
        }

        var color = type == ResourceType.Gold ? GameColors.GoldMine : new Color(0.55f, 0.86f, 0.45f);
        _effectsLayer.BuildPulse(position);
        _effectsLayer.FloatingText(position + new Vector2(0f, -10f), $"+{amount}", color);
        _audio.PlayGather();
    }

    private void OnResourceDeposited(Vector2 position, ResourceType type, int amount, GameSide side)
    {
        if (side != GameSide.Player)
        {
            return;
        }

        var color = type == ResourceType.Gold ? GameColors.GoldMine : new Color(0.55f, 0.86f, 0.45f);
        _effectsLayer.CommandMarker(position, color, $"+{amount}");
        _audio.PlayDeposit();
    }

    private void OnUnderAttack(Vector2 position)
    {
        if (_simulation is null || _simulation.GameOver)
        {
            return;
        }

        if (_fog is not null)
        {
            var tile = _simulation.Map.WorldToTile(position);
            _fog.RevealCircle(tile.X, tile.Y, GameConstants.UnderAttackRevealTiles);
        }

        var now = Time.GetTicksMsec();
        if (now - _lastUnderAttackNotificationMs < 2800d)
        {
            return;
        }

        _lastUnderAttackNotificationMs = now;
        _hud.ShowMessage(GameUiText.MessageUnderAttack);
        _effectsLayer.CommandMarker(position, new Color(1f, 0.45f, 0.3f), GameUiText.MarkerAlert);
        _audio.PlayAlert();
        _minimapPings.Add(new MinimapPingState(position, new Color(1f, 0.35f, 0.3f), Time.GetTicksMsec() + 2200d));
    }

    private void OnGameOverResolved(GameSide winner)
    {
        _hud.ShowMessage(winner == GameSide.Player ? GameUiText.MessageVictory : GameUiText.MessageDefeat);
        if (winner == GameSide.Player)
        {
            _audio.PlayVictory();
        }
        else
        {
            _audio.PlayDefeat();
        }
    }

    private void AutoSelectFirstWorker()
    {
        if (_simulation is null)
        {
            return;
        }

        var worker = _simulation.Units.Find(unit => unit.Side == GameSide.Player && unit.Kind == UnitKind.Worker);
        if (worker is null)
        {
            return;
        }

        _selectedUnits.Add(worker);
        _selectedBuilding = null;
    }

    private void UpdateFog()
    {
        if (_simulation is null || _fog is null)
        {
            return;
        }

        _fog.DimVisible();
        foreach (var building in _simulation.Buildings)
        {
            if (!building.Alive || building.Side != GameSide.Player)
            {
                continue;
            }

            var centerTile = building.CenterTile();
            _fog.RevealCircle(centerTile.X, centerTile.Y, building.Sight);
        }

        foreach (var unit in _simulation.Units)
        {
            if (!unit.Alive || unit.Side != GameSide.Player)
            {
                continue;
            }

            var tile = _simulation.Map.WorldToTile(unit.Position);
            _fog.RevealCircle(tile.X, tile.Y, unit.Sight);
        }
    }

    private void HandleLeftMouse(InputEventMouseButton mouseEvent, Vector2 worldPosition)
    {
        if (mouseEvent.Pressed)
        {
            if (_placementKind.HasValue)
            {
                TryPlaceBuilding(worldPosition);
                return;
            }

            if (_attackMoveMode)
            {
                IssueAttackMoveGroup(worldPosition);
                _attackMoveMode = false;
                return;
            }

            _selectionStartWorld = worldPosition;
            _selectionCurrentWorld = worldPosition;
            _selectionAdditive = mouseEvent.ShiftPressed;
            return;
        }

        if (!_selectionStartWorld.HasValue)
        {
            return;
        }

        var start = _selectionStartWorld.Value;
        var end = worldPosition;
        if (start.DistanceTo(end) < GameConstants.SelectionDragThreshold)
        {
            ClickSelect(end, _selectionAdditive);
        }
        else
        {
            BoxSelect(new Rect2(start, end - start).Abs(), _selectionAdditive);
        }

        _selectionStartWorld = null;
        _selectionCurrentWorld = null;
    }

    private void HandleRightMouse(Vector2 worldPosition)
    {
        if (_simulation is null)
        {
            return;
        }

        if (_placementKind.HasValue)
        {
            CancelPlacement();
            return;
        }

        if (_selectedBuilding is not null && _selectedUnits.Count == 0)
        {
            if (_selectedBuilding.Side == GameSide.Player)
            {
                _simulation.SetRallyPoint(_selectedBuilding, worldPosition);
                _lastCommand = GameUiText.CommandRally(FormatVector(worldPosition));
                _effectsLayer.CommandMarker(worldPosition, GameColors.Selection, GameUiText.MarkerRally);
            }

            return;
        }

        var controllableUnits = GetControllableSelectedUnits();
        if (controllableUnits.Count == 0)
        {
            return;
        }

        if (_attackMoveMode)
        {
            IssueAttackMoveGroup(worldPosition);
            _attackMoveMode = false;
            return;
        }

        var unit = FindUnitAt(worldPosition);
        var building = unit is null ? FindBuildingAt(worldPosition) : null;
        var resource = FindResourceAt(worldPosition);

        if (unit is not null && unit.Side != GameSide.Player)
        {
            foreach (var selected in controllableUnits)
            {
                _simulation.IssueAttack(selected, unit);
            }

            _lastCommand = GameUiText.CommandAttackUnit(unit.Id);
            _effectsLayer.CommandMarker(worldPosition, new Color(1f, 0.42f, 0.42f), GameUiText.MarkerAttack);
            return;
        }

        if (building is not null && building.Side != GameSide.Player)
        {
            foreach (var selected in controllableUnits)
            {
                _simulation.IssueAttack(selected, building);
            }

            _lastCommand = GameUiText.CommandAttackBuilding(building.Id);
            _effectsLayer.CommandMarker(worldPosition, new Color(1f, 0.42f, 0.42f), GameUiText.MarkerAttack);
            return;
        }

        if (resource is not null)
        {
            var issued = false;
            foreach (var selected in controllableUnits)
            {
                if (!selected.IsWorker())
                {
                    continue;
                }

                _simulation.IssueGather(selected, resource);
                issued = true;
            }

            if (issued)
            {
                _lastCommand = GameUiText.CommandGather(resource.Type, resource.Id);
                _effectsLayer.CommandMarker(worldPosition, resource.Type == ResourceType.Gold ? GameColors.GoldMine : new Color(0.55f, 0.86f, 0.45f), GameUiText.MarkerGather);
                return;
            }
        }

        if (building is not null && building.Side == GameSide.Player && !building.Completed)
        {
            var issued = false;
            foreach (var selected in controllableUnits)
            {
                if (!selected.IsWorker())
                {
                    continue;
                }

                _simulation.IssueBuild(selected, building);
                issued = true;
            }

            if (issued)
            {
                _lastCommand = GameUiText.CommandBuild(building.Id);
                _effectsLayer.CommandMarker(worldPosition, new Color(0.52f, 1f, 0.55f), GameUiText.MarkerBuild);
                return;
            }
        }

        if (building is not null && building.Side == GameSide.Player && building.Kind == BuildingKind.TownHall)
        {
            var returned = false;
            foreach (var selected in controllableUnits)
            {
                if (!selected.IsWorker() || selected.CargoType is null || selected.CargoAmount <= 0)
                {
                    continue;
                }

                _simulation.IssueReturnCargo(selected, building);
                returned = true;
            }

            if (returned)
            {
                _lastCommand = GameUiText.CommandReturnCargo(building.Id);
                _effectsLayer.CommandMarker(worldPosition, new Color(0.48f, 0.85f, 1f), GameUiText.MarkerDeposit);
                return;
            }
        }

        if (unit is not null && unit.Side == GameSide.Player)
        {
            IssueMoveGroup(controllableUnits, worldPosition, unit.Position, unit.Radius);
            return;
        }

        if (building is not null && building.Side == GameSide.Player)
        {
            IssueMoveGroup(controllableUnits, worldPosition, building.Center, building.Radius);
            return;
        }

        IssueMoveGroup(controllableUnits, worldPosition);
    }

    private void ClickSelect(Vector2 worldPosition, bool additive)
    {
        var unit = FindUnitAt(worldPosition);
        if (unit is not null)
        {
            if (!additive)
            {
                _selectedUnits.Clear();
            }

            _selectedBuilding = null;
            if (!_selectedUnits.Contains(unit))
            {
                _selectedUnits.Add(unit);
            }

            _lastCommand = GameUiText.CommandSelectUnit(unit.Id);
            _audio.PlaySelect();
            return;
        }

        var building = FindBuildingAt(worldPosition);
        if (building is not null)
        {
            _selectedUnits.Clear();
            _selectedBuilding = building;
            _lastCommand = GameUiText.CommandSelectBuilding(building.Id);
            _audio.PlaySelect();
            return;
        }

        if (_selectedUnits.Count > 0 || _selectedBuilding is not null)
        {
            _lastCommand = GameUiText.CommandSelectNone();
        }
    }

    private void BoxSelect(Rect2 worldRect, bool additive)
    {
        if (_simulation is null)
        {
            return;
        }

        if (!additive)
        {
            _selectedUnits.Clear();
        }

        _selectedBuilding = null;
        foreach (var unit in _simulation.Units)
        {
            if (!unit.Alive || unit.Side != GameSide.Player || !worldRect.HasPoint(unit.Position))
            {
                continue;
            }

            if (!_selectedUnits.Contains(unit))
            {
                _selectedUnits.Add(unit);
            }
        }

        _lastCommand = GameUiText.CommandBoxSelect(_selectedUnits.Count);
        if (_selectedUnits.Count > 0)
        {
            _audio.PlaySelect();
        }
    }

    private void HandleKeyPress(Key key)
    {
        switch (key)
        {
            case Key.Escape:
                if (_placementKind.HasValue)
                {
                    CancelPlacement();
                    _attackMoveMode = false;
                    break;
                }

                TogglePause();
                break;
            case Key.Space:
                CenterOnTownHall();
                break;
            case Key.R:
                EmitSignal(SignalName.RestartRequested);
                break;
            case Key.X:
                StopSelectedUnits();
                break;
            case Key.Q:
                if (GetControllableSelectedUnits().Count > 0)
                {
                    _attackMoveMode = !_attackMoveMode;
                    _hud.ShowMessage(_attackMoveMode ? GameUiText.MessageAttackMoveArmed : GameUiText.MessageAttackMoveCleared);
                }
                break;
            default:
                if (TryHandleTrainingHotkey(key))
                {
                    return;
                }

                TryHandleBuildHotkey(key);
                break;
        }
    }

    private void OnDebugModeChanged(bool enabled)
    {
        _debugModeEnabled = enabled;
        RefreshPresentationState();
    }

    private bool TryHandleTrainingHotkey(Key key)
    {
        if (_selectedBuilding is null || _selectedBuilding.Side != GameSide.Player || !_selectedBuilding.Completed)
        {
            return false;
        }

        return key switch
        {
            Key.E => TryQueueSelectedBuilding(UnitKind.Worker),
            Key.F => TryQueueSelectedBuilding(UnitKind.Footman),
            Key.G => TryQueueSelectedBuilding(UnitKind.Archer),
            Key.K => TryQueueSelectedBuilding(UnitKind.Knight),
            Key.C => TryQueueSelectedBuilding(UnitKind.Catapult),
            _ => false
        };
    }

    private void TryHandleBuildHotkey(Key key)
    {
        if (!HasSelectedWorker())
        {
            return;
        }

        switch (key)
        {
            case Key.H:
                BeginPlacement(BuildingKind.TownHall);
                break;
            case Key.F:
                BeginPlacement(BuildingKind.Farm);
                break;
            case Key.B:
                BeginPlacement(BuildingKind.Barracks);
                break;
            case Key.V:
                BeginPlacement(BuildingKind.Workshop);
                break;
            case Key.T:
                BeginPlacement(BuildingKind.Tower);
                break;
        }
    }

    private bool TryQueueSelectedBuilding(UnitKind kind)
    {
        if (_selectedBuilding is null || _simulation is null)
        {
            return false;
        }

        var unitDefinition = GameDefinitions.Units[kind];
        if (_selectedBuilding.Kind != unitDefinition.Producer)
        {
            _hud.ShowMessage(GameUiText.MessageWrongProducer);
            return false;
        }

        if (_simulation.TryQueueUnit(_selectedBuilding, kind))
        {
            _lastCommand = GameUiText.CommandQueue(kind, _currentInit.PlayerRace);
            _hud.ShowMessage(GameUiText.UnitQueued(GameUiText.UnitDisplayName(kind, _currentInit.PlayerRace)));
            _audio.PlayTrain();
            return true;
        }

        _hud.ShowMessage(GameUiText.MessageQueueFailed);
        return false;
    }

    private void TryCancelSelectedQueuedUnit()
    {
        if (_selectedBuilding is null || _simulation is null)
        {
            return;
        }

        if (_simulation.TryCancelLastQueuedUnit(_selectedBuilding, out var canceledKind) && canceledKind.HasValue)
        {
            _lastCommand = GameUiText.CommandCancelQueue(canceledKind.Value, _currentInit.PlayerRace);
            _hud.ShowMessage(GameUiText.QueueCanceled(GameUiText.UnitDisplayName(canceledKind.Value, _currentInit.PlayerRace)));
            return;
        }

        _hud.ShowMessage(GameUiText.MessageQueueNothingToCancel);
    }

    private void BeginPlacement(BuildingKind kind)
    {
        if (_simulation is null)
        {
            return;
        }

        var placement = _simulation.EvaluateBuildingPlacement(GameSide.Player, kind, _simulation.Map.WorldToTile(GetGlobalMousePosition()));
        if (placement.Issue == BuildingPlacementIssue.InsufficientResources)
        {
            _hud.ShowMessage(GameUiText.MessageNotEnoughResources);
            return;
        }

        _placementKind = kind;
        _attackMoveMode = false;
        _lastCommand = GameUiText.CommandPlace(kind, _currentInit.PlayerRace);
        _hud.ShowMessage(GameUiText.PlacingBuilding(GameUiText.BuildingDisplayName(kind, _currentInit.PlayerRace)));
        _overlay.QueueRedraw();
    }

    private void CancelPlacement()
    {
        if (_placementKind.HasValue)
        {
            _hud.ShowMessage(GameUiText.MessagePlacementCancelled);
        }

        _placementKind = null;
        _overlay.QueueRedraw();
    }

    private void TryPlaceBuilding(Vector2 worldPosition)
    {
        if (!_placementKind.HasValue || _simulation is null)
        {
            return;
        }

        var worker = GetControllableSelectedUnits().Find(unit => unit.IsWorker());
        if (worker is null)
        {
            _hud.ShowMessage(GameUiText.MessageSelectWorkerFirst);
            return;
        }

        var tile = _simulation.Map.WorldToTile(worldPosition);
        var placement = _simulation.EvaluateBuildingPlacement(GameSide.Player, _placementKind.Value, tile);
        if (!placement.CanPlace)
        {
            _hud.ShowMessage(GameUiText.PlacementIssueMessage(placement.Issue, _placementKind.Value, _currentInit.PlayerRace));
            return;
        }

        if (!_simulation.TryStartBuilding(GameSide.Player, _currentInit.PlayerRace, _placementKind.Value, tile, out var site) || site is null)
        {
            _hud.ShowMessage(GameUiText.MessageCannotPlaceBuilding);
            return;
        }

        _simulation.IssueBuild(worker, site);
        _lastCommand = GameUiText.CommandStartFoundation(_placementKind.Value, site.Id, _currentInit.PlayerRace);
        _placementKind = null;
        _effectsLayer.CommandMarker(site.Center, new Color(0.52f, 1f, 0.55f), GameUiText.MarkerFoundation);
        _audio.PlayBuild();
    }

    private void IssueMoveGroup(List<SimUnit> units, Vector2 worldPosition, Vector2? occupiedCenter = null, float occupiedRadius = 0f)
    {
        if (_simulation is null)
        {
            return;
        }

        var slots = FormationSlots(units, worldPosition, occupiedCenter, occupiedRadius);
        _simulation.IssueMoveGroup(units, slots, worldPosition);

        _lastCommand = GameUiText.CommandMove(FormatVector(worldPosition));
        _effectsLayer.CommandMarker(worldPosition, new Color(0.42f, 1f, 0.48f), GameUiText.MarkerMove);
        _audio.PlayMove();
    }

    private void IssueAttackMoveGroup(Vector2 worldPosition)
    {
        if (_simulation is null)
        {
            return;
        }

        var units = GetControllableSelectedUnits();
        var slots = FormationSlots(units, worldPosition);
        _simulation.IssueAttackMoveGroup(units, slots, worldPosition);

        _lastCommand = GameUiText.CommandAttackMove(FormatVector(worldPosition));
        _effectsLayer.CommandMarker(worldPosition, new Color(1f, 0.62f, 0.34f), GameUiText.MarkerAttack);
        _audio.PlayAttack();
    }

    private void StopSelectedUnits()
    {
        if (_simulation is null)
        {
            return;
        }

        foreach (var unit in GetControllableSelectedUnits())
        {
            _simulation.IssueStop(unit);
        }

        _attackMoveMode = false;
        _lastCommand = GameUiText.CommandStop();
        _hud.ShowMessage(GameUiText.MessageOrdersCleared);
    }

    private void TogglePause()
    {
        if (!_isActive || _simulation is null || _simulation.GameOver)
        {
            return;
        }

        _isPaused = !_isPaused;
        _attackMoveMode = false;
        _selectionStartWorld = null;
        _selectionCurrentWorld = null;
        if (_isPaused)
        {
            _hud.ShowPauseMenu();
        }
        else
        {
            _hud.HidePauseMenu();
        }
    }

    private void CenterOnTownHall()
    {
        if (_simulation is null)
        {
            return;
        }

        var building = _simulation.Buildings.Find(candidate => candidate.Alive && candidate.Side == GameSide.Player && candidate.Kind == BuildingKind.TownHall);
        if (building is null)
        {
            return;
        }

        _camera.Position = building.Center;
        ClampCamera(GetViewportRect().Size);
    }

    private static List<Vector2> FormationSlots(IReadOnlyList<SimUnit> units, Vector2 center, Vector2? occupiedCenter = null, float occupiedRadius = 0f)
    {
        var count = units.Count;
        var slots = new List<Vector2>(count);
        if (count <= 0)
        {
            return slots;
        }

        if (occupiedCenter.HasValue && occupiedRadius > 0.01f)
        {
            return BuildOccupiedFormationSlots(units, center, occupiedCenter.Value, occupiedRadius);
        }

        var columns = Mathf.CeilToInt(Mathf.Sqrt(count));
        var rows = Mathf.CeilToInt(count / (float)columns);
        var width = (columns - 1) * GameConstants.GroupSpacing;
        var height = (rows - 1) * GameConstants.GroupSpacing;

        for (var index = 0; index < count; index++)
        {
            var row = index / columns;
            var column = index % columns;
            var x = center.X - width / 2f + column * GameConstants.GroupSpacing;
            var y = center.Y - height / 2f + row * GameConstants.GroupSpacing;
            slots.Add(new Vector2(x, y));
        }

        return slots;
    }

    private static List<Vector2> BuildOccupiedFormationSlots(IReadOnlyList<SimUnit> units, Vector2 clickPosition, Vector2 occupiedCenter, float occupiedRadius)
    {
        var count = units.Count;
        var slots = new List<Vector2>(count);
        if (count <= 0)
        {
            return slots;
        }

        var outward = clickPosition - occupiedCenter;
        if (outward.LengthSquared() <= 16f)
        {
            outward = occupiedCenter - ComputeUnitCentroid(units);
        }

        if (outward.LengthSquared() <= 16f)
        {
            outward = Vector2.Down;
        }

        outward = outward.Normalized();
        var lateral = new Vector2(-outward.Y, outward.X);
        var columns = Mathf.CeilToInt(Mathf.Sqrt(count));
        var outwardSpacing = GameConstants.GroupSpacing * 0.9f;
        var baseDistance = occupiedRadius + GameConstants.GroupSpacing * 0.65f;
        var width = (columns - 1) * GameConstants.GroupSpacing;

        for (var index = 0; index < count; index++)
        {
            var row = index / columns;
            var column = index % columns;
            var lateralOffset = column * GameConstants.GroupSpacing - width / 2f;
            var distance = baseDistance + row * outwardSpacing;
            slots.Add(occupiedCenter + outward * distance + lateral * lateralOffset);
        }

        return slots;
    }

    private static Vector2 ComputeUnitCentroid(IReadOnlyList<SimUnit> units)
    {
        if (units.Count == 0)
        {
            return Vector2.Zero;
        }

        var center = Vector2.Zero;
        for (var index = 0; index < units.Count; index++)
        {
            center += units[index].Position;
        }

        return center / units.Count;
    }

    private void SyncViews()
    {
        if (_simulation is null || _fog is null)
        {
            return;
        }

        var selectedUnits = new HashSet<int>();
        foreach (var unit in _selectedUnits)
        {
            if (unit.Alive)
            {
                selectedUnits.Add(unit.Id);
            }
        }

        SyncResourceViews();
        SyncBuildingViews();
        SyncUnitViews(selectedUnits);
        _selectedUnits.RemoveAll(unit => !unit.Alive);
        if (_selectedBuilding is not null && !_selectedBuilding.Alive)
        {
            _selectedBuilding = null;
        }
        else if (_selectedBuilding is not null && _selectedBuilding.Side != GameSide.Player)
        {
            var centerTile = _selectedBuilding.CenterTile();
            if (!_fog.IsVisible(centerTile.X, centerTile.Y))
            {
                _selectedBuilding = null;
            }
        }
    }

    private void SyncResourceViews()
    {
        if (_simulation is null || _fog is null)
        {
            return;
        }

        var seen = new HashSet<int>();
        var liveIds = new HashSet<int>();
        foreach (var resource in _simulation.Resources)
        {
            var tile = _simulation.Map.WorldToTile(resource.Center);
            var visible = _fog.IsVisible(tile.X, tile.Y);
            var explored = _fog.IsExplored(tile.X, tile.Y);
            liveIds.Add(resource.Id);

            if (visible)
            {
                _rememberedResources[resource.Id] = RememberedResourceState.From(resource);
                var view = GetOrCreateResourceView(resource.Id);
                view.Bind(resource);
                view.SyncFromSimulation();
                view.ApplyFogState(true, true);
                seen.Add(resource.Id);
                continue;
            }

            if (explored && _rememberedResources.TryGetValue(resource.Id, out var remembered))
            {
                var view = GetOrCreateResourceView(resource.Id);
                view.ApplyRememberedState(remembered);
                view.ApplyFogState(false, true);
                seen.Add(resource.Id);
            }
        }

        var staleRememberedResources = new List<int>();
        foreach (var remembered in _rememberedResources.Values)
        {
            if (liveIds.Contains(remembered.Id))
            {
                continue;
            }

            var tile = _simulation.Map.WorldToTile(remembered.Center);
            if (_fog.IsVisible(tile.X, tile.Y))
            {
                staleRememberedResources.Add(remembered.Id);
                continue;
            }

            if (!_fog.IsExplored(tile.X, tile.Y))
            {
                continue;
            }

            var view = GetOrCreateResourceView(remembered.Id);
            view.ApplyRememberedState(remembered);
            view.ApplyFogState(false, true);
            seen.Add(remembered.Id);
        }

        foreach (var id in staleRememberedResources)
        {
            _rememberedResources.Remove(id);
        }

        RemoveMissingViews(_resourceViews, seen);
    }

    private void SyncBuildingViews()
    {
        if (_simulation is null || _fog is null)
        {
            return;
        }

        var seen = new HashSet<int>();
        var liveIds = new HashSet<int>();
        foreach (var building in _simulation.Buildings)
        {
            liveIds.Add(building.Id);
            if (building.Side == GameSide.Player)
            {
                var playerView = GetOrCreateBuildingView(building.Id);
                playerView.Bind(building);
                playerView.SyncFromSimulation(building == _selectedBuilding);
                playerView.ApplyFogState(true, true);
                seen.Add(building.Id);
                continue;
            }

            var centerTile = building.CenterTile();
            var visible = _fog.IsVisible(centerTile.X, centerTile.Y);
            var explored = _fog.IsExplored(centerTile.X, centerTile.Y);
            if (visible)
            {
                _rememberedBuildings[building.Id] = RememberedBuildingState.From(building);
                var view = GetOrCreateBuildingView(building.Id);
                view.Bind(building);
                view.SyncFromSimulation(false);
                view.ApplyFogState(true, true);
                seen.Add(building.Id);
            }
            else if (explored && _rememberedBuildings.TryGetValue(building.Id, out var remembered))
            {
                var view = GetOrCreateBuildingView(building.Id);
                view.ApplyRememberedState(remembered, false);
                view.ApplyFogState(false, true);
                seen.Add(building.Id);
            }
        }

        var staleRememberedBuildings = new List<int>();
        foreach (var remembered in _rememberedBuildings.Values)
        {
            if (liveIds.Contains(remembered.Id))
            {
                continue;
            }

            var centerTile = remembered.CenterTile;
            if (_fog.IsVisible(centerTile.X, centerTile.Y))
            {
                staleRememberedBuildings.Add(remembered.Id);
                continue;
            }

            if (!_fog.IsExplored(centerTile.X, centerTile.Y))
            {
                continue;
            }

            var view = GetOrCreateBuildingView(remembered.Id);
            view.ApplyRememberedState(remembered, false);
            view.ApplyFogState(false, true);
            seen.Add(remembered.Id);
        }

        foreach (var id in staleRememberedBuildings)
        {
            _rememberedBuildings.Remove(id);
        }

        RemoveMissingViews(_buildingViews, seen);
    }

    private void SyncUnitViews(HashSet<int> selectedUnits)
    {
        if (_simulation is null || _fog is null)
        {
            return;
        }

        var seen = new HashSet<int>();
        foreach (var unit in _simulation.Units)
        {
            seen.Add(unit.Id);
            if (!_unitViews.TryGetValue(unit.Id, out var view))
            {
                view = new UnitView { Name = $"Unit_{unit.Id}" };
                view.Bind(unit);
                _unitsLayer.AddChild(view);
                _unitViews[unit.Id] = view;
            }

            view.SyncFromSimulation(selectedUnits.Contains(unit.Id));
            if (unit.Side == GameSide.Player)
            {
                view.ApplyFogState(true);
            }
            else
            {
                view.ApplyFogState(CanSeeEnemyUnit(unit));
            }
        }

        RemoveMissingViews(_unitViews, seen);
    }

    private void SyncHud()
    {
        if (_simulation is null || _fog is null)
        {
            return;
        }

        var mouseWorld = GetGlobalMousePosition();
        var hoveredUnit = FindUnitAt(mouseWorld);
        var hoveredBuilding = hoveredUnit is null ? FindBuildingAt(mouseWorld) : null;
        var hoveredResource = FindResourceAt(mouseWorld);
        _hud.UpdateState(
            _simulation.GetPlayer(GameSide.Player),
            _selectedUnits,
            _selectedBuilding,
            GetPlayerBuildings(),
            _lastCommand,
            _placementKind,
            _attackMoveMode,
            hoveredUnit,
            hoveredBuilding,
            hoveredResource,
            _simulation.Map.WorldToTile(mouseWorld),
            _simulation.Winner,
            _currentInit.PlayerRace,
            BuildMinimapState());
    }

    private IReadOnlyList<SimBuilding> GetPlayerBuildings()
    {
        if (_simulation is null)
        {
            return Array.Empty<SimBuilding>();
        }

        var buildings = new List<SimBuilding>();
        foreach (var building in _simulation.Buildings)
        {
            if (building.Alive && building.Side == GameSide.Player)
            {
                buildings.Add(building);
            }
        }

        return buildings;
    }

    private MinimapState BuildMinimapState()
    {
        var markers = new List<MinimapMarker>();
        var pings = new List<MinimapPing>();
        if (_simulation is not null && _fog is not null)
        {
            foreach (var unit in _simulation.Units)
            {
                if (!unit.Alive)
                {
                    continue;
                }

                if (unit.Side != GameSide.Player)
                {
                    if (!CanSeeEnemyUnit(unit))
                    {
                        continue;
                    }
                }

                markers.Add(new MinimapMarker(unit.Position, unit.Side == GameSide.Player ? GameColors.Player : GameColors.AI, unit.Radius, false));
            }

            foreach (var building in _simulation.Buildings)
            {
                if (!building.Alive)
                {
                    continue;
                }

                if (building.Side == GameSide.Player)
                {
                    markers.Add(new MinimapMarker(building.Center, GameColors.Player, building.Radius, true));
                    continue;
                }

                var centerTile = building.CenterTile();
                if (_fog.IsVisible(centerTile.X, centerTile.Y))
                {
                    markers.Add(new MinimapMarker(building.Center, GameColors.AI, building.Radius, true));
                }
            }

            foreach (var remembered in _rememberedBuildings.Values)
            {
                var centerTile = remembered.CenterTile;
                if (!_fog.IsExplored(centerTile.X, centerTile.Y) || _fog.IsVisible(centerTile.X, centerTile.Y))
                {
                    continue;
                }

                markers.Add(new MinimapMarker(remembered.Center, GameColors.AI, remembered.Radius, true));
            }

            var now = Time.GetTicksMsec();
            foreach (var ping in _minimapPings)
            {
                var life = Mathf.Clamp((float)((ping.ExpiresAtMs - now) / 2200d), 0f, 1f);
                if (life <= 0f)
                {
                    continue;
                }

                pings.Add(new MinimapPing(ping.Position, ping.Color, life));
            }
        }

        return new MinimapState
        {
            Map = _simulation!.Map,
            Fog = _fog!,
            Markers = markers,
            Pings = pings,
            CameraWorldRect = GetCameraWorldRect()
        };
    }

    private void PruneMinimapPings()
    {
        var now = Time.GetTicksMsec();
        _minimapPings.RemoveAll(ping => ping.ExpiresAtMs <= now);
    }

    private Rect2 GetCameraWorldRect()
    {
        var viewportSize = GetViewportRect().Size / _camera.Zoom.X;
        return new Rect2(_camera.Position - viewportSize / 2f, viewportSize);
    }

    private void ConfigureCamera()
    {
        if (_simulation is null)
        {
            return;
        }

        _camera.Zoom = new Vector2(GameConstants.CameraZoom, GameConstants.CameraZoom);
        _camera.Position = new Vector2(
            _simulation.Map.Width * GameConstants.TileSize / 2f,
            _simulation.Map.Height * GameConstants.TileSize / 2f);
        _camera.Enabled = true;
    }

    private void AdjustZoom(float delta)
    {
        var nextZoom = Mathf.Clamp(_camera.Zoom.X + delta, GameConstants.CameraMinZoom, GameConstants.CameraMaxZoom);
        _camera.Zoom = new Vector2(nextZoom, nextZoom);
        ClampCamera(GetViewportRect().Size);
    }

    private void UpdateCamera(double delta)
    {
        if (_simulation is null)
        {
            return;
        }

        var velocity = Vector2.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W))
        {
            velocity.Y -= 1f;
        }

        if (Input.IsPhysicalKeyPressed(Key.S))
        {
            velocity.Y += 1f;
        }

        if (Input.IsPhysicalKeyPressed(Key.A))
        {
            velocity.X -= 1f;
        }

        if (Input.IsPhysicalKeyPressed(Key.D))
        {
            velocity.X += 1f;
        }

        var mousePosition = GetViewport().GetMousePosition();
        var viewportSize = GetViewportRect().Size;
        if (mousePosition.X <= GameConstants.EdgeScrollPixels)
        {
            velocity.X -= 1f;
        }
        else if (mousePosition.X >= viewportSize.X - GameConstants.EdgeScrollPixels)
        {
            velocity.X += 1f;
        }

        if (mousePosition.Y <= GameConstants.EdgeScrollPixels)
        {
            velocity.Y -= 1f;
        }
        else if (mousePosition.Y >= viewportSize.Y - GameConstants.EdgeScrollPixels)
        {
            velocity.Y += 1f;
        }

        if (velocity != Vector2.Zero)
        {
            velocity = velocity.Normalized() * GameConstants.CameraSpeed * (float)delta;
            _camera.Position += velocity;
        }

        ClampCamera(viewportSize);
    }

    private void ClampCamera(Vector2 viewportSize)
    {
        if (_simulation is null)
        {
            return;
        }

        var worldWidth = _simulation.Map.Width * GameConstants.TileSize;
        var worldHeight = _simulation.Map.Height * GameConstants.TileSize;
        _camera.Position = new Vector2(
            Mathf.Clamp(_camera.Position.X, 0f, worldWidth),
            Mathf.Clamp(_camera.Position.Y, 0f, worldHeight));
    }

    private SimUnit? FindUnitAt(Vector2 worldPosition)
    {
        if (_simulation is null)
        {
            return null;
        }

        SimUnit? best = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var unit in _simulation.Units)
        {
            if (!unit.Alive)
            {
                continue;
            }

            if (unit.Side != GameSide.Player)
            {
                if (!CanSeeEnemyUnit(unit))
                {
                    continue;
                }
            }

            var distance = unit.Position.DistanceTo(worldPosition);
            if (distance <= unit.Radius + 6f && distance < bestDistance)
            {
                best = unit;
                bestDistance = distance;
            }
        }

        return best;
    }

    private SimBuilding? FindBuildingAt(Vector2 worldPosition)
    {
        if (_simulation is null)
        {
            return null;
        }

        SimBuilding? best = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var building in _simulation.Buildings)
        {
            if (!building.Alive)
            {
                continue;
            }

            if (building.Side != GameSide.Player)
            {
                var centerTile = building.CenterTile();
                if (_fog is not null && !_fog.IsVisible(centerTile.X, centerTile.Y))
                {
                    continue;
                }
            }

            var distance = building.Center.DistanceTo(worldPosition);
            if (distance <= building.Radius && distance < bestDistance)
            {
                best = building;
                bestDistance = distance;
            }
        }

        return best;
    }

    private SimResourceNode? FindResourceAt(Vector2 worldPosition)
    {
        if (_simulation is null)
        {
            return null;
        }

        SimResourceNode? best = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var resource in _simulation.Resources)
        {
            if (!resource.Alive)
            {
                continue;
            }

            var tile = _simulation.Map.WorldToTile(resource.Center);
            if (_fog is not null && !_fog.IsVisible(tile.X, tile.Y))
            {
                continue;
            }

            var distance = resource.Center.DistanceTo(worldPosition);
            if (distance <= resource.Radius + 8f && distance < bestDistance)
            {
                best = resource;
                bestDistance = distance;
            }
        }

        return best;
    }

    private ResourceView GetOrCreateResourceView(int id)
    {
        if (_resourceViews.TryGetValue(id, out var existing))
        {
            return existing;
        }

        var view = new ResourceView { Name = $"Resource_{id}" };
        _resourcesLayer.AddChild(view);
        _resourceViews[id] = view;
        return view;
    }

    private BuildingView GetOrCreateBuildingView(int id)
    {
        if (_buildingViews.TryGetValue(id, out var existing))
        {
            return existing;
        }

        var view = new BuildingView { Name = $"Building_{id}" };
        _buildingsLayer.AddChild(view);
        _buildingViews[id] = view;
        return view;
    }

    private List<SimUnit> GetControllableSelectedUnits()
    {
        var controllable = new List<SimUnit>();
        foreach (var unit in _selectedUnits)
        {
            if (unit.Alive && unit.Side == GameSide.Player)
            {
                controllable.Add(unit);
            }
        }

        return controllable;
    }

    private bool HasSelectedWorker()
    {
        foreach (var unit in _selectedUnits)
        {
            if (unit.Alive && unit.Side == GameSide.Player && unit.IsWorker())
            {
                return true;
            }
        }

        return false;
    }

    private bool ShouldDisplayPosition(Vector2 position)
    {
        if (_simulation is null || _fog is null)
        {
            return false;
        }

        var tile = _simulation.Map.WorldToTile(position);
        if (_fog.IsVisible(tile.X, tile.Y))
        {
            return true;
        }

        if (!_debugModeEnabled)
        {
            return false;
        }

        foreach (var unit in _simulation.Units)
        {
            if (!unit.Alive || unit.Side == GameSide.Player)
            {
                continue;
            }

            if (unit.Position.DistanceTo(position) <= unit.Radius + 16f)
            {
                return true;
            }
        }

        return false;
    }

    private bool CanSeeEnemyUnit(SimUnit unit)
    {
        if (unit.Side == GameSide.Player)
        {
            return true;
        }

        if (_debugModeEnabled)
        {
            return true;
        }

        if (_simulation is null || _fog is null)
        {
            return false;
        }

        var tile = _simulation.Map.WorldToTile(unit.Position);
        return _fog.IsVisible(tile.X, tile.Y);
    }

    private void RefreshPresentationState()
    {
        if (!_isActive || _simulation is null || _fog is null)
        {
            _hud.UpdateDebugOverlay(false, string.Empty);
            return;
        }

        SyncViews();
        SyncHud();
        _fogOverlayView.Refresh();
        var mouseWorld = GetGlobalMousePosition();
        var hoveredUnit = FindUnitAt(mouseWorld);
        var hoveredBuilding = hoveredUnit is null ? FindBuildingAt(mouseWorld) : null;
        var hoveredResource = FindResourceAt(mouseWorld);
        _overlay.SyncState(_simulation, _placementKind, mouseWorld, _selectionStartWorld, _selectionCurrentWorld, hoveredUnit, hoveredBuilding, hoveredResource);
    }

    private void UpdateDebugOverlay(double delta, bool simulationAdvanced)
    {
        if (!_isActive)
        {
            _hud.UpdateDebugOverlay(false, string.Empty);
            return;
        }

        if (simulationAdvanced)
        {
            _tickCounterWindow++;
        }

        _tickCounterWindowMs += delta * 1000d;
        while (_tickCounterWindowMs >= 1000d)
        {
            _ticksPerSecond = _tickCounterWindow;
            _tickCounterWindow = 0;
            _tickCounterWindowMs -= 1000d;
        }

        var text = string.Format(
            GameUiText.DebugOverlayFormat,
            Engine.GetFramesPerSecond(),
            _ticksPerSecond,
            _simulationTickCount);
        _hud.UpdateDebugOverlay(_debugModeEnabled, text);
    }

    private bool IsPointerOverHud()
    {
        var hovered = GetViewport().GuiGetHoveredControl();
        for (Node? current = hovered; current is not null; current = current.GetParent())
        {
            if (current == _hud)
            {
                return true;
            }
        }

        return false;
    }

    private static void RemoveMissingViews<TView>(Dictionary<int, TView> views, HashSet<int> seen)
        where TView : Node
    {
        var toRemove = new List<int>();
        foreach (var entry in views)
        {
            if (seen.Contains(entry.Key))
            {
                continue;
            }

            entry.Value.QueueFree();
            toRemove.Add(entry.Key);
        }

        foreach (var id in toRemove)
        {
            views.Remove(id);
        }
    }

    private static string FormatVector(Vector2 value)
    {
        return $"{Mathf.RoundToInt(value.X)},{Mathf.RoundToInt(value.Y)}";
    }
}
