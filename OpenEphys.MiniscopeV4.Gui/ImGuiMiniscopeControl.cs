using Bonsai.ImGui.Design;
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpenEphys.MiniscopeV4.Gui;

sealed class ImGuiMiniscopeControl : ImGuiControl
{
    static readonly IntPtr PerMonitorAwareV2 = new(-4);
    const int DpiHostingBehaviorInvalid = -1;
    const int DpiHostingBehaviorMixed = 1;

    int lastLoggedDpi;

    protected override void CreateHandle()
    {
        IntPtr previousContext = IntPtr.Zero;
        int previousHosting = DpiHostingBehaviorInvalid;
        try { previousHosting = SetThreadDpiHostingBehavior(DpiHostingBehaviorMixed); } catch (EntryPointNotFoundException) { }
        try { previousContext = SetThreadDpiAwarenessContext(PerMonitorAwareV2); } catch (EntryPointNotFoundException) { }

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

    // NB: Capture the DPI scale before the base class creates the ImGui context and builds the font
    // atlas, so the font and style are sized for the monitor the window is created on.
    protected override void OnHandleCreated(EventArgs e)
    {
        UpdateScaleFromDpi();
        base.OnHandleCreated(e);
    }

    void UpdateScaleFromDpi()
    {
        if (!IsHandleCreated)
            return;

        int dpi = GetWindowDpi();
        UiScale.SetFromDpi(dpi);

        if (dpi > 0 && dpi != lastLoggedDpi)
        {
            lastLoggedDpi = dpi;
        }
    }

    int GetWindowDpi()
    {
        try { return (int)GetDpiForWindow(Handle); }
        catch (EntryPointNotFoundException) { return DeviceDpi; }
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
