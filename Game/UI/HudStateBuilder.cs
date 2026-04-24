using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Resources;
using RtsNaGodote.Core.Simulation.Units;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Game.UI;

internal static class HudStateBuilder
{
    public static HudPresentation Build(HudPresenterInput input)
    {
        var selectionModel = BuildSelectionModel(input.SelectedUnits, input.SelectedBuilding, input.PlayerRace);
        var hintText = BuildHintText(input.SelectedUnits, input.SelectedBuilding, input.PlacementKind, input.AttackMoveMode, input.Winner, input.PlayerRace);
        var activityText = BuildActivityText(input.LastCommand, input.PlacementKind, input.HoveredUnit, input.HoveredBuilding, input.HoveredResource, input.HoveredTile, input.Winner, input.PlayerRace);
        var statusBlock = BuildStatusBlock(input.SelectedBuilding, input.PlacementKind, input.AttackMoveMode, input.PlayerRace);
        var statusText = $"{statusBlock.ModeText}  |  {statusBlock.RallyText}  |  {statusBlock.ProductionText}  |  {statusBlock.QueueText}";
        var actions = BuildActionModels(input.Player, input.SelectedUnits, input.SelectedBuilding, input.PlayerBuildings, input.PlayerRace, input.Winner);
        var selectionSignature = BuildSelectionSignature(selectionModel);
        var actionsSignature = BuildActionsSignature(actions);
        var statusSignature = $"{statusBlock.ModeText}|{statusBlock.RallyText}|{statusBlock.ProductionText}|{statusBlock.QueueText}";
        var viewState = new HudViewState(
            input.Player.Gold,
            input.Player.Lumber,
            input.Player.Food,
            input.Player.FoodCap,
            hintText,
            activityText,
            selectionSignature,
            actionsSignature,
            statusSignature,
            input.Winner);

        return new HudPresentation(selectionModel, actions, hintText, activityText, statusText, viewState);
    }

    private static SelectionPanelModel BuildSelectionModel(
        IReadOnlyList<SimUnit> selectedUnits,
        SimBuilding? selectedBuilding,
        Race playerRace)
    {
        if (selectedBuilding is not null)
        {
            return BuildBuildingSelectionModel(selectedBuilding, playerRace);
        }

        if (selectedUnits.Count == 1)
        {
            return BuildSingleUnitSelectionModel(selectedUnits[0], playerRace);
        }

        if (selectedUnits.Count > 1)
        {
            return BuildGroupSelectionModel(selectedUnits, playerRace);
        }

        return new SelectionPanelModel(
            GameUiText.SelectionIdleTitle,
            GameUiText.SelectionIdleStats,
            GameUiText.SelectionIdleSummary,
            [
                new SelectionCardModel(GameUiText.SelectionCardStartTitle, GameUiText.SelectionCardStartValue, GameUiText.SelectionCardStartDetail, new Color(0.48f, 0.87f, 1f)),
                new SelectionCardModel(GameUiText.SelectionCardMapTitle, GameUiText.SelectionCardMapValue, GameUiText.SelectionCardMapDetail, new Color(0.56f, 0.88f, 0.5f)),
                new SelectionCardModel(GameUiText.SelectionCardArmyTitle, GameUiText.SelectionCardArmyValue, GameUiText.SelectionCardArmyDetail, new Color(1f, 0.8f, 0.4f)),
                new SelectionCardModel(GameUiText.SelectionCardControlTitle, GameUiText.SelectionCardControlValue, GameUiText.SelectionCardControlDetail, new Color(0.92f, 0.62f, 0.46f))
            ],
            HiddenProduction());
    }

