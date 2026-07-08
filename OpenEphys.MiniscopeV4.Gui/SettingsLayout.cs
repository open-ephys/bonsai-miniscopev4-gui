namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Shared layout state for the settings sidebar. <see cref="SettingsPanel"/> and <see cref="FilePanel"/>
/// render into a single child window (SettingsPanel opens it, FilePanel closes it), so they coordinate
/// through here rather than through either class directly: SettingsPanel reports whether the sidebar is
/// open (so FilePanel knows whether to render its own content into the shared child or leave it collapsed),
/// and FilePanel reports how tall its own content was last frame (so SettingsPanel can reserve room for it,
/// one frame stale since the height is otherwise unknown until FilePanel renders).
/// </summary>
static class SettingsLayout
{
    /// <summary>
    /// Gets or sets whether the sidebar is currently expanded (showing its full content) rather than
    /// collapsed to icon width.
    /// </summary>
    public static bool SidebarOpen { get; set; }

    /// <summary>
    /// Gets or sets the height, in pixels, that the Recording section's content occupied last frame.
    /// </summary>
    public static float RecordingSectionHeight { get; set; }
}
