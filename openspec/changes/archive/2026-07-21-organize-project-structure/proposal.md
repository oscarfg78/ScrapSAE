## Why

The root directory of the project contains many files (scripts, configs, docs, temp files) that clutter the workspace. Organizing these files into structured directories will improve the maintainability, readability, and overall understanding of the project's solution, establishing a standard for where files should reside.

## What Changes

- Move documentation files (`.md`, `.docx`) from the root to a `docs/` folder.
- Move PowerShell and Python utility scripts from the root to a `scripts/` folder.
- Move configuration files (`.json`, `.txt`) from the root to a `configs/` folder.
- Move temporary data and scrape dumps from the root to a `temp/` folder.
- Establish a project file organization standard.

## Capabilities

### New Capabilities
- `project-organization`: Defines the standard directory structure and file placement rules for the ScrapSAE project.

### Modified Capabilities


## Impact

- Cleaner project root.
- Easier navigation for developers.
- Scripts or tools depending on hardcoded root paths for configurations or dumps might need to be updated to point to the new directories.