    private static SelectionPanelModel BuildBuildingSelectionModel(SimBuilding building, Race playerRace)
    {
        var name = GameUiText.BuildingDisplayName(building.Kind, building.Race);
        var state = building.Completed
            ? GameUiText.BuildingReadyState
            : building.BuildProgressMs <= 0.01
                ? GameUiText.BuildingFoundationState
                : GameUiText.BuildingConstructing(Mathf.RoundToInt(building.ProgressFraction() * 100f));

        var stats = new List<string> { $"HP {building.Hp}/{building.MaxHp}" };
        if (building.CanAttack())
        {
            stats.Add($"ATK {building.Attack}");
            stats.Add($"RNG {Mathf.RoundToInt(building.Range)}");
        }

        string summary;
        if (!building.Completed)
        {
            summary = building.BuildProgressMs <= 0.01
                ? GameUiText.BuildingConstructionWaiting
                : GameUiText.BuildingConstructionInProgress(Mathf.RoundToInt(building.ProgressFraction() * 100f));
        }
        else if (building.Queue.Count > 0)
        {
            var queueBuilder = new StringBuilder(GameUiText.QueueSummaryPrefix);
            for (var i = 0; i < building.Queue.Count; i++)
            {
                if (i > 0)
                {
                    queueBuilder.Append(GameUiText.QueueArrow);
                }

                queueBuilder.Append(GameUiText.UnitDisplayName(building.Queue[i].Kind, playerRace));
            }

            summary = queueBuilder.ToString();
        }
        else
        {
            summary = building.RallyPoint.HasValue
                ? GameUiText.RallyPointSummary(Mathf.RoundToInt(building.RallyPoint.Value.X), Mathf.RoundToInt(building.RallyPoint.Value.Y))
                : GameUiText.BuildingIdleRallyHint;
        }

        var cards = new List<SelectionCardModel>
        {
            new(GameUiText.BuildingCardTitle, name, GameUiText.BuildingRole(building.Kind), GameUiText.BuildingAccent(building.Kind)),
            new(GameUiText.StatusCardTitle, state, building.Completed ? GameUiText.BuildingActiveDetail : GameUiText.BuildingVulnerableDetail, new Color(0.62f, 0.82f, 1f)),
            new(GameUiText.ProductionCardTitle, building.Queue.Count == 0 ? GameUiText.EmptyValue : $"{building.Queue.Count}/5", building.Queue.Count == 0 ? GameUiText.NoActiveTraining : QueueLeadText(building, playerRace), new Color(0.65f, 1f, 0.55f)),
            new(GameUiText.RallyCardTitle, building.RallyPoint.HasValue ? GameUiText.RallySetValue : GameUiText.RallyUnsetValue, building.RallyPoint.HasValue ? GameUiText.RallyPointCardDetail(Mathf.RoundToInt(building.RallyPoint.Value.X), Mathf.RoundToInt(building.RallyPoint.Value.Y)) : GameUiText.RallyCardEmptyDetail, new Color(1f, 0.78f, 0.45f))
        };

        return new SelectionPanelModel(name, string.Join("  ", stats), summary, cards, BuildProductionPanelModel(building, playerRace));
    }

    private static SelectionPanelModel BuildSingleUnitSelectionModel(SimUnit unit, Race playerRace)
    {
        var title = GameUiText.UnitDisplayName(unit.Kind, unit.Race);
        var stats = $"HP {unit.Hp}/{unit.MaxHp}  ATK {unit.Attack}  RNG {Mathf.RoundToInt(unit.Range)}  {GameUiText.UnitStateLabel(unit.State)}";
        var summary = unit.CargoType is not null && unit.CargoAmount > 0
            ? GameUiText.UnitCarrying(unit.CargoAmount, GameUiText.ResourceShort(unit.CargoType.Value))
            : GameUiText.UnitRole(unit.Kind);

        var cards = new List<SelectionCardModel>
        {
            new(GameUiText.RoleCardTitle, title, GameUiText.UnitRole(unit.Kind), GameUiText.UnitAccent(unit.Kind)),
            new(GameUiText.OrderCardTitle, GameUiText.UnitStateLabel(unit.State), GameUiText.UnitStateHintText(unit.State), new Color(0.6f, 0.84f, 1f)),
            new(GameUiText.CombatCardTitle, unit.CanAttack() ? GameUiText.UnitDamage(unit.Attack) : GameUiText.UnarmedValue, unit.CanAttack() ? GameUiText.UnitCombatDetail(unit.IsRanged(), Mathf.RoundToInt(unit.Range)) : GameUiText.UnitNonCombatDetail, new Color(1f, 0.74f, 0.45f)),
            new(GameUiText.CargoCardTitle, unit.CargoType is null ? GameUiText.EmptyValue : $"{unit.CargoAmount}", unit.CargoType is null ? GameUiText.NoCargoDetail : GameUiText.UnitCargoDetail(GameUiText.ResourceShort(unit.CargoType.Value)), new Color(0.55f, 0.94f, 0.56f))
        };

        return new SelectionPanelModel(title, stats, summary, cards, HiddenProduction());
    }

