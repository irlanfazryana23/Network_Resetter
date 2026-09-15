# Publishing version 1.0

The first public release is **1.0 — Initial release**, with tag **v1.0**.

The project is prepared locally on branch main. A GitHub remote and first commit must be added before the first push.

## Build the release

From the project folder, run:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify.ps1
~~~

These commands compile and inspect files. They do not launch the app or perform network operations.

Release files:

| File | Purpose |
| --- | --- |
| dist/NetworkResetter-1.0-portable.zip | Portable download |
| dist/NetworkResetter-1.0.exe | Optional standalone download |
| dist/SHA256SUMS.txt | SHA-256 hashes of the EXE and ZIP |

The ZIP contains the EXE, README, changelog, MIT license, logo, and EXE checksum. Only upload the current 1.0 package; other local builds are not release assets.

The project owner tested the simplified 1.0 app in a Windows VM and reported successful operation. Build and static checks passed. If application behavior changes before publishing, validate the changed behavior in an isolated VM or another approved device.

## First push

Create an empty repository on GitHub, without a generated README, license, or gitignore. Replace the URL below with your repository URL.

Run these commands from the project folder:

~~~powershell
git add .
git diff --cached --stat
git diff --cached --check
git status
git commit -m "Initial release 1.0"
git remote add origin https://github.com/YOUR_USERNAME/network-resetter.git
git push -u origin main
~~~

Review the staged files before committing. The ignore rules exclude generated builds, local IDE/agent files, logs, and signing keys. Keep the MIT license and logo assets in the commit.

If you downloaded a source ZIP without Git metadata, initialize the repository with git init -b main before the commands above.

## Publish the release

1. Confirm the Windows build workflow passes on the pushed commit.
2. Open the repository's Releases page and create a new release.
3. Create tag **v1.0** on the tested commit and use title **1.0 — Initial release**.
4. Copy the 1.0 entry from CHANGELOG.md into the release description.
5. Attach the portable ZIP and standalone SHA256SUMS.txt. Optionally attach the EXE.
6. Publish the release.

The workflow uploads build artifacts only; it does not publish releases or tags automatically. GitHub Actions results become available after the repository is pushed.

The EXE is unsigned. Do not include private signing keys or certificates in the repository.

## Future releases

Update these together:

- VERSION.
- AssemblyVersion and AssemblyFileVersion in src/NetworkResetter.cs.
- Version shown in the app's session log.
- src/app.manifest.
- Versioned filename in open_gui.bat and documentation.
- CHANGELOG.md.

Then rebuild, verify, and test the affected runtime behavior. The build checks version consistency and derives output filenames from VERSION. Use a new version for changes after publishing 1.0.
