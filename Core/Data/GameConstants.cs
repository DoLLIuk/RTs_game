namespace RtsNaGodote.Core.Data;

public static class GameConstants
{
	public const int TileSize = 32;
	public const int MapWidth = 64;
	public const int MapHeight = 64;

	public const int ViewWidth = 1844;
	public const int ViewHeight = 1036;

	public const float ArrivalRadius = 13f;
	public const int DefaultSeed = 42;
	public const float CameraZoom = 0.82f;
	public const float CameraMinZoom = 0.38f;
	public const float CameraMaxZoom = 1.85f;
	public const float CameraZoomStep = 0.08f;
	public const float CameraSpeed = 760f;
	public const int EdgeScrollPixels = 18;

	public const int StartingGold = 430;
	public const int StartingLumber = 230;
	public const int StartingFoodCap = 6;

	public const int WorkerCarry = 10;
	public const int GatherTimeMs = 1650;
	public const float GatherReachPaddingTiles = 1.5f;
	public const int AITickMs = 700;
	public const float SelectionDragThreshold = 12f;
	public const float GroupSpacing = 34f;
	public const float RepathIntervalMs = 500f;
	public const float StuckRepathDelayMs = 650f;
	public const float StuckMovedEpsilon = 0.2f;
	public const float PathProgressImprovementEpsilon = 4f;
	public const float LocalAvoidanceStep = 8f;
	public const float WorkerFlowLaneOffset = 10f;
	public const float DeadlockResolveTriggerMs = 140f;
	public const float DeadlockResolveMinOverlap = 2.5f;
	public const float DeadlockYieldMinStep = 3.5f;
	public const float DeadlockYieldMaxStep = 6f;
	public const float WorkerSafeCombatHallRadiusMultiplier = 1.5f;
	public const float WorkerCombatLeashHallRadiusMultiplier = 3.0f;
	public const float WorkerThreatQuietWindowMs = 850f;
	public const float WorkerThreatCheckRadiusTiles = 5.5f;
	public const int UnderAttackRevealTiles = 1;
}