    private static SelectionPanelModel BuildGroupSelectionModel(IReadOnlyList<SimUnit> selectedUnits, Race playerRace)
    {
        var totalHp = 0;
        var totalMaxHp = 0;
        var counts = new SortedDictionary<UnitKind, int>();
        foreach (var unit in selectedUnits)
        {
            totalHp += unit.Hp;
            totalMaxHp += unit.MaxHp;
            counts[unit.Kind] = counts.GetValueOrDefault(unit.Kind) + 1;
        }

        var summaryBuilder = new StringBuilder();
        var first = true;
        foreach (var pair in counts)
        {
            if (!first)
            {
                summaryBuilder.Append(GameUiText.GroupSummarySeparator);
            }

            summaryBuilder.Append(GameUiText.GroupSummaryEntry(GameUiText.UnitDisplayName(pair.Key, playerRace), pair.Value));
            first = false;
        }

        var cards = new List<SelectionCardModel>();
        foreach (var pair in counts)
        {
            var hp = 0;
            var maxHp = 0;
            foreach (var unit in selectedUnits)
            {
                if (unit.Kind != pair.Key)
                {
                    continue;
                }

                hp += unit.Hp;
                maxHp += unit.MaxHp;
            }

            cards.Add(new SelectionCardModel(
                GameUiText.UnitDisplayName(pair.Key, playerRace),
                GameUiText.GroupCardValue(pair.Value, hp, maxHp),
                GroupRoleText(pair.Key),
                GameUiText.UnitAccent(pair.Key)));
        }

        while (cards.Count < 4)
        {
            cards.Add(new SelectionCardModel(GameUiText.EmptySlotTitle, GameUiText.EmptyValue, GameUiText.EmptyGroupSlotDetail, new Color(0.36f, 0.4f, 0.46f)));
        }

        return new SelectionPanelModel(
            GameUiText.GroupSelectionTitle(selectedUnits.Count),
            GameUiText.GroupSelectionStats(totalHp, totalMaxHp),
            summaryBuilder.ToString(),
            cards,
            HiddenProduction());
    }

