VISIONARY ROUTER - QUICK START
==============================

Nothing needs to be installed.

1. Extract the entire ZIP to a normal folder.
2. Double-click VisionaryRouter.exe.
3. Keep the console window open while using the router.
4. The Web UI opens automatically at http://localhost:8088.
5. Close the console window to stop Visionary Router.

The first Windows launch may show an "unknown publisher" warning because this build is not
code-signed. Use only a ZIP received from a source you trust.

PHONE OR TABLET ACCESS
----------------------

In the Web UI, open Diagnostics > Network Access. Select only the intended network adapter,
set an administrator password, save, and restart Visionary Router. Sign in with username
"admin". Windows Firewall must allow inbound TCP port 8088 on that adapter.

The Web UI port can be changed in the same panel. If that port is already occupied at
startup, Visionary Router automatically selects and saves the next available port. Read the
"Web UI" line in the console window to see the active localhost address. Firewall rules must
match the selected port.

Basic authentication uses plain HTTP. Enable network access only on a trusted LAN.

DATA
----

Device names, addresses, groups, presets, and access settings are normally stored in:

    C:\ProgramData\VisionaryRouter\router.db

Replacing the application folder does not erase that database.
