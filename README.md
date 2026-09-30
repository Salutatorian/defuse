# Defuse

Paste a link, save it, and play it inside the app. Progress stays on this device.

Infuse is a usability reference only: the name, graphics, layout, and branding here are not Infuse's.

![Defuse home, with Continue and Library](docs/home.png)

## Download

<table>
  <tr>
    <th align="left">OS</th>
    <th align="left">Download</th>
  </tr>
  <tr>
    <td>Linux</td>
    <td>
      <a href="https://github.com/Salutatorian/defuse/releases/latest/download/Defuse-linux-arm64.tar.gz"><img alt="Portable arm64" src="https://img.shields.io/badge/Portable-arm64-C47A3A?style=for-the-badge"></a>
      <a href="https://github.com/Salutatorian/defuse/releases/latest/download/Defuse-linux-x64.tar.gz"><img alt="Portable x64" src="https://img.shields.io/badge/Portable-x64-C47A3A?style=for-the-badge"></a>
      <a href="https://github.com/Salutatorian/defuse/releases/latest/download/Defuse-linux-x64.flatpak"><img alt="Flatpak x64" src="https://img.shields.io/badge/Flatpak-x64-3B82F6?style=for-the-badge"></a>
    </td>
  </tr>
  <tr>
    <td>Windows</td>
    <td>
      <a href="https://github.com/Salutatorian/defuse/releases/latest/download/Defuse-windows-x64-portable.zip"><img alt="Portable x64" src="https://img.shields.io/badge/Portable-x64-3A3A3A?style=for-the-badge&logo=windows&logoColor=white"></a>
      <a href="https://github.com/Salutatorian/defuse/releases/latest/download/Defuse-windows-x64-setup.exe"><img alt="Installer x64" src="https://img.shields.io/badge/Installer-x64-3B82F6?style=for-the-badge&logo=windows&logoColor=white"></a>
    </td>
  </tr>
  <tr>
    <td>macOS</td>
    <td>
      Sequoia or later
      <a href="https://github.com/Salutatorian/defuse/releases/latest/download/Defuse-macos-arm64.dmg"><img alt="DMG arm64" src="https://img.shields.io/badge/DMG-arm64-E85A9B?style=for-the-badge"></a>
    </td>
  </tr>
  <tr>
    <td>Android</td>
    <td>
      <a href="https://github.com/Salutatorian/defuse/releases/latest/download/Defuse-android.apk"><img alt="APK arm64" src="https://img.shields.io/badge/APK-arm64-3DDC84?style=for-the-badge&logo=android&logoColor=white"></a>
    </td>
  </tr>
</table>

Linux portable builds need the VLC package from your distribution (`vlc` on Debian, Ubuntu, and Fedora). Unzip or untar, then run `Defuse`. Install the Flatpak with `flatpak install Defuse-linux-x64.flatpak`. On macOS, open the disk image and run Defuse; if Gatekeeper blocks it, open it once from System Settings.

The Windows app is the WPF player in `src/Desktop`. Linux and macOS share `src/App`. Android is `src/Android`.

No license file is included until a license is chosen.