    private static List<HudActionModel> BuildActionModels(
        PlayerState player,
        IReadOnlyList<SimUnit> selectedUnits,
        SimBuilding? selectedBuilding,
        IReadOnlyList<SimBuilding> playerBuildings,
        Race playerRace,
        GameSide? winner)
    {
        var actions = new List<HudActionModel>();
        if (winner.HasValue)
        {
            return actions;
        }

        if (selectedBuilding is not null && selectedBuilding.Side == GameSide.Player)
        {
            foreach (var unitKind in TrainableFor(selectedBuilding.Kind))
            {
                var definition = GameDefinitions.Units[unitKind];
                var enabled = true;
                var reason = string.Empty;
                if (!selectedBuilding.Completed)
                {
                    enabled = false;
                    reason = GameUiText.ReasonBuildingStillConstructing;
                }
                else if (selectedBuilding.Queue.Count >= 5)
                {
                    enabled = false;
                    reason = GameUiText.ReasonQueueFull;
                }
                else if (definition.Requires.HasValue && !HasCompletedBuilding(playerBuildings, definition.Requires.Value))
                {
                    enabled = false;
                    reason = GameUiText.ReasonRequiresBuilding(GameUiText.BuildingDisplayName(definition.Requires.Value, playerRace));
                }
                else if (player.Gold < definition.CostGold || player.Lumber < definition.CostLumber)
                {
                    enabled = false;
                    reason = GameUiText.ReasonNotEnoughResources;
                }
                else if (player.Food + definition.Food > player.FoodCap)
                {
                    enabled = false;
                    reason = GameUiText.ReasonNotEnoughSupply;
                }

                actions.Add(new HudActionModel(
                    HudActionKind.Train,
                    (int)unitKind,
                    GameUiText.TrainButtonText(GameUiText.UnitDisplayName(unitKind, playerRace), definition.Hotkey),
                    GameUiText.UnitTooltip(unitKind, playerRace),
                    enabled,
                    reason));
            }

            actions.Add(new HudActionModel(
                HudActionKind.CancelQueue,
                0,
                GameUiText.CancelQueueButton,
                GameUiText.CancelQueueTooltip,
                selectedBuilding.Queue.Count > 1,
                GameUiText.CancelQueueDisabledReason));
            actions.Add(new HudActionModel(
                HudActionKind.Center,
                0,
                GameUiText.CenterCameraButton,
                GameUiText.CenterCameraTooltip,
                true,
                string.Empty));
            return actions;
        }

        if (selectedUnits.Count == 0)
        {
            actions.Add(new HudActionModel(
                HudActionKind.Center,
                0,
                GameUiText.CenterCameraButton,
                GameUiText.CenterCameraTooltip,
                true,
                string.Empty));
            return actions;
        }

        var hasWorker = false;
        foreach (var unit in selectedUnits)
        {
            if (unit.Side == GameSide.Player && unit.IsWorker())
            {
                hasWorker = true;
                break;
            }
        }

        if (hasWorker)
        {
            foreach (var buildingKind in BuildableKinds())
            {
                var definition = GameDefinitions.Buildings[buildingKind];
                var enabled = player.Gold >= definition.CostGold && player.Lumber >= definition.CostLumber;
                var reason = enabled ? string.Empty : GameUiText.ReasonNotEnoughResources;
                actions.Add(new HudActionModel(
                    HudActionKind.Build,
                    (int)buildingKind,
                    GameUiText.BuildButtonText(GameUiText.BuildingDisplayName(buildingKind, playerRace), definition.Hotkey),
                    GameUiText.BuildingTooltip(buildingKind, playerRace),
                    enabled,
                    reason));
            }
        }

        actions.Add(new HudActionModel(HudActionKind.AttackMove, 0, GameUiText.AttackMoveButton, GameUiText.AttackMoveTooltip, true, string.Empty));
        actions.Add(new HudActionModel(HudActionKind.Stop, 0, GameUiText.StopButton, GameUiText.StopTooltip, true, string.Empty));
        actions.Add(new HudActionModel(HudActionKind.Center, 0, GameUiText.CenterCameraButton, GameUiText.CenterCameraTooltip, true, string.Empty));
        return actions;
    }

    private static string BuildHintText(
        IReadOnlyList<SimUnit> selectedUnits,
        SimBuilding? selectedBuilding,
        BuildingKind? placementKind,
        bool attackMoveMode,
        GameSide? winner,
        Race playerRace)
    {
        if (winner.HasValue)
        {
            return GameUiText.HintMatchFinished;
        }

        if (placementKind.HasValue)
        {
            return GameUiText.HintPlacingBuilding(GameUiText.BuildingDisplayName(placementKind.Value, playerRace));
        }

        if (attackMoveMode)
        {
            return GameUiText.HintAttackMoveActive;
        }

        if (selectedBuilding is not null)
        {
            return GameUiText.HintBuildingSelected;
        }

        if (selectedUnits.Count > 0)
        {
            return GameUiText.HintUnitsSelected;
        }

        return GameUiText.HintNothingSelected;
    }

