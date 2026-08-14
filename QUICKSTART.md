# xUnitRevit — Quick Start

Get from a fresh clone to green tests in a few minutes. Targets the **Modern** stack (.NET 8, Revit 2025–2027).

## 0. One-time machine setup

**a. Restore the test model (git LFS).** `walls.rvt` is stored via LFS:
```bash
git lfs pull
```
Confirm it's a real file (~5 MB), not a ~130-byte pointer:
```bash
ls -l SampleLibrary/TestModels/walls.rvt
```

**b. Set up local code signing (skips Revit's "unsigned add-in" prompt).**
Revit blocks unsigned add-ins with a prompt the add-in can't dismiss itself. Create a local self-signed cert once (no admin, no purchase):
```powershell
./setup-signing.ps1
```
This trusts a `xUnitRevit Local Dev` cert in your CurrentUser store and writes `.signing-thumbprint` (gitignored). Every build/deploy then auto-signs. Skip this only if you don't run inside Revit.

## 1. Fastest feedback — console runner (no Revit)

Runs the non-Revit tests in ~1s. Revit-only tests are auto-skipped by trait.
```bash
dotnet build SampleLibrary.Modern/SampleLibrary.Modern.csproj -c Debug
dotnet run --project xUnitRevit.Headless.Console -- \
  SampleLibrary.Modern/bin/Debug/net8.0-windows/SampleLibrary.Modern.dll results.xml
```
Expected: `35 passed, 0 failed, 29 skipped`, **exit 0**.

- Add `--include-revit` to also run the Revit-tagged tests (they fail outside Revit — expected).
- Exit codes: `0` all passed · `1` test failures · `2` infrastructure failure (e.g. unwritable results path).

## 2. Full run inside Revit — headless (CI-style)

Builds, deploys (signed), launches Revit, runs everything, writes JUnit XML, then resets config to dormant:
```powershell
./run-revit-tests.ps1 -RevitVersion 2026
./run-revit-tests.ps1 -RevitVersion 2026 -Timeout 600   # slower machines
./run-revit-tests.ps1 -SkipBuild                        # reuse deployed DLLs
```
Expected: `64 total, 63 passed, 0 failed, 1 skipped`, **exit 0**. Results in `RevitTestResults.xml` + `.log`.

## 3. Interactive — normal Revit + on-demand runner

The add-in has **two modes** (controlled by the deployed `config.json`):

| Mode | `headless` | `autoStart` | Behavior |
|------|-----------|-------------|----------|
| **Normal + on-demand** (default after any run) | `false` | `false` | Revit launches normally; run tests via **Add-Ins ▸ External Tools ▸ xUnitRevit** |
| **Auto UI** | `false` | `true` | Test runner window opens on Revit startup |
| **Headless** | `true` | `true` | Runs tests on startup, writes XML (used by `run-revit-tests.ps1`) |

`run-revit-tests.ps1` sets headless for its run, then **resets to dormant**, so your next manual Revit launch is normal and doesn't hijack other add-ins.

In the runner window: **Run All** / **Run Selected**, colored pass/fail/skip badges, and **📋 Copy Report** to copy a text summary (counts + failure messages) to the clipboard.

## 4. VS Code

The extension wraps `run-revit-tests.ps1` (local or SSH/remote). Trigger its run command; output streams to the *xUnitRevit* output channel. See [xunitrevit-vscode/](xunitrevit-vscode/).

> Visual Studio Test Explorer / `dotnet test` is **not** wired to run in Revit yet — the `xUnitRevit.TestAdapter` exists but isn't referenced, so `dotnet test` runs locally and Revit tests fail. Use the console runner or the headless script instead.

## Speeding up runs — per-version models

Opening the shared `walls.rvt` (older format) makes Revit upgrade it in-memory (~28 s) on **every** run. To skip that, save a native copy per Revit version:

1. Open `SampleLibrary/TestModels/walls.rvt` in Revit 2026.
2. **Save As** → `SampleLibrary/TestModels/walls_2026.rvt`.

`TestModelLocator` automatically prefers `walls_<version>.rvt` and falls back to `walls.rvt`. Native-format opens are near-instant, and older Revit versions still work via the fallback.

## Writing tests

- All Revit API calls from tests go through `xru.DispatchToMainThread()` (or `xru.OpenDoc`/`xru.RunInTransaction`, which dispatch internally).
- Revit document tests use `[Collection("Revit")]` + `WallsDocFixture` (opens the model once, runs sequentially).
- Tag Revit-runtime-dependent classes with `[Trait("Category", "Revit")]` so the console runner skips them by default.
- Resolve models with `TestModelLocator.GetTestModel("walls.rvt")`.

See [SampleLibrary.Modern/RevitTests.cs](SampleLibrary.Modern/RevitTests.cs) for full examples, and the [README](README.md) for detailed patterns.

## Troubleshooting

| Symptom | Fix |
|---|---|
| "Security – Unsigned Add-In" prompt | Run `./setup-signing.ps1`, then rebuild/redeploy |
| Revit stuck at "ready" / other add-ins missing / crash on toggle | Deployed `config.json` is stuck `headless:true`. Set `headless:false, autoStart:false`, or run `run-revit-tests.ps1` once (it resets to dormant) |
| Fixture error: model is a tiny/LFS pointer file | `git lfs pull`, then rebuild |
| Headless run slow / no results | Check free disk (Revit needs several GB for document upgrade); check `RevitTestResults.log` tail. A stuck main-thread call now fails with a logged `TimeoutException` after 120s rather than hanging indefinitely |
| `dotnet test` runs tests locally and Revit tests fail | Expected — VS adapter not wired. Use console runner or headless script |
