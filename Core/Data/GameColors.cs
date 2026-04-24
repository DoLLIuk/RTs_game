using Godot;

namespace RtsNaGodote.Core.Data;

public static class GameColors
{
	public static readonly Color Grass = FromHex(0x2f5627);
	public static readonly Color Grass2 = FromHex(0x3b6730);
	public static readonly Color Forest = FromHex(0x14381d);
	public static readonly Color Stone = FromHex(0x596066);
	public static readonly Color Water = FromHex(0x123d5b);
	public static readonly Color Dirt = FromHex(0x58482e);
	public static readonly Color GoldMine = FromHex(0xd9ad3d);
	public static readonly Color Player = FromHex(0x4aa3ff);
	public static readonly Color AI = FromHex(0xd24a3a);
	public static readonly Color Selection = Colors.White;
	public static readonly Color SelectionShadow = new(0f, 0f, 0f, 0.45f);
	public static readonly Color PanelBackground = new(0.07f, 0.09f, 0.1f, 0.82f);

	private static Color FromHex(uint hex)
	{
		return Color.Color8(
			(byte)((hex >> 16) & 0xff),
			(byte)((hex >> 8) & 0xff),
			(byte)(hex & 0xff));
	}
}
