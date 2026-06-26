using Bonsai.ImGui.Design;
using System.Windows.Forms;

namespace OpenEphys.Miniscope.Design.Gui;

sealed class ImGuiMiniscopeControl : ImGuiControl
{
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
}
