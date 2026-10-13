# WPF Visual Validation Demo

A small WPF app ("Insuracare Component Lab") plus an Applitools Eyes test that validates the
running app against a **Figma design** rather than against an earlier screenshot of itself.

```
MainWindow.xaml / App.xaml            The app: a control gallery (buttons, combos, inputs, toggles)
design/
  Insuracare Component Lab - ....fig  Original Figma source
  control-gallery.figma.png           Export of the Figma "Control gallery" frame (1184×781)
tests/WpfVisualValidationDemo.VisualTests/
  AppSession.cs                       Launches the exe, captures its client area with PrintWindow
  ComponentLabVisualTests.cs          The Eyes test + the baseline seeding test
  DotEnv.cs                           Loads .env into the process environment
.github/workflows/visual-tests.yml    CI: builds and runs the visual test on windows-latest
.mcp.json                             Applitools + Figma MCP servers for Claude Code
.env                                  Secrets (git-ignored) — see below
```

## Setup

Everything below was done to stand this repo up from source on a fresh Windows machine, in the
order it happened. Steps 1–3 get the app building and running; 4–6 add Applitools; 7 wires up CI.

### 1. Prerequisites

- Windows 10/11 at **100 % display scaling** — the capture is in device pixels, and the
  1184×781 baseline only matches at 100 %
- .NET SDK 8 or later (`dotnet --version`)
- Node.js 18 or later (`node --version`) — only for the Applitools MCP server, which runs via `npx`
- An Applitools account with an execution API key, plus read-only and write-only keys
  (Applitools Admin → API Keys)

### 2. Build and run the app

```
dotnet build WpfVisualValidationDemo.slnx
dotnet run --project WpfVisualValidationDemo.csproj
```

The window titled *Component Lab — UI Control Gallery* should appear.

### 3. Secrets

Create `.env` in the repo root (it is git-ignored):

```
APPLITOOLS_API_KEY=...       # execution key — used by the tests and the MCP server
APPLITOOLS_READ_KEY=...      # read-only key — MCP inspect/review tools
APPLITOOLS_WRITE_KEY=...     # write-only key — MCP resolve (accept/reject) tools
FIGMA_ACCESS_TOKEN=          # optional; enables Figma Dev Comparison (see "How it works", item 4)
```

`DotEnv.cs` loads this file for the tests; the Applitools MCP server finds it by walking up from
the working directory. Blank values are ignored.

### 4. Applitools MCP server (Claude Code)

`.mcp.json` in the repo root registers the server project-scoped, so nothing global is needed:

```json
"applitools-mcp": {
  "command": "cmd",
  "args": ["/c", "npx", "-y", "@applitools/mcp@latest"],
  "env": { "APPLITOOLS_API_KEY": "${APPLITOOLS_API_KEY}", ... }
}
```

The `cmd /c` wrapper is required on Windows for `npx`-based stdio servers. After creating `.env`,
**restart Claude Code**, approve the project-scoped server when prompted, and confirm with the
`eyes_verify_api_key` tool ("Eyes authentication successful"). The equivalent CLI command is
`claude mcp add applitools-mcp npx @applitools/mcp@latest`.

### 5. The visual test project

