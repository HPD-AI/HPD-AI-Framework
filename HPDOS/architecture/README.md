# HPDOS Architecture Notes

This folder is the reference shelf for architecture work that we may restart,
replace, or rebuild cleanly. These notes are not compatibility contracts. They
capture the thinking and constraints we learned from the current prototype.

## Documents

- `workspace-tabs.md` - Main workspace tab model. This is the strongest current
  direction: apps, artifacts, files, terminals, and other surfaces should live as
  first-class tabs in the main workspace area.
- `terminal-pty.md` - Terminal backend and PTY notes, with opencode references.
  The sidebar display idea is mostly superseded, but the backend lessons are
  still useful.
- `app-runtime-lima.md` - Lima app runtime lifecycle and app contract notes for
  running web, compose, remote GUI, and static app surfaces.

## Working Rule

When the code starts feeling patched together, use these notes to restart from a
small architecture instead of layering more special cases onto the prototype.
