using Bonsai.ImGui.Design;
using System.Windows.Forms;

namespace OpenEphys.Miniscope.Design.Gui;

sealed class ImGuiMiniscopeControl : ImGuiControl
{
    // NB: Overrides ProcessDialogKey to suppress Escape so the Bonsai visualizer dialog
    // does not close when the user presses Escape inside the ImGui window.
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Escape)
            return true;
        return base.ProcessDialogKey(keyData);
    }
}
