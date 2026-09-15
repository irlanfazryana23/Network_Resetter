<img src="networks-logo.png" width="64" alt="Network Resetter logo">

# Network Resetter

A simple Windows network repair app that can help when your PC cannot get an automatic IP address (DHCP), has DNS lookup problems, or loses connectivity because of local network settings.

**1.0 — Initial release · Windows 10/11 · MIT license**

## Get started

Download the portable ZIP from this repository's Releases page when available, or build it from source.

1. Extract **NetworkResetter-1.0-portable.zip**.
2. Open **NetworkResetter-1.0.exe** and allow administrator access.
3. Select **Check**, then choose your network adapter.
4. Use **Quick repair** and confirm.
5. Select **Check again** and try opening a website.

The app requires **.NET Framework 4.8** and administrator access. The portable app needs no installation. The EXE is unsigned.

## When this app can help

Network Resetter can help with common Windows DHCP and DNS problems when the cause is on your PC, including:

- Windows cannot obtain an automatic IP address.
- DHCP server not found or unreachable messages.
- Wi-Fi or Ethernet reports an invalid IP configuration.
- Websites fail to open because of stale DNS cache entries.

Start with **Quick repair** and leave **Renew automatic IP (DHCP)** enabled for the affected adapter. This clears DNS cache and asks the DHCP server to renew the adapter's IPv4 configuration. Renewal is available only for connected adapters already using automatic IP settings. [Microsoft: ipconfig](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/ipconfig)

**A DHCP error does not always mean the problem is on your PC.** The app cannot fix an offline DHCP server, disconnected cable, router fault, or provider outage. If renewal still fails, check the connection and router, or contact your network administrator. Quick repair keeps manual IP settings unchanged. [Microsoft: DHCP client troubleshooting](https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/troubleshoot-problems-dhcp-client)

## Repair options

| Option | What it does | When to use it |
| --- | --- | --- |
| Quick repair | Clears DNS cache and optionally renews the selected adapter's IPv4 DHCP lease | Start here for DNS or automatic IP problems. |
| Winsock reset | Resets Windows network sockets | Try if Quick repair does not help. Requires a restart. |
| Full adapter reset | Removes network devices so Windows can detect them again | Last resort. Requires a restart; VPNs and virtual networks may need setup again. |

**Quick repair** keeps manual IP/DNS settings and does not release your current IP address before renewal. Uncheck **Renew automatic IP (DHCP)** to clear DNS cache only.

Winsock reset and Full adapter reset are under **Advanced repairs** and affect all connections. Save online work and run repairs locally, since remote access may disconnect. Restarting requires a separate confirmation.

## Connection checks

**Check** reads adapter, IP, gateway, and DNS settings, then asks Windows to resolve www.microsoft.com. It does not change settings.

A successful lookup may use cached results or another connection/VPN; it does not prove full internet access or confirm that the DHCP server is reachable.

## Logs

Select **Open logs** after starting a repair. Session files are saved under the account running the app:

~~~text
%LOCALAPPDATA%\NetworkResetter\Sessions\<timestamp-id>
~~~

Each session contains:

- **activity.log** — commands, output, errors, and exit codes.
- **ipconfig-before.txt** — IP and adapter settings before repair.
- **interface-before.txt** — interface configuration before repair.
- **README.txt** — notes about the saved records.

These records are a reference, not a full backup or automatic undo. Windows command output may appear in your Windows language. Remove private details before sharing logs in a public issue.

## Build from source

Use Windows PowerShell 5.1 on Windows with .NET Framework 4.8:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify.ps1
~~~

The build uses the installed .NET Framework C# compiler and WPF assemblies. No NuGet packages or separate SDK are needed.

~~~text
dist/
  NetworkResetter-1.0.exe
  NetworkResetter-1.0-portable.zip
  SHA256SUMS.txt
~~~

The ZIP includes the EXE, README, changelog, MIT license, logo, and EXE checksum. The standalone checksum file covers both the EXE and ZIP. Generated files are excluded from Git.

After building, open **open_gui.bat** or the EXE in dist/. The launcher only opens the app; choose a repair inside the window.

## Development

| Location | Purpose |
| --- | --- |
| src/ | C# application, WPF interface, and manifest |
| assets/ and networks-logo.png | App icon and logo |
| VERSION | Release version |
| build.ps1 | Compile and package |
| open_gui.bat | Open the built app |
| scripts/verify.ps1 | Static source and package checks |
| .github/ | Windows build workflow and contribution templates |
| docs/RELEASING.md | First push and release instructions |

See CONTRIBUTING.md for development guidance and docs/RELEASING.md for publishing steps.

**Validation:** the project owner tested the simplified 1.0 app in a Windows VM and reported successful operation. Build and static checks also passed. Automated checks compile and inspect files; they do not launch the app, run network checks or repairs, or restart Windows.

## References

- [Microsoft: ipconfig](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/ipconfig)
- [Microsoft: Winsock reset](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-winsock)
- [Microsoft: netcfg](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netcfg)
- [Microsoft: Wi-Fi troubleshooting](https://support.microsoft.com/en-us/windows/experience/connectivity-networking/fix-wi-fi-connection-issues-in-windows)

## License

[MIT](LICENSE). Copyright 2026 Network Resetter contributors.
