Windows System Monitor 1.0.0

Run WindowsSystemMonitor.exe on 64-bit Windows 10 22H2 or Windows 11.
The portable executable includes .NET. No development tools or separately
installed .NET runtime are required. Its first launch extracts bundled native
runtime files into the current user's temporary .net directory. These files
are inert when the application is closed and may be recreated on next launch.

Monitoring runs as a normal user. Maintenance requires explicit action;
individual Windows operations may request UAC elevation. Windows tools opened
from the application can continue independently after it closes.

Close the main window or press Alt+F4 to exit. There is no tray application,
startup task, scheduled task, or service. For the portable version, remove
the executable to uninstall. For the installed version, use Settings > Apps.
The installer uses current-user permissions and creates desktop/Start Menu
shortcuts. Close the monitor before installing or uninstalling. Uninstallation
removes only application-owned files; unrelated files in the folder are kept.

Builds are unsigned unless a distributor signs them. Windows may show an
unknown-publisher or SmartScreen warning. Check the publisher/source and
release checksum; do not disable Windows security protections.
