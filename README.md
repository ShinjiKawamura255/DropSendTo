# DropSendTo

A lightweight, always-on-top Windows launcher built with .NET 8. Drag and drop files/folders onto a 2x2 grid (with 4 layers → 16 slots) to launch registered apps/shortcuts, passing dropped paths as arguments. Behaves like SendTo; also accepts target paths via command-line.

## Features
- 2x2 slots × 4 layers (16 registrations)
- Drag-and-drop to launch with arguments
- Always-on-top, black-themed, semi-transparent window (text remains readable)
- Right-click context menu for register/edit/remove/exit
- Config stored under `%AppData%/DropSendTo`

## Requirements
- Windows 10 22H2+ / 11
- .NET SDK 8.x installed on Windows (not WSL)

## Build & Run
- Build: `dotnet build`
- Run (UI): `dotnet run --project src/DropSendTo`
- Run (CLI launch): `src/DropSendTo/bin/Debug/net8.0-windows/DropSendTo.exe "C:\\path\\to\\file.txt"`
- Test: `dotnet test`

## SendTo Integration (optional)
Create a shortcut to the built EXE and place it in `%AppData%\Microsoft\Windows\SendTo`. Then you can right-click a file → Send to → DropSendTo to forward paths as arguments.

## Repo Docs
See `docs/REQUIREMENTS.md`, `docs/SPEC.md`, `docs/DESIGN.md`, and `docs/TESTPLAN.md` for details.

## Contributing
Read `AGENTS.md` for structure, coding style, testing, and PR conventions.
