using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Resources;
using RtsNaGodote.Core.Simulation.Units;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Game.UI;

public enum HudActionKind
{
    Build,
    Train,
    CancelQueue,
    AttackMove,
    Stop,
    Center
}

public sealed record HudActionModel(
    HudActionKind Kind,
    int Payload,
    string Text,
    string Description,
    bool Enabled,
    string DisabledReason);

public sealed record SelectionCardModel(
    string Title,
    string Value,
    string Detail,
    Color Accent);

public sealed record ProductionSlotModel(
    string Label,
    string Detail,
    bool Active);

public sealed record ProductionPanelModel(
    bool Visible,
    string CurrentText,
    string ProgressText,
    IReadOnlyList<ProductionSlotModel> Slots,
    bool CanCancelLast,
    string CancelHint);

public sealed record StatusBlockModel(
    string ModeText,
    string RallyText,
    string ProductionText,
    string QueueText);

public sealed record SelectionPanelModel(
    string Title,
    string Stats,
    string Summary,
    IReadOnlyList<SelectionCardModel> Cards,
    ProductionPanelModel Production);

public sealed record HudViewState(
    int Gold,
    int Lumber,
    int Food,
    int FoodCap,
    string HintText,
    string ActivityText,
    string SelectionSignature,
    string ActionsSignature,
    string StatusSignature,
    GameSide? Winner);

public sealed record HudPresenterInput(
    PlayerState Player,
    IReadOnlyList<SimUnit> SelectedUnits,
    SimBuilding? SelectedBuilding,
    IReadOnlyList<SimBuilding> PlayerBuildings,
    string LastCommand,
    BuildingKind? PlacementKind,
    bool AttackMoveMode,
    SimUnit? HoveredUnit,
    SimBuilding? HoveredBuilding,
    SimResourceNode? HoveredResource,
    Vector2I HoveredTile,
    GameSide? Winner,
    Race PlayerRace,
    MinimapState MinimapState);

public sealed record HudPresentation(
    SelectionPanelModel SelectionModel,
    IReadOnlyList<HudActionModel> Actions,
    string HintText,
    string ActivityText,
    string StatusText,
    HudViewState ViewState);
