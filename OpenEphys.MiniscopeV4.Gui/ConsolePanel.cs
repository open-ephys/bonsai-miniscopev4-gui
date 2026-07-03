using Bonsai;
using Hexa.NET.ImGui;
using System;
using System.ComponentModel;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Renders the console log docked at the bottom of the GUI. The panel spans the full width of
/// the window and is preceded by a draggable splitter that resizes its height. Each source value
/// is forwarded unchanged.
/// </summary>
[Combinator]
[Description("Renders the full-width console log docked at the bottom of the GUI.")]
public class ConsolePanel
{
    static readonly Vector4 colorError = new(0.9f, 0.3f, 0.3f, 1f);
    static readonly Vector4 colorWarning = new(0.9f, 0.8f, 0.3f, 1f);
    static readonly Vector4 colorInfo = new(0.8f, 0.8f, 0.8f, 1f);
    static readonly Vector4 colorHint = new(0.6f, 0.6f, 0.6f, 1f);

    static void RenderLogLines(LogEntry[] entries)
    {
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var color = entry.Level switch
            {
                LogLevel.Error => colorError,
                LogLevel.Warning => colorWarning,
                _ => colorInfo,
            };
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextUnformatted($"[{entry.Timestamp:HH:mm:ss}] {entry.Message}");
            ImGui.PopStyleColor();
        }
    }

    /// <summary>
    /// Renders the console for each source value and forwards the value unchanged.
    /// </summary>
    /// <param name="source">A sequence of values tied to the render tick of DearImGui.</param>
    /// <returns>The unmodified <paramref name="source"/> sequence.</returns>
    public IObservable<TSource> Process<TSource>(IObservable<TSource> source)
    {
        return Observable.Create<TSource>(observer =>
        {
            int lastLogVersion = -1;
            int scrollVersion = -1;
            LogEntry[] logCache = Array.Empty<LogEntry>();

            var sourceObserver = Observer.Create<TSource>(value =>
            {
                ImGui.InvisibleButton("##console_splitter", new Vector2(-1f, ConsoleLayout.SplitterThickness));

                bool hovered = ImGui.IsItemHovered();
                bool active = ImGui.IsItemActive();

                if (active)
                    ConsoleLayout.ConsoleHeight -= ImGui.GetIO().MouseDelta.Y;
                if (hovered || active)
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);
                var drawList = ImGui.GetWindowDrawList();

                Vector2 min = ImGui.GetItemRectMin();
                Vector2 max = ImGui.GetItemRectMax();

                float y = (min.Y + max.Y) * 0.5f;

                uint color =
                    active ? ImGui.GetColorU32(ImGuiCol.SeparatorActive) :
                    hovered ? ImGui.GetColorU32(ImGuiCol.SeparatorHovered) :
                              ImGui.GetColorU32(ImGuiCol.Separator);

                float thickness =
                    active ? 3.0f :
                    hovered ? 2.0f :
                              1.0f;

                drawList.AddLine(
                    new Vector2(min.X, y),
                    new Vector2(max.X, y),
                    color,
                    thickness);

                int logVersion = MiniscopeLog.Version;
                if (logVersion != lastLogVersion)
                {
                    logCache = MiniscopeLog.Snapshot();
                    lastLogVersion = logVersion;
                }

                if (ImGui.BeginChild("##console", new Vector2(-1f, -1f), ImGuiChildFlags.Borders))
                {
                    if (ImGui.Button("Clear##console_clear"))
                        MiniscopeLog.Clear();

                    ImGui.SameLine();
                    ImGui.TextUnformatted($"Console — {logCache.Length} message(s)");
                    ImGui.Separator();

                    if (ImGui.BeginChild("##console_scroll", new Vector2(-1f, -1f), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
                    {
                        if (logCache.Length == 0)
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, colorHint);
                            ImGui.TextUnformatted("No messages yet.");
                            ImGui.PopStyleColor();
                        }
                        else
                        {
                            RenderLogLines(logCache);
                            if (logVersion != scrollVersion)
                            {
                                ImGui.SetScrollHereY(1f);
                                scrollVersion = logVersion;
                            }
                        }
                    }

                    ImGui.EndChild();
                }

                ImGui.EndChild();

                observer.OnNext(value);
            },
            observer.OnError,
            observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }
}
