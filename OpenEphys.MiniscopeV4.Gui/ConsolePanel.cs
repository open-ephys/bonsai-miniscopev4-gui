using Bonsai;
using Hexa.NET.ImGui;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Renders the console log docked at the bottom of the GUI. The panel spans the full width of
/// the window and is preceded by a draggable splitter that resizes its height.
/// </summary>
[Combinator]
[Description("Renders the full-width console log docked at the bottom of the GUI.")]
public class ConsolePanel
{
    static bool IsVisible(LogLevel level, bool showInfo, bool showWarnings, bool showErrors, bool showPropertyChanges) =>
        level switch
        {
            LogLevel.Warning => showWarnings,
            LogLevel.Error => showErrors,
            LogLevel.PropertyChanged => showPropertyChanges,
            LogLevel.Info => showInfo,
            _ => true,
        };

    static string FormatSelectedEntries(LogEntry[] entries, HashSet<int> selectedIndices) =>
        string.Join("\n", selectedIndices.OrderBy(i => i).Select(i => entries[i].ToString()));

    static string FormatAllEntries(LogEntry[] entries, bool showInfo, bool showWarnings, bool showErrors, bool showPropertyChanges) =>
        string.Join("\n", entries
            .Where(e => IsVisible(e.Level, showInfo, showWarnings, showErrors, showPropertyChanges))
            .Select(e => e.ToString()));

    static int RenderLogLines(LogEntry[] entries, bool showInfo, bool showWarnings, bool showErrors, bool showPropertyChanges,
        HashSet<int> selectedIndices, ref int lastClickedIndex)
    {
        int rendered = 0;

        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];

            if (!IsVisible(entry.Level, showInfo, showWarnings, showErrors, showPropertyChanges))
                continue;

            rendered++;

            ImGui.PushID(i);

            var text = entry.SimpleString();
            Vector2 textSize = ImGui.CalcTextSize(text);

            bool isSelected = selectedIndices.Contains(i);
            if (ImGui.Selectable("##sel", isSelected, ImGuiSelectableFlags.AllowOverlap, new Vector2(0, textSize.Y)))
            {
                bool ctrl = ImGui.GetIO().KeyCtrl;
                bool shift = ImGui.GetIO().KeyShift;

                if (shift && lastClickedIndex >= 0)
                {
                    selectedIndices.Clear();
                    int lo = Math.Min(lastClickedIndex, i);
                    int hi = Math.Max(lastClickedIndex, i);
                    for (int j = lo; j <= hi; j++)
                    {
                        if (IsVisible(entries[j].Level, showInfo, showWarnings, showErrors, showPropertyChanges))
                            selectedIndices.Add(j);
                    }
                }
                else if (ctrl)
                {
                    if (!selectedIndices.Add(i))
                        selectedIndices.Remove(i);
                    lastClickedIndex = i;
                }
                else
                {
                    selectedIndices.Clear();
                    selectedIndices.Add(i);
                    lastClickedIndex = i;
                }
            }

            if (ImGui.BeginPopupContextItem("##ctx"))
            {
                if (!selectedIndices.Contains(i))
                {
                    selectedIndices.Clear();
                    selectedIndices.Add(i);
                    lastClickedIndex = i;
                }

                if (ImGui.MenuItem("Copy Selected"))
                    ImGui.SetClipboardText(FormatSelectedEntries(entries, selectedIndices));
                if (ImGui.MenuItem("Copy All"))
                    ImGui.SetClipboardText(FormatAllEntries(entries, showInfo, showWarnings, showErrors, showPropertyChanges));

                ImGui.EndPopup();
            }

            ImGui.PopID();

            ImGui.SameLine(0, 0);

