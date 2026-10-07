# Contributing

Keep the app simple. Describe the user problem and keep each change focused. Use short labels and put advanced repairs under Advanced repairs.

## Build and check

Use Windows PowerShell 5.1 on Windows with .NET Framework 4.8:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify.ps1
~~~

The build uses the installed .NET Framework compiler; no SDK or NuGet restore is needed. Keep source compatible with that compiler.

Verification checks XAML, control references, PowerShell syntax, launcher path, version metadata, embedded resources, and package hashes. It does not launch the app or run network checks, repairs, or restarts. GitHub Actions runs the same commands.

## Runtime testing

Do not run the app or network operations on the development device. Use an isolated Windows VM or another explicitly approved test device.

For relevant changes, check the affected repair mode, DHCP/manual-IP behavior, error reporting, cancellation, and restart handling. UI changes should also be checked for keyboard navigation and display scaling.

Record build/static results separately from runtime results. Include the Windows version, test environment, repair option, and outcome when available. Do not claim that compilation verifies runtime behavior.

The project owner reported a successful Windows VM test of version 1.0.

## Behavior to preserve

- Quick repair is the default.
- Manual IP/DNS settings are preserved by Quick repair.
- Repairs and restarts require confirmation.
- No network operations run at startup.
- Stop waits for the active command to finish.
- Command failures stay visible in the activity log.

Keep generated EXEs, ZIPs, local logs, and signing material out of commits. Source, icons, documentation, and build scripts belong in Git; release packages belong in GitHub Releases.

## Pull requests and issues

Use the repository's issue and pull-request templates. Explain what changed, why, and how it was checked. Share only relevant log excerpts and remove private details.

For version changes and publishing, see docs/RELEASING.md.

Contributions use the project's [MIT license](LICENSE).
