VISIONARY ROUTER - WINDOWS INSTALLER
====================================

Run VisionaryRouter-Setup-win-x64.exe and follow the prompts. The installer:

- installs the self-contained application (no separate .NET runtime required),
- runs Visionary Router automatically as a Windows Service,
- asks which Web UI port to use,
- can enable password-protected phone and tablet access,
- can create a Windows Firewall rule limited to Private networks and the local subnet,
- adds Start Menu and optional desktop shortcuts, and
- opens the local Web UI when setup finishes.

The firewall rule only permits inbound TCP to VisionaryRouter.exe, which listens only on the
Web UI port. Visionary endpoint control uses outbound UDP and normally needs no inbound
firewall exception.

Network access uses plain HTTP Basic authentication. Enable it only on a trusted AV/control
LAN. The password file is restricted to Windows SYSTEM and local administrators.

UNINSTALLING
------------

Windows Settings > Apps > Installed apps > Visionary Router removes the Windows Service,
application files, shortcuts, and firewall rule. The uninstaller asks whether to remove the
saved database. Choose No if you want a later reinstall to retain devices and presets.

Saved data is stored in:

    C:\ProgramData\VisionaryRouter

