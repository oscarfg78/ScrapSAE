## Context

The root directory of ScrapSAE currently mixes source code, project files, configuration, utility scripts, documentation, and raw scrape data. This makes it hard to distinguish what is an essential part of the system versus what is temporary or auxiliary.

## Goals / Non-Goals

**Goals:**
- Create standard directories (`docs/`, `scripts/`, `configs/`, `temp/`, `tools/`).
- Move the miscellaneous files and folders from the root to their proper locations.
- Keep only standard root-level configuration files (like `.gitignore`, `.sln`, `openspec` dir) and essential project components (`src`, `tests`, `database`) in the root.

**Non-Goals:**
- We will not refactor the C# code in `src/` or `tests/`.
- We will not rewrite scripts, although we will update relative paths within them if they break due to the move.

## Decisions

- **docs/**: Used for all `.md` and `.docx` files that document project capabilities or issues (e.g., `CAMBIOS_IMPLEMENTADOS.md`, `ScrappSAE.docx`). Also hosts example files (e.g. `ejemplos/`).
- **scripts/**: Used for `.ps1` and `.py` files that are utilities for the project (e.g., `add_column.ps1`, `list_sites.py`).
- **configs/**: Used for standalone JSON or TXT config files that configure external scripts (e.g., `festo_config.json`).
- **temp/**: Used for `.html`, `.js`, `.json` files that are dumps or temporary state of scrapes (e.g., `tmp_store_page.html`, `stealth_script.js`).
- **tools/**: Used for third-party executables or tools (e.g., `thirds/`).

## Risks / Trade-offs

- **Risk**: Hardcoded paths in utility scripts may break.
  - **Mitigation**: Search for usage of moved configurations inside the scripts and update them if necessary, or let developers know they need to run the scripts from the `scripts/` directory now.
