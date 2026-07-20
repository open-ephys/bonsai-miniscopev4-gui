using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenEphys.MiniscopeV4.Gui;

internal class FileDialogHelpers
{
    public static Task<string> RunDialogTask(Func<CommonDialog> createDialog, Func<CommonDialog, string> assignResult)
    {
        return Task.Run(() =>
        {
            string result = string.Empty;
            Thread t = new(() =>
            {
                using var icon = AppResources.LoadIcon();
                using var owner = new Form
                {
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new System.Drawing.Point(-32000, -32000),
                    Size = new System.Drawing.Size(1, 1),
                    TopMost = true,
                    Icon = icon,
                    ShowIcon = true
                };
                owner.Show();

                using var dlg = createDialog();
                if (dlg.ShowDialog(owner) == DialogResult.OK)
                    result = assignResult(dlg);

                owner.Close();
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            return result;
        });
    }

    public static string GetDirectory(string path) => string.IsNullOrEmpty(path)
        ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        : Path.GetDirectoryName(Path.GetFullPath(path));
}
