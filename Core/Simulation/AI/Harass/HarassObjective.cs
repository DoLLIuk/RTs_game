using Godot;

namespace RtsNaGodote.Core.Simulation;

internal readonly record struct HarassObjective(HarassTargetKind Kind, Vector2 Position, int? EntityId, float Score);
