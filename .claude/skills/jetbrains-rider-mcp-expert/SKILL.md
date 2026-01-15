---
name: jetbrains-rider-mcp-expert
description: Use when there is a need for compiler diagnostics, safe symbol renaming, indexed file search, or IDE-configured test runs. Rider MCP provides LSP-like semantic operations (diagnostics, rename, symbol info) plus IDE features (run configs, terminal, project structure, OpenTelemetry tracing) for .NET/C# projects. Prefer over Grep/Glob/Bash when semantic understanding or real compile errors are needed.
---

# JetBrains Rider MCP Expert

## Philosophy: Rider MCP First

**Always prefer Rider MCP tools.** The MCP server connects to a full IDE with:

- Semantic code understanding (not just text matching)
- Pre-built indexes (faster than file system scanning)
- Compiler integration (real errors, not guesses)
- Solution/project awareness (.NET structure)
- OpenTelemetry integration (distributed tracing)

**Fallback to native tools naturally when Rider MCP doesn't deliver results.**

---

## Tool Reference

### Code Analysis & Refactoring

| Tool                 | Purpose                                    |
|----------------------|--------------------------------------------|
| `get_file_problems`  | Compiler errors/warnings with line numbers |
| `get_symbol_info`    | Symbol documentation at position           |
| `rename_refactoring` | Safe symbol rename across entire project   |
| `reformat_file`      | Apply IDE formatting rules                 |

### Search & Navigation

| Tool                         | Purpose                                      |
|------------------------------|----------------------------------------------|
| `find_files_by_name_keyword` | Find files by name (FASTEST - uses indexes)  |
| `find_files_by_glob`         | Find files by glob pattern                   |
| `search_in_files_by_text`    | Search code content (highlights with `\|\|`) |
| `search_in_files_by_regex`   | Regex search in code                         |
| `list_directory_tree`        | Directory tree view                          |
| `get_all_open_file_paths`    | Currently open files in IDE                  |

### File Operations

| Tool                    | Purpose                      |
|-------------------------|------------------------------|
| `get_file_text_by_path` | Read file content            |
| `replace_text_in_file`  | Find and replace in file     |
| `create_new_file`       | Create new file with content |
| `open_file_in_editor`   | Open file in Rider           |

### Project Structure

| Tool                       | Purpose                        |
|----------------------------|--------------------------------|
| `get_project_modules`      | List projects in .NET solution |
| `get_project_dependencies` | List NuGet dependencies        |
| `get_run_configurations`   | List IDE run configurations    |
| `get_repositories`         | List VCS roots                 |

### Execution

| Tool                        | Purpose                               |
|-----------------------------|---------------------------------------|
| `execute_run_configuration` | Run an IDE configuration (tests, app) |
| `execute_terminal_command`  | Run shell command in IDE terminal     |

### OpenTelemetry (Distributed Tracing)

| Tool              | Purpose                          |
|-------------------|----------------------------------|
| `get_services`    | List discovered services         |
| `get_service_map` | Service architecture from traces |
| `get_spans`       | Query distributed traces         |
| `get_log_records` | Query log records                |

---

## Quick Tool Selection

```
What do you need?
│
├─ Compile errors → get_file_problems
├─ Rename symbol → rename_refactoring
├─ Find files → find_files_by_name_keyword (fastest)
├─ Search code → search_in_files_by_text
├─ Run tests → get_run_configurations → execute_run_configuration
├─ Project info → get_project_modules, get_project_dependencies
├─ Debug distributed system → get_services, get_spans, get_log_records
└─ File ops → get_file_text_by_path, replace_text_in_file, create_new_file
```

---

## Common Workflows

### Investigate Compile Errors

```
1. get_file_problems(filePath: "src/MyClass.cs")
2. get_file_text_by_path(pathInProject: "src/MyClass.cs")
3. Fix the issue
4. get_file_problems(...) again to verify
```

### Safe Rename Across Project

```
1. search_in_files_by_text(searchText: "OldName", fileMask: "*.cs")
2. rename_refactoring(pathInProject, symbolName: "OldName", newName: "NewName")
3. get_file_problems(...) to verify
```

### Debug Distributed System

```
1. get_services() → see all services
2. get_service_map() → understand relationships
3. get_spans(serviceName: "my-api", since: "2025-01-06 10:00:00")
4. get_log_records(serviceName: "my-api", severityFilter: ["Error", "Warn"])
```

### Explore Codebase

```
1. get_project_modules() → solution structure
2. list_directory_tree(directoryPath: "src", maxDepth: 2)
3. find_files_by_name_keyword(nameKeyword: "Program")
4. get_file_text_by_path(pathInProject: "src/Program.cs")
```

---

## Key Parameters

### Always Provide `projectPath`

Every tool accepts it. Reduces ambiguity and speeds up execution.

```yaml
get_file_problems:
  filePath: "src/User.cs"
  projectPath: "/Users/dev/MyProject"
```

### File Paths Are Relative

All paths relative to project root, not absolute.

```yaml
pathInProject: "src/Models/User.cs"
```

### Line/Column Are 1-Based

Like editors, not 0-indexed.

### Search Results Use `||` Delimiters

```
some text ||matched|| more text
```

### Truncate Modes for Large Files

`get_file_text_by_path` supports: START (see end), MIDDLE (see both ends), END (see start), NONE (full)

### OpenTelemetry Severity Filters

`get_log_records` accepts: Trace, Debug, Info, Warn, Error, Fatal
