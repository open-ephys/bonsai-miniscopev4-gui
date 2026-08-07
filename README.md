# OpenEphys.MiniscopeV4.Gui

[![Build](https://github.com/open-ephys/bonsai-miniscope-gui/actions/workflows/build.yml/badge.svg)](https://github.com/open-ephys/bonsai-miniscope-gui/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/OpenEphys.MiniscopeV4.Gui.svg)](https://www.nuget.org/packages/OpenEphys.MiniscopeV4.Gui)

A [Bonsai](https://bonsai-rx.org) package that provides a self-contained ImGui-based graphical interface for
configuring and acquiring data from a UCLA Miniscope V4 head-borne miniature microscope, including control of
an Open Ephys commutator.

It wraps the acquisition node from
[`OpenEphys.Miniscope`](https://www.nuget.org/packages/OpenEphys.Miniscope) in a single-window GUI (settings
panel, live video/data display, file recording controls, and a status/console log).

Full hardware and experiment documentation: <https://open-ephys.org/miniscope-docs>.

## Installing

### As a standalone application

Download the latest `MiniscopeGui-Setup-*.exe` from the [Releases](../../releases) page and run it. The
installer:

- Installs to your user profile (no admin rights required).
- Downloads all dependencies (Bonsai and required packages) so no separate Bonsai install is needed.
- Adds Start Menu / Desktop shortcuts to launch the GUI.

### As a Bonsai package

Add the [`OpenEphys.MiniscopeV4.Gui`](https://www.nuget.org/packages/OpenEphys.MiniscopeV4.Gui) package
through Bonsai's package manager, then drop the `MiniscopeGui.bonsai` workflow into the editor.

## Developing

Prerequisites: Visual Studio 2026, and a Windows machine (the GUI is WinForms + ImGui, `net472`, `x64`).

1. Clone the repository and open `OpenEphys.MiniscopeV4.Gui.sln`.
2. Double-click `.bonsai\Setup.cmd` once. This downloads a copy of Bonsai into `.bonsai\` and restores
   the packages listed in `.bonsai\Bonsai.config`.
3. Build the solution; this automatically generates a
   `OpenEphys.MiniscopeV4.Gui\Configuration\Settings.cs` file containing generated classes based on
   the `OpenEphys.MiniscopeV4.Gui\Configuration\miniscope-config.schema.json` schema.
4. The main GUI workflow lives at `OpenEphys.MiniscopeV4.Gui\Workflows\MiniscopeGui.bonsai`.

## CI/CD

Three GitHub Actions workflows run on every pull request to `main`, plus on published releases:

- **`build.yml`** — calls Open Ephys' shared
  [`build_dotnet_publish_nuget.yml`](https://github.com/open-ephys/github-actions) workflow. On PRs, it just
  confirms the project builds (debug + release, Windows + Linux) and checks the version was bumped
  compared to the last published release; on a published release, it packs and pushes the NuGet package.
- **`validate-workflow.yml`** — builds the project, provisions a headless Bonsai environment, and runs
  `Bonsai.exe MiniscopeGui.bonsai --export-image` against the freshly built plugin. If the workflow references
  a type that no longer exists or fails to compile, this exits non-zero and fails the check (Bonsai rejects
  export for workflows containing unknown types as of
  [bonsai-rx/bonsai#2267](https://github.com/bonsai-rx/bonsai/pull/2267)). The rendered SVG is uploaded as a
  build artifact so reviewers can visually confirm the workflow.
- **`installer.yml`** — packs the NuGet package, builds the Inno Setup installer
  (`installer\MiniscopeGui.iss`), and, on a published release, attaches
  `MiniscopeGui-Setup-<version>.exe` to that release. On pull requests it builds the installer as a smoke
  test and uploads it as an artifact, without publishing anything.

## License

[MIT](LICENSE)
