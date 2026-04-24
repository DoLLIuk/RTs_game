using System.Collections.Generic;

namespace RtsNaGodote.Core.Data;

public enum Race
{
    Alliance,
    Horde
}

public enum Side
{
    Player = 0,
    AI = 1,
    Neutral = 2
}

public enum UnitKind
{
    Worker,
    Footman,
    Archer,
    Knight,
    Catapult
}

public enum BuildingKind
{
    TownHall,
    Farm,
    Barracks,
    Workshop,
    Tower
}

public enum ResourceType
{
    Gold,
    Lumber
}

public sealed record UnitDefinition(
    int Hp,
    float Speed,
    float Size,
    int Attack,
    float Range,
    int CooldownMs,
    int Sight,
    int CostGold,
    int CostLumber,
    int Food,
    int BuildTimeMs,
    int Score,
    BuildingKind Producer,
    BuildingKind? Requires,
    float SplashRadius,
    int BonusVsBuilding,
    string Hotkey,
    string AllianceLabel,
    string HordeLabel);

public sealed record BuildingDefinition(
    int Hp,
    int Size,
    int Sight,
    int FoodCapBonus,
    int CostGold,
    int CostLumber,
    int BuildTimeMs,
    int Attack,
    float Range,
    int CooldownMs,
    string Hotkey,
    string AllianceLabel,
    string HordeLabel);

public sealed record ResourceDefinition(
    int Amount,
    int TileWidth,
    int TileHeight,
    float Radius);

public sealed class PlayerState
{
    public required Side Side { get; init; }
    public required Race Race { get; init; }
    public int Gold { get; set; }
    public int Lumber { get; set; }
    public int Food { get; set; }
    public int FoodCap { get; set; }
}

public static class GameDefinitions
{
    public static readonly IReadOnlyDictionary<UnitKind, UnitDefinition> Units = new Dictionary<UnitKind, UnitDefinition>
    {
        [UnitKind.Worker] = new(42, 94f, 14f, 4, 24f, 1150, 6, 50, 0, 1, 10500, 1, BuildingKind.TownHall, null, 0f, 0, "E", "Peasant", "Peon"),
        [UnitKind.Footman] = new(86, 76f, 16f, 11, 28f, 950, 7, 80, 0, 1, 14500, 2, BuildingKind.Barracks, null, 0f, 0, "F", "Footman", "Grunt"),
        [UnitKind.Archer] = new(52, 82f, 14f, 8, 168f, 1350, 8, 70, 40, 1, 15500, 2, BuildingKind.Barracks, null, 0f, 0, "G", "Ranger", "Headhunter"),
        [UnitKind.Knight] = new(132, 112f, 20f, 18, 30f, 950, 8, 145, 35, 2, 21000, 4, BuildingKind.Barracks, BuildingKind.Workshop, 0f, 0, "K", "Knight", "Raider"),
        [UnitKind.Catapult] = new(115, 54f, 24f, 28, 230f, 2450, 9, 170, 110, 3, 26500, 6, BuildingKind.Workshop, null, 52f, 22, "C", "Ballista", "Catapult")
    };

    public static readonly IReadOnlyDictionary<BuildingKind, BuildingDefinition> Buildings = new Dictionary<BuildingKind, BuildingDefinition>
    {
        [BuildingKind.TownHall] = new(950, 3, 8, 6, 400, 200, 24000, 0, 0f, 0, "H", "Town Hall", "Great Hall"),
        [BuildingKind.Farm] = new(420, 2, 4, 5, 80, 40, 11500, 0, 0f, 0, "F", "Farm", "Burrow"),
        [BuildingKind.Barracks] = new(650, 3, 6, 0, 155, 85, 18000, 0, 0f, 0, "B", "Barracks", "Barracks"),
        [BuildingKind.Workshop] = new(620, 3, 6, 0, 170, 120, 21000, 0, 0f, 0, "V", "Workshop", "Siege Lodge"),
        [BuildingKind.Tower] = new(470, 2, 9, 0, 120, 100, 16500, 12, 190f, 1200, "T", "Guard Tower", "Watch Tower")
    };

    public static readonly IReadOnlyDictionary<ResourceType, ResourceDefinition> Resources = new Dictionary<ResourceType, ResourceDefinition>
    {
        [ResourceType.Gold] = new(1800, 3, 3, GameConstants.TileSize * 1.4f),
        [ResourceType.Lumber] = new(180, 1, 1, GameConstants.TileSize * 0.45f)
    };

    public static string UnitLabel(UnitKind kind, Race race)
    {
        var definition = Units[kind];
        return race == Race.Alliance ? definition.AllianceLabel : definition.HordeLabel;
    }

    public static string BuildingLabel(BuildingKind kind, Race race)
    {
        var definition = Buildings[kind];
        return race == Race.Alliance ? definition.AllianceLabel : definition.HordeLabel;
    }
}
