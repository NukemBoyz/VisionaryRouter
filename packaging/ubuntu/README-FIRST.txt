VISIONARY ROUTER FOR UBUNTU SERVER
==================================

Choose the package that matches the server:

  linux-arm64  Raspberry Pi running 64-bit Ubuntu Server
  linux-x64    Intel or AMD NUC/server

Install:

  tar -xzf VisionaryRouter-ubuntu-<architecture>.tar.gz
  cd VisionaryRouter-ubuntu-<architecture>
  sudo bash install.sh

The installer asks for the Web UI port and an administrator password, installs the app under
/opt/visionary-router, stores data under /var/lib/visionary-router, and starts a systemd
service. If UFW is active, it offers to open the selected TCP port.

The first run listens on every server interface so setup can be completed remotely. Sign in
with username "admin", then use Diagnostics > Network Access to choose specific interfaces.
Save and restart the service:

  sudo systemctl restart visionary-router

Useful commands:

  sudo systemctl status visionary-router
  sudo journalctl -u visionary-router -f
  sudo systemctl restart visionary-router
  sudo visionary-router-uninstall

Re-running install.sh upgrades the application while preserving the database and existing
administrator password. The --purge uninstall option permanently removes saved data.
