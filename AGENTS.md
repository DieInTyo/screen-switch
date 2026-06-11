# Agent Notes

- Before running `dotnet publish .\ScreenSwitch.csproj -c Release`, check whether `ScreenSwitch.exe` is already running. If it is running from this workspace, stop that process first so the publish target can replace the root `ScreenSwitch.exe`; after a successful publish, start the fresh executable again if the app should remain running for manual testing.
- Whenever a response follows code, documentation, asset, config, or project-file changes, include suggested commit text. If the changes contain user-visible behavior, also include suggested release notes. These texts are separate from the assistant's own summary of work.
- Before writing suggested commit text, check the full current uncommitted state with `git status --short` and, when needed, `git diff`. The commit text must cover the entire uncommitted diff relative to the previous commit, including changes made in earlier turns that are still uncommitted. Do not write commit text only for the latest requested change unless it is the only uncommitted change.
- Commit text must describe the final diff, not the assistant's turn-by-turn activity. Do not describe temporary intermediate attempts that are no longer present in the final diff.
- Commit text format:
  - Use a short imperative subject line, for example `Add language selection to tray menu`.
  - Add a blank line.
  - Prefer short bullets when the diff contains multiple meaningful change groups. Each bullet should cover one user-facing or technical group, not a long mixed paragraph.
  - Mention internal/project changes when they are part of the final diff, such as version metadata, README updates, ignore rules, or build assets.
  - Example:
    ```text
    Add minimize-all and move-window hotkeys

    - Add a tray action and global hotkey for minimizing all visible movable windows.
    - Add a move-window hotkey that opens a picker with multi-select and double-click support.
    - Update hotkey settings, registration, and localized UI text for the new actions.
    - Document the project rules for commit messages and release notes in AGENTS.md.
    ```
- Release notes must include only real user-visible changes: new features, changed behavior, and bug fixes that users can notice. Do not include internal cleanup, implementation details, logging, build metadata, or intermediate fixes unless they affect the user experience.
- Release notes format:
  - Prefer grouped sections when useful: `Additions` and `Bug Fixes and improvements`.
  - Write each item as a user-facing bullet in past tense, for example `Added ...`, `Fixed ...`, or `Improved ...`.
  - Merge closely related changes into one bullet instead of listing implementation steps separately.
  - Omit a release-notes block when the final diff only changes internal metadata, repository hygiene, or agent instructions.