`tests/WpfVisualValidationDemo.VisualTests` is an NUnit project using the
[Eyes.Images](https://www.nuget.org/packages/Eyes.Images) .NET SDK (3.68.x). The Applitools
MCP server's capture tools are Playwright-only, so for a desktop app the test does the capture
itself:

- `AppSession.cs` starts the built `WpfVisualValidationDemo.exe` (a `ProjectReference` copies it
  next to the test assembly), waits for the main window, sets per-monitor DPI awareness, and
  renders the **client area** with `PrintWindow(PW_CLIENTONLY | PW_RENDERFULLCONTENT)` — no
  title bar, so window-activation state never shows up as a diff.
- It drives the app through UI Automation (`InvokePattern`) and waits for the resulting text
  to appear before capturing (UIA `Invoke` returns before WPF has processed the click, so a
  fixed sleep is a race).
- `ComponentLabVisualTests.cs` opens Eyes with the capture size as the viewport and sends two
  checkpoints: *Initial state* and *After clicking Save changes*. It fails with the Eyes URL if
  any step mismatches.

The root `WpfVisualValidationDemo.csproj` excludes `tests/**` from its default source glob;
without that, the SDK compiles the test sources into the app.

### 6. Seed the baseline and run the test

Seed the Eyes baseline from the Figma export (the test is `[Explicit]`, so it only runs when
selected):

```
dotnet test tests/WpfVisualValidationDemo.VisualTests --filter Name~SeedBaselineFromFigmaDesign
```

On first run the Figma image becomes the baseline automatically. If a baseline already exists in
the Figma environment (re-seeding after a design change), the test fails with a link: open it and
**Accept** the run in the Eyes dashboard.

Then run the visual test:

```
dotnet test tests/WpfVisualValidationDemo.VisualTests
```

Or just the Figma-comparison test on its own:

```
dotnet test tests/WpfVisualValidationDemo.VisualTests --filter Name~ControlGallery_MatchesBaseline
```

The output contains the Eyes results URL. Expect mismatches on the first run — mostly text
rendering (Inter vs Segoe UI) plus any real layout deviations from the design. Review them in
the dashboard (or with the MCP server's `eyes_inspect_*` / `eyes_resolve_*` tools from Claude
Code); accepting a step makes that implementation the baseline from then on.

### 7. GitHub Actions

`.github/workflows/visual-tests.yml` runs the test suite on `windows-latest` (which has the
interactive desktop session WPF needs, at 1920×1080 / 100 %). It builds in Release, runs
`dotnet test` with a TRX logger, and uploads `TestResults/` as an artifact even on failure.

It runs automatically on every push to `master`, and can also be run manually from the Actions
tab (Actions → Visual tests → Run workflow) or with the GitHub CLI:

```
gh workflow run visual-tests.yml
```

Repository secrets:

| Secret | Required | Purpose |
|---|---|---|
| `APPLITOOLS_API_KEY` | yes | execution key |
| `FIGMA_ACCESS_TOKEN` | no | enables Figma Dev Comparison in CI |

The workflow sets `APPLITOOLS_BATCH_ID` (one batch per run), `APPLITOOLS_BATCH_NAME`, and
`APPLITOOLS_BRANCH` / `APPLITOOLS_PARENT_BRANCH` so PR branches compare against the `master`
baseline.

## How it works

1. **Design in Figma.** The Control gallery screen lives in Figma:
   <https://www.figma.com/design/ezwnpJMHvW5YqQNyr5vGRJ/Insuracare-Component-Lab?node-id=3-2>.
   The frame is exactly **1184×781** — the app's client area (its 1200×820 window minus
   Windows chrome) at 100 % DPI — so Eyes compares like for like. It was generated from the
   XAML with the Figma MCP server; Segoe UI is not available in Figma, so the design uses Inter.

2. **Seed the Eyes baseline from the design.** `SeedBaselineFromFigmaDesign` checks
   `design/control-gallery.figma.png` into Eyes under the same app name, test name and
   viewport as the real test. Baselines live in a dedicated environment
   (`HostOS = HostApp = "Figma"`, the convention the Applitools Figma plugin uses), so they
   never collide with baselines captured from the app itself.

3. **Test the app against it.** `ControlGallery_MatchesBaseline` launches the exe, captures
   the client area and sends it to Eyes. Differences between implementation and design show
   up in the Eyes dashboard; accept the intended ones and the test goes green.

4. **Optional — let Eyes pull the design itself.** If `FIGMA_ACCESS_TOKEN` is set, the test
   also enables Applitools *Figma Dev Comparison* (`Eyes.FigmaBaselines` / `FigmaOptions`),
   where the SDK fetches the frame from Figma at run time. This needs a Figma personal
   access token with the `file_content:read` scope; the MCP connector's OAuth session cannot
   be used for it. Modes: `figma-baseline` (default — the design is always the baseline, so
   design edits fail the next run), `auto-baseline` (design until accepted, then the accepted
   capture), `test-baseline`, `disabled`; override with `APPLITOOLS_FIGMA_MODE`. The fetched
   frame is cached for 7 days under `%TEMP%pplitools-figma-cache` (`APPLITOOLS_FIGMA_TEMP_PATH`)
   because Figma's render quota is small; delete it to force a refetch after a design change.

## Updating the design

1. Edit the frame in Figma (keep it 1184×781, or change the window size and the frame together).
2. Export it: select the frame → Export → PNG at 1× → save over `design/control-gallery.figma.png`
   (or ask Claude Code to do it via the Figma MCP server).
3. Re-run the seed command from Setup step 6 and accept the re-seed in the dashboard.

## MCP servers (Claude Code)

`.mcp.json` registers two servers for Claude Code sessions in this repo:

| Server | Purpose | Auth |
|---|---|---|
| `applitools-mcp` | Inspect Eyes runs, view diffs, accept/reject steps | `APPLITOOLS_*` keys from `.env` |
| `figma` (`https://mcp.figma.com/mcp`) | Read/generate designs, export frames | OAuth — run `/mcp` once to sign in |

Accepting steps through the MCP server needs a key with write permission; a `403` from
`eyes_resolve_checkpoint` means `APPLITOOLS_WRITE_KEY` is missing that.

## References

- [Applitools — Figma Dev Comparison](https://applitools.com/docs/eyes/integrations/design-validation/figma-dev-compare)
- [Applitools — Eyes Figma plugin](https://applitools.com/docs/eyes/integrations/design-validation/figma-plugin)
- [Eyes.Images NuGet package](https://www.nuget.org/packages/Eyes.Images) (3.68.7 — includes `FigmaBaselines`/`FigmaOptions`)
