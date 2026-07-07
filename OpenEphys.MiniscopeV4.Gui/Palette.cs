using Hexa.NET.ImGui;
using System.Numerics;

namespace OpenEphys.MiniscopeV4.Gui;

internal static class Palette
{
    public static readonly Vector4 Green = new(0.220f, 0.580f, 0.340f, 1f);
    public static readonly Vector4 GreenHovered = new(0.256f, 0.800f, 0.437f, 1f);
    public static readonly Vector4 GreenActive = new(0.150f, 0.680f, 0.326f, 1f);
    public static readonly Vector4 GreenDimmed = new(0.293f, 0.450f, 0.345f, 1f);

    public static readonly Vector4 Red = new(0.580f, 0.244f, 0.220f, 1f);
    public static readonly Vector4 RedHovered = new(0.800f, 0.292f, 0.256f, 1f);
    public static readonly Vector4 RedActive = new(0.680f, 0.185f, 0.150f, 1f);
    public static readonly Vector4 RedDimmed = new(0.450f, 0.303f, 0.293f, 1f);

    public static readonly Vector4 Yellow = new(0.580f, 0.484f, 0.220f, 1f);
    public static readonly Vector4 YellowHovered = new(0.800f, 0.655f, 0.256f, 1f);
    public static readonly Vector4 YellowActive = new(0.680f, 0.539f, 0.150f, 1f);
    public static readonly Vector4 YellowDimmed = new(0.450f, 0.408f, 0.293f, 1f);

    public static readonly Vector4 Purple = new(0.430f, 0.220f, 0.580f, 1f);
    public static readonly Vector4 PurpleHovered = new(0.573f, 0.256f, 0.800f, 1f);
    public static readonly Vector4 PurpleActive = new(0.459f, 0.150f, 0.680f, 1f);
    public static readonly Vector4 PurpleDimmed = new(0.384f, 0.293f, 0.450f, 1f);

    public static readonly Vector4 Blue = new(0.220f, 0.244f, 0.580f, 1f);
    public static readonly Vector4 BlueHovered = new(0.256f, 0.292f, 0.800f, 1f);
    public static readonly Vector4 BlueActive = new(0.150f, 0.185f, 0.680f, 1f);
    public static readonly Vector4 BlueDimmed = new(0.293f, 0.303f, 0.450f, 1f);

    public readonly ref struct StyleColorScope
    {
        readonly int count;
        internal StyleColorScope(int count) => this.count = count;
        public void Dispose() => ImGui.PopStyleColor(count);
    }

    public static StyleColorScope PushColor(ImGuiCol idx, Vector4 color)
    {
        ImGui.PushStyleColor(idx, color);
        return new StyleColorScope(1);
    }

    public static StyleColorScope PushButtonColors(Vector4 baseColor, Vector4 hoveredColor, Vector4 activeColor)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, baseColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, activeColor);
        return new StyleColorScope(3);
    }
}
