# Introduction

DesktopMirror is a WPF app to show the content of your desktop in a semi-transparent folder when a use-defined hotkey is pressed (defaults to Ctrl+Alt+D).

# Download

Compiled downloads are not available.

# Compiling

To clone and run this application, you'll need [Git](https://git-scm.com) and [.NET](https://dotnet.microsoft.com/) installed on your computer. From your command line:

```
# Clone this repository
$ git clone https://github.com/btigi/desktopmirror

# Go into the repository
$ cd src

# Build  the app
$ dotnet build
```

# Usage

A configuration file named config.json is automatically created in %HOMEDRIVE%%HOMEPATH%\AppData\Roaming\DesktopMirror is one does not exist. The configuration options are:

- `CloseOnEscape`: close the window if the ESC key is pressed
- `TargetMonitor`: the monitor id the window should be displayed on, an integer value or `null` for the current monitor
- `HideRegex`: a regular expression defining files to be hidden from the window
- `HideExtensions`: if file extensions should be hidden (Always), displayed (Never) or only hidden for certain extensions (ListedOnly)
- `HideExtensionsList`: a | separated list of file extensions to hide, if `HideExtensions` is set to `ListedOnly`
- `UseCtrl`: if the Ctrl key is used in the shortcut definition
- `UseAlt`: if the Alt key is used in the shortcut definition
- `UseShift`: if the Shift key is used in the shortcut definition
- `Hotkey`: the shortcut key, e.g. "D"


# Licencing

DesktopMirror is licenced under the MIT license. Full licence details are available in license.md