using System;
using System.Numerics;
using Hexa.NET.ImGui;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Renders a real-time 3D orientation gizmo from a quaternion by projecting a small 3D scene onto the
/// current ImGui window's draw list.
/// </summary>
internal static class OrientationView
{
    static readonly Vector4 xColor = new(0.90f, 0.28f, 0.28f, 1f);
    static readonly Vector4 yColor = new(0.33f, 0.80f, 0.33f, 1f);
    static readonly Vector4 zColor = new(0.36f, 0.52f, 1.00f, 1f);
    static readonly Vector4 referenceColor = new(0.45f, 0.45f, 0.50f, 0.55f);
    static readonly Vector4 originColor = new(0.85f, 0.85f, 0.88f, 1f);

    // NB: Fixed three-quarter view looking at the origin with world +Z up. The camera never moves, so
    // a rotating headstage is read against a stationary frame. Only the viewing angle lives here; the
    // orientation tracking comes entirely from rotating the model by the quaternion.
    static readonly Matrix4x4 View = Matrix4x4.CreateLookAt(
        Vector3.Normalize(new Vector3(1.0f, -1.0f, 0.6f)) * 4f,
        Vector3.Zero,
        Vector3.UnitZ);

    // NB: Fraction of the smaller viewport dimension used for a unit-length axis, leaving margin for
    // the arrowheads and labels.
    const float FillFraction = 0.32f;

    static readonly Comparison<BodyAxis> byDepthFarToNear = (a, b) => a.Depth.CompareTo(b.Depth);

    /// <summary>
    /// Draws the orientation gizmo for the given orientation into the current ImGui window/tab,
    /// filling the available content region.
    /// </summary>
    /// <param name="orientation">The current headstage orientation.</param>
    public static void Draw(Quaternion orientation)
    {
        var size = ImGui.GetContentRegionAvail();
        var topLeft = ImGui.GetCursorScreenPos();

        ImGui.InvisibleButton("##orientation_canvas", size);

        var q = SafeNormalize(orientation);
        var center = topLeft + size * 0.5f;
        float scale = Math.Min(size.X, size.Y) * FillFraction;

        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(topLeft, topLeft + size, true);

        var originScreen = Project(Vector3.Zero, center, scale, out _);

        // NB: static reference frame (world axes) so the fixed orientation is noticeable.
        uint referenceU32 = ImGui.ColorConvertFloat4ToU32(referenceColor);
        drawList.AddLine(originScreen, Project(Vector3.UnitX, center, scale, out _), referenceU32, 1.5f);
        drawList.AddLine(originScreen, Project(Vector3.UnitY, center, scale, out _), referenceU32, 1.5f);
        drawList.AddLine(originScreen, Project(Vector3.UnitZ, center, scale, out _), referenceU32, 1.5f);

        // NB: The body axes are the headstage's local unit vectors rotated into the world frame.
        var axes = new BodyAxis[3];
        axes[0] = MakeAxis(Vector3.Transform(Vector3.UnitX, q), center, scale, xColor, "X");
        axes[1] = MakeAxis(Vector3.Transform(Vector3.UnitY, q), center, scale, yColor, "Y");
        axes[2] = MakeAxis(Vector3.Transform(Vector3.UnitZ, q), center, scale, zColor, "Z");

        Array.Sort(axes, byDepthFarToNear);
        foreach (var axis in axes)
            DrawArrow(drawList, originScreen, axis.Tip, axis.Color, axis.Label);

        drawList.AddCircleFilled(originScreen, 3.5f, ImGui.ColorConvertFloat4ToU32(originColor));
        drawList.PopClipRect();
    }

    static BodyAxis MakeAxis(Vector3 direction, Vector2 center, float scale, Vector4 color, string label)
    {
        var tip = Project(direction, center, scale, out float depth);
        return new BodyAxis(tip, depth, ImGui.ColorConvertFloat4ToU32(color), label);
    }

    static void DrawArrow(ImDrawListPtr drawList, Vector2 from, Vector2 to, uint color, string label)
    {
        drawList.AddLine(from, to, color, 2.5f);

        var delta = to - from;
        float length = delta.Length();
        if (length < 1e-3f)
            return;

        var direction = delta / length;

        const float headLength = 11f;
        const float headHalfWidth = 6f;
        if (length > headLength)
        {
            var perpendicular = new Vector2(-direction.Y, direction.X);
            var baseCenter = to - direction * headLength;
            drawList.AddTriangleFilled(
                to,
                baseCenter + perpendicular * headHalfWidth,
                baseCenter - perpendicular * headHalfWidth,
                color);
        }

        drawList.AddText(to + direction * 6f, color, label);
    }

    static Vector2 Project(Vector3 world, Vector2 center, float scale, out float depth)
    {
        var camera = Vector3.Transform(world, View);
        depth = camera.Z;
        return new Vector2(center.X + scale * camera.X, center.Y - scale * camera.Y);
    }

    static Quaternion SafeNormalize(Quaternion q)
    {
        var lengthSquared = q.LengthSquared();
        if (float.IsNaN(lengthSquared) || lengthSquared < 1e-8f)
            return Quaternion.Identity;

        return Quaternion.Normalize(q);
    }

    readonly struct BodyAxis
    {
        public BodyAxis(Vector2 tip, float depth, uint color, string label)
        {
            Tip = tip;
            Depth = depth;
            Color = color;
            Label = label;
        }

        public Vector2 Tip { get; }
        public float Depth { get; }
        public uint Color { get; }
        public string Label { get; }
    }
}
