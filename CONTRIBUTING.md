# Contributing

Use Windows and the .NET 10 SDK version pinned in `global.json`. Open `TP7-Wav-Editor.slnx` in an IDE or work from the command line. All UI text, documentation, and code comments should be in English.

## Before submitting a change

```powershell
dotnet restore --locked-mode
dotnet format --no-restore
dotnet build -c Release --no-restore
.\scripts\test.ps1 -NoBuild -NoRestore
```

Keep changes focused and describe the user-visible behavior and validation in the pull request. New parsing, export, sample-conversion, and editing behavior should have a regression test using synthetic data. Tests generate isolated temporary files and do not require a recorder or audio device.

For UI changes, run the app and check opening a file, editing a cue and BPM, undo/redo, playback, and export/reopen. Check the minimum window size and keyboard navigation. A successful automated test run does not replace a listening test or validation on a physical TP-7.

## Conventions

- Follow `.editorconfig`; nullable references and compiler warnings are enforced during builds.
- Keep file-format behavior in `TP7.Core`, playback in `TP7.Audio`, and WPF concerns in `TP7.Windows`.
- Preserve unknown WAV chunks and original audio bytes unless the user requests normalization.
- Keep private recordings, generated files, personal IDE settings, and credentials out of the repository.
- Update documentation when behavior or supported formats change.

To update a NuGet dependency, change its project reference, run `dotnet restore --force-evaluate`, review the lock-file changes, and rerun validation. Commit the updated `packages.lock.json` files with the project files.

Use `dotnet build` for compilation and `scripts/publish.ps1` for a complete release build. The publish script restores dependencies, runs tests, and produces distributable ZIP files. After the validation steps above, use `scripts/publish.ps1 -SkipTests -NoRestore` to avoid repeating them. `scripts/package-source.ps1` creates only the source ZIP and also works outside a Git checkout.

Generated packages belong in GitHub release assets, not Git history. Before a release, review the licenses, version in `Directory.Build.props`, and UI version label. Contributions are provided under the project's MIT License.