            switch (entry.Level)
            {
                case LogLevel.Error:
                    using (Palette.PushColor(ImGuiCol.Text, Palette.RedHovered))
                        ImGui.TextUnformatted(text);
                    break;

                case LogLevel.Warning:
                    using (Palette.PushColor(ImGuiCol.Text, Palette.YellowHovered))
                        ImGui.TextUnformatted(text);
                    break;

                case LogLevel.PropertyChanged:
                    using (Palette.PushColor(ImGuiCol.Text, Palette.BlueHovered))
                        ImGui.TextUnformatted(text);
                    break;

                default:
                    ImGui.TextUnformatted(text);
                    break;
            }
        }

        return rendered;
    }

    /// <summary>
    /// Renders the console for each layout value and returns the updated layout.
    /// </summary>
    /// <param name="source">A sequence of shared <see cref="GuiLayout"/> values threaded through the render tick of DearImGui.</param>
    /// <param name="logSource">The shared <see cref="MiniscopeLog"/> instance (typically a <c>BehaviorSubject</c>), captured once and read (via <see cref="MiniscopeLog.Snapshot"/>) to render the scrollback.</param>
    /// <returns>A sequence of the updated <see cref="GuiLayout"/> values.</returns>
    public IObservable<GuiLayout> Process(IObservable<GuiLayout> source, IObservable<MiniscopeLog> logSource)
    {
        return Observable.Create<GuiLayout>(observer =>
        {
            int lastLogVersion = -1;
            int scrollVersion = -1;
            LogEntry[] logCache = Array.Empty<LogEntry>();

            bool showInfo = true;
            bool showWarnings = true;
            bool showErrors = true;
            bool showPropertyChanges = true;

            var selectedIndices = new HashSet<int>();
            int lastClickedIndex = -1;

            // NB: Expect this to be a BehaviorSubject, so we can take the first value immediately.
            MiniscopeLog log = null;
            var logSubscription = logSource.Take(1).Subscribe(value => log = value);

            if (log == null)
            {
                throw new InvalidOperationException("No MiniscopeLog instance was provided.");
            }

            var sourceObserver = Observer.Create<GuiLayout>(layout =>
            {
                if (!layout.ImageExpanded)
                {
                    layout = layout with { ConsoleHeight = layout.ClampConsoleHeight(layout.ConsoleHeight, ImGui.GetWindowHeight()) };

                    bool consoleOpen = layout.ConsoleOpen;

                    if (consoleOpen)
                    {
                        ImGui.InvisibleButton("##console_splitter", new Vector2(-1f, layout.ConsoleSplitterThickness));

                        bool hovered = ImGui.IsItemHovered();
                        bool active = ImGui.IsItemActive();

                        if (active)
                            layout = layout with { ConsoleHeight = layout.ClampConsoleHeight(layout.ConsoleHeight - ImGui.GetIO().MouseDelta.Y, ImGui.GetWindowHeight()) };
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
                    }

                    int logVersion = log.Version;
                    if (logVersion != lastLogVersion)
                    {
                        logCache = log.Snapshot();
                        lastLogVersion = logVersion;
                    }

                    if (ImGui.BeginChild("##console", new Vector2(-1f, -1f), ImGuiChildFlags.Borders))
                    {
                        float rowWidth = ImGui.GetContentRegionAvail().X;

                        if (!consoleOpen)
                        {
                            // Same click-anywhere/hover-highlight affordance as SettingsPanel's collapsed
                            // column: an invisible button behind the row catches clicks/hover across the
                            // whole collapsed strip, and the real ArrowButton drawn on top is tinted to
                            // look hovered whenever that larger area is.
                            Vector2 collapsedAreaSize = ImGui.GetContentRegionAvail();
                            Vector2 arrowPos = ImGui.GetCursorScreenPos();

                            bool areaClicked = ImGui.InvisibleButton("##console_open_area", collapsedAreaSize);
                            bool areaHovered = ImGui.IsItemHovered();
                            if (areaHovered)
                                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                            ImGui.SetCursorScreenPos(arrowPos);

                            if (areaHovered)
                                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonHovered));

                            ImGui.ArrowButton("##console_toggle", ImGuiDir.Up);
                            Tooltip.Describe("Expand the console.");

                            if (areaHovered)
                                ImGui.PopStyleColor();

                            ImGui.SameLine();
                            ImGui.TextUnformatted($"Console — {logCache.Length} message(s)");

                            if (areaClicked)
                                layout = layout with { ConsoleOpen = true };
                        }
                        else
                        {
                            if (ImGui.ArrowButton("##console_toggle", ImGuiDir.Down))
                                layout = layout with { ConsoleOpen = false };
                            Tooltip.Describe("Collapse the console.");

                            ImGui.SameLine();

                            var controlsHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ScrollbarSize;
                            float clearWidth = ImGui.CalcTextSize("Clear").X + ImGui.GetStyle().FramePadding.X * 2f;
                            if (ImGui.BeginChild("##console_content", new Vector2(-(clearWidth * 1.5f), controlsHeight), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
                            {
                                ImGui.TextUnformatted($"Console — {logCache.Length} message(s)");

                                ImGui.SameLine();
                                Vector2 sepPos = ImGui.GetCursorScreenPos();
                                float sepHeight = ImGui.GetFrameHeight();
                                ImGui.GetWindowDrawList().AddLine(
                                    new Vector2(sepPos.X, sepPos.Y),
                                    new Vector2(sepPos.X, sepPos.Y + sepHeight),
                                    ImGui.GetColorU32(ImGuiCol.Separator));
                                ImGui.Dummy(new Vector2(1f, sepHeight));

                                ImGui.SameLine();
                                if (ImGui.Checkbox("Info##console_filter_info", ref showInfo) && !showInfo)
                                {
                                    selectedIndices.RemoveWhere(i => logCache[i].Level == LogLevel.Info);
                                }
                                Tooltip.Describe("Show or hide informational messages.");
                                ImGui.SameLine();
                                if (ImGui.Checkbox("Warnings##console_filter_warning", ref showWarnings) && !showWarnings)
                                {
                                    selectedIndices.RemoveWhere(i => logCache[i].Level == LogLevel.Warning);
                                }
                                Tooltip.Describe("Show or hide warning messages.");
                                ImGui.SameLine();
                                if (ImGui.Checkbox("Errors##console_filter_error", ref showErrors) && !showErrors)
                                {
                                    selectedIndices.RemoveWhere(i => logCache[i].Level == LogLevel.Error);
                                }
                                Tooltip.Describe("Show or hide error messages.");
                                ImGui.SameLine();
                                if (ImGui.Checkbox("Property Changes##console_filter_property", ref showPropertyChanges) && !showPropertyChanges)
                                {
                                    selectedIndices.RemoveWhere(i => logCache[i].Level == LogLevel.PropertyChanged);
                                }
                                Tooltip.Describe("Show or hide messages logged when hardware settings change.");
                            }

                            ImGui.EndChild();

                            ImGui.SameLine(rowWidth - clearWidth);
                            if (ImGui.Button("Clear##console_clear"))
                            {
                                log.Clear();
                                selectedIndices.Clear();
                                lastClickedIndex = -1;
                            }
                            Tooltip.Describe("Remove all messages from the console.");

                            ImGui.Separator();

                            if (ImGui.BeginChild("##console_scroll", new Vector2(-1f, -1f), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
                            {
                                if (logCache.Length == 0)
                                {
                                    ImGui.TextDisabled("No messages yet.");
                                }
                                else
                                {
                                    int visibleCount = RenderLogLines(logCache, showInfo, showWarnings, showErrors, showPropertyChanges, selectedIndices, ref lastClickedIndex);
                                    if (visibleCount == 0)
                                    {
                                        ImGui.TextDisabled("No messages match the current filter.");
                                    }
                                    else if (logVersion != scrollVersion)
                                    {
                                        ImGui.SetScrollHereY(1f);
                                        scrollVersion = logVersion;
                                    }
                                }

                                if (ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows) && ImGui.GetIO().KeyCtrl &&
                                    ImGui.IsKeyPressed(ImGuiKey.C) && selectedIndices.Count > 0)
                                {
                                    ImGui.SetClipboardText(FormatSelectedEntries(logCache, selectedIndices));
                                }
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndChild();
                }

                observer.OnNext(layout);
            },
            observer.OnError,
            observer.OnCompleted);

            return new CompositeDisposable(logSubscription, source.SubscribeSafe(sourceObserver));
        });
    }
}