    private static string BuildActivityText(
        string lastCommand,
        BuildingKind? placementKind,
        SimUnit? hoveredUnit,
        SimBuilding? hoveredBuilding,
        SimResourceNode? hoveredResource,
        Vector2I hoveredTile,
        GameSide? winner,
        Race playerRace)
    {
        var hovered = hoveredUnit is not null
            ? GameUiText.HoverUnit(hoveredUnit.Kind, hoveredUnit.Race, hoveredUnit.Id)
            : hoveredBuilding is not null
                ? GameUiText.HoverBuilding(hoveredBuilding.Kind, hoveredBuilding.Race, hoveredBuilding.Id)
                : hoveredResource is not null
                    ? GameUiText.HoverResource(hoveredResource.Type, hoveredResource.Id)
                    : GameUiText.HoverNothing;

        return GameUiText.ActivityLine(
            lastCommand,
            hovered,
            hoveredTile,
            placementKind.HasValue ? GameUiText.PlacementModeShort(GameUiText.BuildingDisplayName(placementKind.Value, playerRace)) : GameUiText.ModeNormal,
            winner.HasValue ? GameUiText.WinnerLabel(winner.Value) : GameUiText.NoWinner);
    }

    private static string BuildSelectionSignature(SelectionPanelModel model)
    {
        var builder = new StringBuilder();
        builder.Append(model.Title).Append('|').Append(model.Stats).Append('|').Append(model.Summary);
        builder.Append('|').Append(model.Production.Visible).Append('|').Append(model.Production.CurrentText).Append('|').Append(model.Production.ProgressText).Append('|').Append(model.Production.CanCancelLast);
        foreach (var slot in model.Production.Slots)
        {
            builder.Append('|').Append(slot.Label).Append(':').Append(slot.Detail).Append(':').Append(slot.Active);
        }

        foreach (var card in model.Cards)
        {
            builder.Append('|').Append(card.Title).Append(':').Append(card.Value).Append(':').Append(card.Detail);
        }

        return builder.ToString();
    }

    private static string BuildActionsSignature(IReadOnlyList<HudActionModel> actions)
    {
        var builder = new StringBuilder();
        foreach (var action in actions)
        {
            builder.Append(action.Kind).Append(':').Append(action.Payload).Append(':').Append(action.Text).Append(':').Append(action.Enabled).Append(':').Append(action.DisabledReason).Append('|');
        }

        return builder.ToString();
    }

