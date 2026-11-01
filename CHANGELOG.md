# Changelog

All notable changes to this package will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-11-01

### Added

- `Tools/SakuraWine/MA Shape Sync Auto` window (UI Toolkit): pick the outfit,
  review/override the auto-detected body, preview per-mesh changes, then run
- Auto-detect the body mesh by counting blendshapes shared with the outfit
- Add MA Blendshape Sync to every outfit mesh that shares blendshapes with the body
  (reuses existing components, skips already-registered shapes, single Undo step)
- Editor tests (Unity Test Framework)
- Declare Modular Avatar (>=1.18.0 <2.0.0) as a VPM dependency
