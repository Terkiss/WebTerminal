---
name: detect-stuck-subagent
description: Monitor running subagents to detect deadlocks or infinite loops by checking if project source files have been modified recently. Use when subagents have been running for a long time without completion.
---

# Detect Stuck Subagent Skill

## Context
When subagents (like Ralph Orchestrator or AGY Worker) are running in the background for extended periods (e.g., > 1-2 hours), they may be stuck in a deadlock or infinite test loop. Relying solely on their active task status is insufficient.

## Instructions
When invoked to check the health of subagents, follow these steps:

1. **Query Active Subagents**: Use the `manage_subagents` tool to list currently active subagents. If none are active, exit.
2. **Check File Modification Times**: Run a PowerShell command to check the `LastWriteTime` of source files in the project workspace, strictly excluding build artifacts like `bin\` or `obj\`.
   - Example Command:
     ```powershell
     Get-ChildItem -Path ".\src" -File -Recurse | Where-Object { $_.FullName -notmatch "\\obj\\" -and $_.FullName -notmatch "\\bin\\" } | Sort-Object LastWriteTime -Descending | Select-Object FullName, LastWriteTime -First 10
     ```
3. **Evaluate the Gap**: Compare the current system time with the most recent `LastWriteTime` found.
4. **Action on Deadlock**:
   - If the most recent modification was **more than 2 hours ago**, conclude that the subagents are stuck.
   - Report the findings to the USER immediately.
   - Wait for USER confirmation, and then use `manage_subagents` to `kill` or `kill_all` the stalled subagents.