    private static bool HasCompletedBuilding(IReadOnlyList<SimBuilding> buildings, BuildingKind required)
    {
        foreach (var building in buildings)
        {
            if (building.Alive && building.Completed && building.Kind == required)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<BuildingKind> BuildableKinds()
    {
        yield return BuildingKind.Farm;
        yield return BuildingKind.Barracks;
        yield return BuildingKind.Workshop;
        yield return BuildingKind.Tower;
        yield return BuildingKind.TownHall;
    }

    private static IEnumerable<UnitKind> TrainableFor(BuildingKind kind)
    {
        return kind switch
        {
            BuildingKind.TownHall => [UnitKind.Worker],
            BuildingKind.Barracks => [UnitKind.Footman, UnitKind.Archer, UnitKind.Knight],
            BuildingKind.Workshop => [UnitKind.Catapult],
            _ => []
        };
    }

    private static string QueueLeadText(SimBuilding building, Race playerRace)
    {
        if (building.Queue.Count == 0)
        {
            return GameUiText.QueueEmpty;
        }

        var current = building.Queue[0];
        return GameUiText.QueueLead(GameUiText.UnitDisplayName(current.Kind, playerRace), Mathf.RoundToInt(building.ProgressFraction() * 100f));
    }

    private static ProductionPanelModel BuildProductionPanelModel(SimBuilding building, Race playerRace)
    {
        if (!building.Completed)
        {
            return new ProductionPanelModel(
                true,
                GameUiText.ProductionCurrentBuildingIncomplete,
                GameUiText.ProductionConstructionProgress(Mathf.RoundToInt(building.ProgressFraction() * 100f)),
                [],
                false,
                GameUiText.ProductionUnavailableUntilComplete);
        }

        var slots = new List<ProductionSlotModel>();
        for (var i = 0; i < 5; i++)
        {
            if (i < building.Queue.Count)
            {
                var item = building.Queue[i];
                slots.Add(new ProductionSlotModel(
                    GameUiText.UnitDisplayName(item.Kind, playerRace),
                    i == 0 ? GameUiText.ProgressPercent(Mathf.RoundToInt(building.ProgressFraction() * 100f)) : GameUiText.QueueSlotLabel(i + 1),
                    i == 0));
            }
            else
            {
                slots.Add(new ProductionSlotModel(GameUiText.EmptyValue, GameUiText.FreeSlot, false));
            }
        }

        var currentText = building.Queue.Count > 0
            ? GameUiText.ProductionCurrent(GameUiText.UnitDisplayName(building.Queue[0].Kind, playerRace))
            : GameUiText.ProductionCurrentIdle;
        var progressText = building.Queue.Count > 0
            ? GameUiText.ProductionProgress(Mathf.RoundToInt(building.ProgressFraction() * 100f), building.Queue.Count)
            : GameUiText.ProductionProgressIdle;
        var canCancel = building.Queue.Count > 1;
        var cancelHint = canCancel
            ? GameUiText.CancelLastQueuedHint(GameUiText.UnitDisplayName(building.Queue[^1].Kind, playerRace))
            : GameUiText.CancelQueueDisabledReason;

        return new ProductionPanelModel(true, currentText, progressText, slots, canCancel, cancelHint);
    }

    private static StatusBlockModel BuildStatusBlock(SimBuilding? selectedBuilding, BuildingKind? placementKind, bool attackMoveMode, Race playerRace)
    {
        var mode = placementKind.HasValue
            ? GameUiText.StatusModePlacement(GameUiText.BuildingDisplayName(placementKind.Value, playerRace))
            : attackMoveMode
                ? GameUiText.StatusModeAttackMove
                : GameUiText.StatusModeNormal;
        var rally = selectedBuilding is not null
            ? selectedBuilding.RallyPoint.HasValue
                ? GameUiText.StatusRally(Mathf.RoundToInt(selectedBuilding.RallyPoint.Value.X), Mathf.RoundToInt(selectedBuilding.RallyPoint.Value.Y))
                : GameUiText.StatusRallyUnset
            : GameUiText.StatusRallyNone;
        var production = selectedBuilding is not null
            ? selectedBuilding.Queue.Count > 0
                ? GameUiText.StatusProduction(GameUiText.UnitDisplayName(selectedBuilding.Queue[0].Kind, selectedBuilding.Race))
                : GameUiText.StatusProductionIdle
            : GameUiText.StatusProductionNone;
        var queue = selectedBuilding is not null
            ? GameUiText.StatusQueue(selectedBuilding.Queue.Count)
            : GameUiText.StatusQueueNone;
        return new StatusBlockModel(mode, rally, production, queue);
    }

    private static ProductionPanelModel HiddenProduction()
    {
        return new ProductionPanelModel(false, string.Empty, string.Empty, Array.Empty<ProductionSlotModel>(), false, string.Empty);
    }

    private static string GroupRoleText(UnitKind kind)
    {
        return kind switch
        {
            UnitKind.Worker => GameUiText.GroupRoleWorker,
            UnitKind.Footman => GameUiText.GroupRoleFootman,
            UnitKind.Archer => GameUiText.GroupRoleArcher,
            UnitKind.Knight => GameUiText.GroupRoleKnight,
            UnitKind.Catapult => GameUiText.GroupRoleCatapult,
            _ => GameUiText.GroupRoleDefault
        };
    }
}
