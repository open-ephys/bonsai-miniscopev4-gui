using Bonsai.ImGui.Design;
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpenEphys.MiniscopeV4.Gui;

sealed class ImGuiMiniscopeControl : ImGuiControl
{
    static readonly IntPtr PerMonitorAwareV2 = new(-4); // NB: a Win32 pseudo-handle (value -4): DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
    const int DpiHostingBehaviorInvalid = -1;
    const int DpiHostingBehaviorMixed = 1;

    bool DpiApiUnavailable = false;

    protected override void CreateHandle()
    {
        // NB: Bonsai is system-DPI-aware, so we must create this control's handle under a per-monitor-aware
        // thread context to avoid bitmap scaling on high-DPI monitors.
        IntPtr previousContext = IntPtr.Zero;
        int previousHosting = DpiHostingBehaviorInvalid;
        try { previousHosting = SetThreadDpiHostingBehavior(DpiHostingBehaviorMixed); }
        catch (EntryPointNotFoundException) { ReportDpiApiUnavailable(); }

        try { previousContext = SetThreadDpiAwarenessContext(PerMonitorAwareV2); }
        catch (EntryPointNotFoundException) { ReportDpiApiUnavailable(); }

        try
        {
            base.CreateHandle();
        }
        finally
        {
            if (previousContext != IntPtr.Zero)
                SetThreadDpiAwarenessContext(previousContext);
            if (previousHosting >= 0)
                SetThreadDpiHostingBehavior(previousHosting);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        UiScale.SetFromDpi(GetWindowDpi()); // NB: Capture the monitor DPI before the base class creates the ImGui context
        base.OnHandleCreated(e);
    }

    int GetWindowDpi()
    {
        try { return (int)GetDpiForWindow(Handle); }
        catch (EntryPointNotFoundException) { ReportDpiApiUnavailable(); return DeviceDpi; }
    }

    void ReportDpiApiUnavailable()
    {
        if (DpiApiUnavailable)
            return;

        DpiApiUnavailable = true;
        MiniscopeLog.Warning(
            "Per-monitor DPI scaling is unavailable on this version of Windows (requires Windows 10 1803 or later); " +
            "the UI will scale to the system DPI instead.");
    }

    // NB: By default WinForms treats the arrow keys and Tab as dialog navigation keys and
    // consumes them before they reach WndProc, which prevents the Win32 backend from
    // forwarding them to ImGui (e.g. moving the caret inside an InputText). Claim them as
    // regular input so they reach the ImGui context.
    protected override bool IsInputKey(Keys keyData)
    {
        return (keyData & Keys.KeyCode) switch
        {
            Keys.Left or Keys.Right or Keys.Up or Keys.Down => true,
            _ => base.IsInputKey(keyData),
        };
    }

    // NB: Overrides ProcessDialogKey to suppress Escape so the Bonsai visualizer dialog
    // does not close when the user presses Escape inside the ImGui window.
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Escape)
            return true;
        return base.ProcessDialogKey(keyData);
    }

    [DllImport("user32.dll")]
    static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    static extern int SetThreadDpiHostingBehavior(int value);

    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr hWnd);
}
