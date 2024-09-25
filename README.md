# Auto Audio Switcher

<img src="https://github.com/user-attachments/assets/16924db8-b00a-4193-b0d9-2f6e2eccd684" width="309" align="right" />

Switches the default playback device based on the focused window's monitor.

For example, if you open a media player and Win+Shift+Left/Right it over to the TV, the playback device will automatically change to the TV. Alt+Tab back to the primary display, and the playback device switches back to the speakers.

## Installing

1. Install the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
2. Download and extract the [latest release](https://github.com/maxkagamine/AutoAudioSwitcher/releases/latest)
   - Not to Program Files; the app needs to be able to write to its install directory to save settings and log errors
3. Place a shortcut in Startup (`%appdata%\Microsoft\Windows\Start Menu\Programs\Startup`)

## Usage notes

- The playback device for each monitor can be set either by right-clicking the tray icon or by editing appsettings.json in the same folder as the exe (it will be created and populated with the connected monitors' names when you run the program; changes are picked up automatically, so there's no need to restart the app).

- If you have multiple speakers with the same name, you can rename them from the Sounds control panel (right click on the volume icon > Sounds > Playback, or Win+R "mmsys.cpl"). In the case of duplicate monitors, see [#6](https://github.com/maxkagamine/AutoAudioSwitcher/issues/6).

- The program only changes the default playback device when the current monitor _changes_, not every time you switch windows, which means if your main monitor is set to Speakers and you manually switch to Headphones, it won't switch back to Speakers on you as long as you stay on that monitor. If you need to temporarily disable auto-switching, left click on the tray icon to toggle it on and off.

- Report bugs and crashes by [creating an issue](https://github.com/maxkagamine/AutoAudioSwitcher/issues/new). Be sure to attach the error log, found in the same folder as the exe. (Changing "LogLevel" in appsettings.json to "Debug" may help nail down the problem.)

## Legal stuff

Copyright © Max Kagamine  
Licensed under the [Apache License, Version 2.0](LICENSE.txt)

## Illegal stuff

[Pirates!](https://www.youtube.com/watch?v=NSZhIAfR6dA)
