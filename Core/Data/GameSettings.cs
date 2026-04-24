using System.Collections.Generic;

namespace RtsNaGodote.Core.Data;

public enum Difficulty
{
    Easy,
    Normal,
    Hard
}

public enum AiProfile
{
    Push,
    Harass
}

public sealed record DifficultyDefinition(
    string Label,
    int AiDelayMs,
    int TargetWorkers,
    int ScoutDelayMs,
    int ScoutMaxExposureMs,
    int ScoutReentryDelayMs,
    float ScoutSectorRepeatPenalty,
    float ScoutThreatTolerance,
    int PushMinPower,
    int HarassMinPower,
    float AttackAdvantageRatio,
    float RetreatRatio,
    int RegroupDurationMs,
    int DefendRadiusTiles);

public readonly record struct GameInit(
    Race PlayerRace,
    Difficulty Difficulty,
    int Seed,
    AiProfile AiProfile)
{
    public Race AIRace => PlayerRace == Race.Alliance ? Race.Horde : Race.Alliance;
}

public static class GameSettings
{
    public static readonly IReadOnlyDictionary<Difficulty, DifficultyDefinition> Difficulties = new Dictionary<Difficulty, DifficultyDefinition>
    {
        [Difficulty.Easy] = new("Easy", 1200, 7, 7000, 1350, 1650, 120f, 0.2f, 9, 7, 1.32f, 0.72f, 5200, 11),
        [Difficulty.Normal] = new("Normal", 750, 10, 4200, 900, 900, 72f, 0.8f, 11, 8, 1.08f, 0.58f, 4000, 12),
        [Difficulty.Hard] = new("Hard", 520, 13, 2500, 650, 550, 42f, 1.35f, 12, 9, 0.92f, 0.46f, 3000, 13)
    };

    public static DifficultyDefinition GetDifficulty(Difficulty difficulty)
    {
        return Difficulties[difficulty];
    }
}
