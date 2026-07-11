# Terminal Development Roadmap (Detailed)

# Phase 1 - MVP

## Multi-shell Support
**Description:** Support CMD, PowerShell, Git Bash, WSL and custom executables.
**Implementation:** Abstract IShell interface, launch each shell with Process/Pty, manage stdin/stdout asynchronously.

## Tabs
**Description:** Multiple terminals in one window.
**Implementation:** Each tab owns a terminal session object and renderer.

## Split View
**Description:** Horizontal/vertical splits.
**Implementation:** Docking layout with independent terminal controls.

## Sessions
**Description:** Save/restore open terminals.
**Implementation:** Serialize shell type, working directory, title and startup command to JSON.

## Search
**Description:** Search terminal output.
**Implementation:** Keep scrollback buffer indexed and highlight matches.

## History
**Description:** Local command history.
**Implementation:** Store per-shell history in SQLite/JSON with search.

# Phase 2 - Productivity

## Command Palette
Description: Universal action launcher (Ctrl+Shift+P).
Implementation: Register commands with IDs and fuzzy-search them.

## Quick Commands
Description: Reusable command library.
Implementation: Store categorized commands with variables/placeholders.

## Snippets
Description: Short aliases expanded with Tab.
Implementation: Detect trigger and replace with template.

## Auto Completion
Description: Suggest commands, files and history.
Implementation: Merge shell completion, filesystem and history providers.

# Phase 3 - Professional

## SSH Manager
Description: Save servers and connect in one click.
Implementation: Secure credential storage and SSH library integration.

## Docker
Description: Manage containers visually.
Implementation: Docker CLI/API wrapper.

## Git
Description: Common Git operations.
Implementation: Execute Git commands and parse results.

## Database
Description: Connect to SQL databases.
Implementation: Provider model for SQL Server, MySQL and PostgreSQL.

# Phase 4 - AI

## Explain Error
Description: Analyze last terminal error.
Implementation: Send recent output to LLM with sanitized context.

## Natural Language
Description: Convert plain language to commands.
Implementation: LLM generates command, user approves before execution.

## Explain Command
Description: Explain command before execution.
Implementation: Parse command and request concise explanation.

# Phase 5 - Unique Features

## ERP Integration
Description: Execute ERP queries through MCP/API.
Implementation: Plugin communicating with ERP backend.

## Macro Recorder
Description: Record and replay commands.
Implementation: Timestamp commands and variables.

## Plugin System
Description: Third-party extensions.
Implementation: Plugin SDK with isolated loading and permissions.
