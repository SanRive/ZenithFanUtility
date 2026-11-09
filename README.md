# Zenith Fan Utility

<p align="center">
  <img src="https://github.com/user-attachments/assets/2b680b93-4c25-46db-a384-e90ab6d851ff" alt="Zenith Fan Utility Screenshot" width="450px" />
</p>

A modern, reliable, and feature-rich fan control utility for ASUS laptops, rebuilt from the ground up for stability, performance, and a polished user experience.

---
## Features

Zenith Fan Utility provides a comprehensive toolset to take full control of your laptop's cooling system.

* **Custom Fan Curves:** Create and switch between two distinct fan curve profiles for automated, temperature-based fan control.
* **Interactive Curve Editor:** A smooth, intuitive editor to add, remove, and drag points to define your perfect fan curve.
* **Manual Control:** Instantly take over with a simple slider, or use the "Max Speed" and "Fans Off" buttons for quick actions.
* **Live Monitoring:** Real-time display of CPU & GPU temperatures and fan RPMs, right on the main window.
* **Hysteresis Control:** Prevents annoying fan "flapping" by adding a configurable temperature buffer, ensuring fan speeds change smoothly.
* **Configurable Refresh Rate:** Adjust the stats refresh interval to your liking, from rapid updates to power-saving intervals.
* **Polished Dark UI:** A beautiful, custom-built dark theme that is **DPI-aware**, ensuring it looks sharp and crisp on modern high-resolution displays.
* **Persistent Tray Icon:** A G-Helper-style tray icon that provides live stats on hover and toggles the main window's visibility with a single click.
* **Startup Ready:** Configure the app to start automatically with Windows, minimized to the tray.

---
## Getting Started

1.  Go to the [**Releases**](https://github.com/realMoai/ZenithFanUtility/releases) page.
2.  Download the `ZenithFanUtility_vX.X.X.zip` file from the latest release's **Assets** section.
3.  Run `ZenithFanUtility.exe`. It is a single file: nothing else needs to sit next to it.

> [!IMPORTANT]
> Windows asks for administrator permission (UAC) when you open the app. Zenith Fan Utility then starts itself with **SYSTEM** privileges, which newer ASUS drivers require for fan control. It does this through a scheduled task named `ZenithFanUtility`, so no extra tools are needed.

---
## Prerequisites & Troubleshooting

### ASUS System Control Interface

> [!NOTE]
> The latest versions of **Asus System Control Interface (v3.1.41.0+)** only accept fan commands from processes running as SYSTEM. Zenith Fan Utility handles this automatically (see above), so PsExec is no longer needed.

**If fan control still doesn't work, roll back the driver:**
1.  [Uninstall the latest Asus System Control Interface](https://github.com/seerge/g-helper/wiki/Troubleshooting#reinstalling-asus-system-control-interface).
2.  Install a previous version, like [**v3.1.40.0**](https://dlcdnets.asus.com/pub/ASUS/GamingNB/Image/Software/SoftwareandUtility/16402/ASUSSystemControlInterfacev3_ASUS_Z_V3.1.40.0_16402.exe).
3.  If Windows Update overrides it, go to `Device Manager -> System Devices -> Asus System Control Interface`, then select `Driver -> Roll Back Driver`.

### Command-line options
* `-tray` starts minimized to the tray.
* `-nosystem` skips the SYSTEM hand-off and runs the app as the current user, as older versions did. Fan control then needs an older driver (see above) and running as administrator.

### Settings location
Settings are stored per account and per exe location. Because the app normally runs as SYSTEM, they live under `C:\Windows\System32\config\systemprofile\AppData\Local\ZenithFanUtility`, and moving the exe to another folder starts with default settings.

### Uninstalling
Exit the app from its tray icon, then remove its scheduled task from an administrator terminal with `schtasks /Delete /TN ZenithFanUtility /F` and delete the exe.

---
## How to Use

* **Master Control:** The "Enable Custom Fan Control" checkbox is the master switch. When checked, the app takes over your system's default fan behavior.
* **Mode Selection:** Use the radio buttons to switch between **Manual Mode** (slider is active), **Fan Curve 1**, or **Fan Curve 2**. When a fan curve is active, the manual slider is disabled.
* **Tray Icon:** The app lives in your system tray. A single left-click on the icon will show or hide the main window. Right-clicking provides options to show or exit the application completely. Opening the exe again while it is running also brings the window up.
* **Start with Windows:** Starts the app at logon, after sleep or hibernation, and again within 5 minutes if it is ever closed. Your fan settings are re-applied after waking from sleep or Modern Standby, and every 30 seconds in case the laptop's controller takes fan control back.

---
## Acknowledgments

This project was built to be a modern, stable fan control utility. It would not have been possible without the foundational work of several open-source developers:

* **[Karmel0x](https://github.com/Karmel0x/AsusFanControl):** Created the original proof-of-concept and the core library for hardware interaction.
* **[Pgian88](https://github.com/pgain88/AsusFanControlRemastered):** Provided a foundational update with many new features in a prior version.
* **[Darren80](https://github.com/Darren80/AsusFanControlEnhanced):** Developed the original visual fan curve editor concept.
* **[BrightenedDay](https://github.com/BrightenedDay/AsusFanControl):** Implemented the initial theming features.

---
## License

Distributed under the MIT License.
