using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Units;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Game.Presentation;

public partial class UnitView : Node2D
{
	private SimUnit? _unit;
	private bool _selected;
	private bool _fogVisible = true;

	public void Bind(SimUnit unit)
	{
		_unit = unit;
		SyncFromSimulation(false);
	}

	public void SyncFromSimulation(bool selected)
	{
		if (_unit is null)
		{
			return;
		}

		_selected = selected;
		GlobalPosition = _unit.Position;
		Visible = _unit.Alive && _fogVisible;
		QueueRedraw();
	}

	public void ApplyFogState(bool visible)
	{
		_fogVisible = visible;
		Visible = (_unit?.Alive ?? false) && visible;
	}

	public override void _Draw()
	{
		if (_unit is null || !_unit.Alive)
		{
			return;
		}

		var fill = _unit.Side == GameSide.Player ? GameColors.Player : GameColors.AI;
		if (_selected)
		{
			DrawArc(Vector2.Zero + Vector2.Down * 2f, _unit.Radius + 8f, 0f, Mathf.Tau, 32, GameColors.SelectionShadow, 4f);
			DrawArc(Vector2.Zero, _unit.Radius + 6f, 0f, Mathf.Tau, 32, GameColors.Selection, 2f);
		}

		DrawCircle(new Vector2(0f, 5f), _unit.Radius * 0.92f, new Color(0f, 0f, 0f, 0.22f));
		DrawUnitBody(fill);
		DrawHpBar();
		DrawCargoBadge();
	}

	private void DrawUnitBody(Color fill)
	{
		if (_unit is null)
		{
			return;
		}

		switch (_unit.Kind)
		{
			case UnitKind.Worker:
				DrawCircle(Vector2.Zero, _unit.Radius, fill);
				DrawLine(new Vector2(-2f, -_unit.Radius * 0.8f), new Vector2(_unit.Radius * 0.7f, _unit.Radius * 0.3f), Colors.Wheat, 2f);
				break;
			case UnitKind.Footman:
				DrawRect(new Rect2(-_unit.Radius, -_unit.Radius, _unit.Radius * 2f, _unit.Radius * 2f), fill);
				DrawRect(new Rect2(-_unit.Radius * 0.35f, -_unit.Radius * 0.4f, _unit.Radius * 0.7f, _unit.Radius * 1.1f), Colors.WhiteSmoke);
				break;
			case UnitKind.Archer:
				DrawPolygon(
					[new Vector2(0f, -_unit.Radius), new Vector2(_unit.Radius, 0f), new Vector2(0f, _unit.Radius), new Vector2(-_unit.Radius, 0f)],
					[fill]);
				DrawArc(Vector2.Zero + new Vector2(3f, 0f), _unit.Radius * 0.65f, -0.9f, 0.9f, 16, Colors.Wheat, 2f);
				break;
			case UnitKind.Knight:
				DrawPolygon(
					[
						new Vector2(-_unit.Radius * 0.9f, -_unit.Radius * 0.1f),
						new Vector2(-_unit.Radius * 0.35f, -_unit.Radius),
						new Vector2(_unit.Radius * 0.55f, -_unit.Radius * 0.8f),
						new Vector2(_unit.Radius, 0f),
						new Vector2(_unit.Radius * 0.35f, _unit.Radius),
						new Vector2(-_unit.Radius * 0.7f, _unit.Radius * 0.7f)
					],
					[fill]);
				DrawLine(new Vector2(-_unit.Radius * 0.7f, -_unit.Radius * 0.4f), new Vector2(_unit.Radius * 0.8f, _unit.Radius * 0.35f), Colors.Wheat, 2f);
				break;
			case UnitKind.Catapult:
				DrawRect(new Rect2(-_unit.Radius, -_unit.Radius * 0.55f, _unit.Radius * 2f, _unit.Radius * 1.1f), fill);
				DrawCircle(new Vector2(-_unit.Radius * 0.6f, _unit.Radius * 0.6f), _unit.Radius * 0.35f, Colors.SaddleBrown);
				DrawCircle(new Vector2(_unit.Radius * 0.6f, _unit.Radius * 0.6f), _unit.Radius * 0.35f, Colors.SaddleBrown);
				DrawLine(new Vector2(-_unit.Radius * 0.2f, -_unit.Radius * 0.7f), new Vector2(_unit.Radius * 0.8f, -_unit.Radius * 1.1f), Colors.Wheat, 2f);
				break;
		}
	}

	private void DrawHpBar()
	{
		if (_unit is null)
		{
			return;
		}

		var width = _unit.Radius * 2.2f;
		var topLeft = new Vector2(-width / 2f, -_unit.Radius - 10f);
		DrawRect(new Rect2(topLeft, new Vector2(width, 5f)), new Color(0f, 0f, 0f, 0.55f));
		DrawRect(new Rect2(topLeft, new Vector2(width * (_unit.Hp / (float)_unit.MaxHp), 5f)), new Color(0.24f, 0.84f, 0.29f));
	}

	private void DrawCargoBadge()
	{
		if (_unit is null || _unit.CargoType is null || _unit.CargoAmount <= 0)
		{
			return;
		}

		var color = _unit.CargoType == ResourceType.Gold ? GameColors.GoldMine : new Color(0.2f, 0.52f, 0.2f);
		DrawCircle(new Vector2(_unit.Radius * 0.7f, -_unit.Radius * 0.7f), 5f, color);
	}
}
