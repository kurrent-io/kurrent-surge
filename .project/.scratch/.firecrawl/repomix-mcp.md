[Skip to content](https://repomix.com/guide/mcp-server#VPContent)

[![](https://repomix.com/images/repomix-logo.svg)Repomix](https://repomix.com/)

Search`` `K`

Main Navigation [Guide](https://repomix.com/guide/) [Chrome Extension](https://chromewebstore.google.com/detail/repomix/fimfamikepjgchehkohedilpdigcpkoa) [Join Discord](https://discord.gg/wNYzTwZFku)

English

[简体中文](https://repomix.com/zh-cn/guide/mcp-server)

[繁體中文](https://repomix.com/zh-tw/guide/mcp-server)

[日本語](https://repomix.com/ja/guide/mcp-server)

[Español](https://repomix.com/es/guide/mcp-server)

[Português](https://repomix.com/pt-br/guide/mcp-server)

[한국어](https://repomix.com/ko/guide/mcp-server)

[Deutsch](https://repomix.com/de/guide/mcp-server)

[Français](https://repomix.com/fr/guide/mcp-server)

[Italiano](https://repomix.com/it/guide/mcp-server)

[हिन्दी](https://repomix.com/hi/guide/mcp-server)

[Indonesia](https://repomix.com/id/guide/mcp-server)

[Tiếng Việt](https://repomix.com/vi/guide/mcp-server)

[Русский](https://repomix.com/ru/guide/mcp-server)

[x](https://x.com/repomix_ai)[discord](https://discord.gg/wNYzTwZFku)[github](https://github.com/yamadashy/repomix)

English

[简体中文](https://repomix.com/zh-cn/guide/mcp-server)

[繁體中文](https://repomix.com/zh-tw/guide/mcp-server)

[日本語](https://repomix.com/ja/guide/mcp-server)

[Español](https://repomix.com/es/guide/mcp-server)

[Português](https://repomix.com/pt-br/guide/mcp-server)

[한국어](https://repomix.com/ko/guide/mcp-server)

[Deutsch](https://repomix.com/de/guide/mcp-server)

[Français](https://repomix.com/fr/guide/mcp-server)

[Italiano](https://repomix.com/it/guide/mcp-server)

[हिन्दी](https://repomix.com/hi/guide/mcp-server)

[Indonesia](https://repomix.com/id/guide/mcp-server)

[Tiếng Việt](https://repomix.com/vi/guide/mcp-server)

[Русский](https://repomix.com/ru/guide/mcp-server)

Appearance

[x](https://x.com/repomix_ai)[discord](https://discord.gg/wNYzTwZFku)[github](https://github.com/yamadashy/repomix)

Menu

On this page

Sidebar Navigation

## Introduction

[Getting Started](https://repomix.com/guide/)

[Installation](https://repomix.com/guide/installation)

[Basic Usage](https://repomix.com/guide/usage)

[Prompt Examples](https://repomix.com/guide/prompt-examples)

[Use Cases](https://repomix.com/guide/use-cases)

## Guide

[Output Formats](https://repomix.com/guide/output)

[Command Line Options](https://repomix.com/guide/command-line-options)

[Configuration](https://repomix.com/guide/configuration)

[Custom Instructions](https://repomix.com/guide/custom-instructions)

[GitHub Repository Processing](https://repomix.com/guide/remote-repository-processing)

[Comment Removal](https://repomix.com/guide/comment-removal)

[Code Compression](https://repomix.com/guide/code-compress)

[Security](https://repomix.com/guide/security)

## Advanced

[MCP Server](https://repomix.com/guide/mcp-server)

[Claude Code Plugins](https://repomix.com/guide/claude-code-plugins)

[Agent Skills Generation](https://repomix.com/guide/agent-skills-generation)

[Repomix Explorer Skill](https://repomix.com/guide/repomix-explorer-skill)

[GitHub Actions](https://repomix.com/guide/github-actions)

[Using Repomix as a Library](https://repomix.com/guide/development/using-repomix-as-a-library)

[AI-Assisted Development Tips](https://repomix.com/guide/tips/best-practices)

## Community

[Community Projects](https://repomix.com/guide/community-projects)

[Contributing to Repomix](https://repomix.com/guide/development/)

[Sponsors](https://repomix.com/guide/sponsors)

[Privacy Policy](https://repomix.com/guide/privacy)

On this page

- [Running Repomix as an MCP Server](https://repomix.com/guide/mcp-server#running-repomix-as-an-mcp-server "Running Repomix as an MCP Server")
- [Configuring MCP Servers](https://repomix.com/guide/mcp-server#configuring-mcp-servers "Configuring MCP Servers")
  - [For VS Code](https://repomix.com/guide/mcp-server#for-vs-code "For VS Code")
  - [For Cline (VS Code extension)](https://repomix.com/guide/mcp-server#for-cline-vs-code-extension "For Cline (VS Code extension)")
  - [For Cursor](https://repomix.com/guide/mcp-server#for-cursor "For Cursor")
  - [For Claude Desktop](https://repomix.com/guide/mcp-server#for-claude-desktop "For Claude Desktop")
  - [For Claude Code](https://repomix.com/guide/mcp-server#for-claude-code "For Claude Code")
  - [Using Docker instead of npx](https://repomix.com/guide/mcp-server#using-docker-instead-of-npx "Using Docker instead of npx")
- [Available MCP Tools](https://repomix.com/guide/mcp-server#available-mcp-tools "Available MCP Tools")
  - [pack\_codebase](https://repomix.com/guide/mcp-server#pack-codebase "pack_codebase")
  - [pack\_remote\_repository](https://repomix.com/guide/mcp-server#pack-remote-repository "pack_remote_repository")
  - [read\_repomix\_output](https://repomix.com/guide/mcp-server#read-repomix-output "read_repomix_output")
  - [grep\_repomix\_output](https://repomix.com/guide/mcp-server#grep-repomix-output "grep_repomix_output")
  - [file\_system\_read\_file and file\_system\_read\_directory](https://repomix.com/guide/mcp-server#file-system-read-file-and-file-system-read-directory "file_system_read_file and file_system_read_directory")
- [Benefits of Using Repomix as an MCP Server](https://repomix.com/guide/mcp-server#benefits-of-using-repomix-as-an-mcp-server "Benefits of Using Repomix as an MCP Server")

# MCP Server [​](https://repomix.com/guide/mcp-server\#mcp-server)

Repomix supports the [Model Context Protocol (MCP)](https://modelcontextprotocol.io/), allowing AI assistants to directly interact with your codebase. When run as an MCP server, Repomix provides tools that enable AI assistants to package local or remote repositories for analysis without requiring manual file preparation.

NOTE

This is an experimental feature that we'll be actively improving based on user feedback and real-world usage

## Running Repomix as an MCP Server [​](https://repomix.com/guide/mcp-server\#running-repomix-as-an-mcp-server)

To run Repomix as an MCP server, use the `--mcp` flag:

bash

```
repomix --mcp
```

This starts Repomix in MCP server mode, making it available for AI assistants that support the Model Context Protocol.

## Configuring MCP Servers [​](https://repomix.com/guide/mcp-server\#configuring-mcp-servers)

To use Repomix as an MCP server with AI assistants like Claude, you need to configure the MCP settings:

### For VS Code [​](https://repomix.com/guide/mcp-server\#for-vs-code)

You can install the Repomix MCP server in VS Code using one of these methods:

1. **Using the Install Badge:**

[![Install in VS Code](https://img.shields.io/badge/VS_Code-VS_Code?style=flat-square&label=Install%20Server&color=0098FF)](vscode:mcp/install?%7B%22name%22%3A%22repomix%22%2C%22command%22%3A%22npx%22%2C%22args%22%3A%5B%22-y%22%2C%22repomix%22%2C%22--mcp%22%5D%7D)

[![Install in VS Code Insiders](https://img.shields.io/badge/VS_Code_Insiders-VS_Code_Insiders?style=flat-square&label=Install%20Server&color=24bfa5)](vscode-insiders:mcp/install?%7B%22name%22%3A%22repomix%22%2C%22command%22%3A%22npx%22%2C%22args%22%3A%5B%22-y%22%2C%22repomix%22%2C%22--mcp%22%5D%7D)

2. **Using the Command Line:**

bash

```
code --add-mcp '{"name":"repomix","command":"npx","args":["-y","repomix","--mcp"]}'
```

For VS Code Insiders:

bash

```
code-insiders --add-mcp '{"name":"repomix","command":"npx","args":["-y","repomix","--mcp"]}'
```

### For Cline (VS Code extension) [​](https://repomix.com/guide/mcp-server\#for-cline-vs-code-extension)

Edit the `cline_mcp_settings.json` file:

json

```
{
  "mcpServers": {
    "repomix": {
      "command": "npx",
      "args": [\
        "-y",\
        "repomix",\
        "--mcp"\
      ]
    }
  }
}
```

### For Cursor [​](https://repomix.com/guide/mcp-server\#for-cursor)

In Cursor, add a new MCP server from `Cursor Settings` \> `MCP` \> `+ Add new global MCP server` with a configuration similar to Cline.

### For Claude Desktop [​](https://repomix.com/guide/mcp-server\#for-claude-desktop)

Edit the `claude_desktop_config.json` file with similar configuration to Cline's config.

### For Claude Code [​](https://repomix.com/guide/mcp-server\#for-claude-code)

Configure Repomix as an MCP server in [Claude Code](https://docs.anthropic.com/en/docs/claude-code/overview) using the following command:

bash

```
claude mcp add repomix -- npx -y repomix --mcp
```

Alternatively, you can use the **official Repomix plugins** for a more convenient experience. The plugins provide natural language commands and easier setup. See the [Claude Code Plugins](https://repomix.com/guide/claude-code-plugins) documentation for details.

### Using Docker instead of npx [​](https://repomix.com/guide/mcp-server\#using-docker-instead-of-npx)

Instead of using npx, you can also use Docker to run Repomix as an MCP server:

json

```
{
  "mcpServers": {
    "repomix-docker": {
      "command": "docker",
      "args": [\
        "run",\
        "-i",\
        "--rm",\
        "ghcr.io/yamadashy/repomix",\
        "--mcp"\
      ]
    }
  }
}
```

## Available MCP Tools [​](https://repomix.com/guide/mcp-server\#available-mcp-tools)

When running as an MCP server, Repomix provides the following tools:

### pack\_codebase [​](https://repomix.com/guide/mcp-server\#pack-codebase)

This tool packages a local code directory into a consolidated XML file for AI analysis. It analyzes the codebase structure, extracts relevant code content, and generates a comprehensive report including metrics, file tree, and formatted code content.

**Parameters:**

- `directory`: (Required) Absolute path to the directory to pack
- `compress`: (Optional, default: false) Enable Tree-sitter compression to extract essential code signatures and structure while removing implementation details. Reduces token usage by ~70% while preserving semantic meaning. Generally not needed since grep\_repomix\_output allows incremental content retrieval. Use only when you specifically need the entire codebase content for large repositories.
- `includePatterns`: (Optional) Specify files to include using fast-glob patterns. Multiple patterns can be comma-separated (e.g., " **/\*.{js,ts}", "src/**,docs/\*\*"). Only matching files will be processed.
- `ignorePatterns`: (Optional) Specify additional files to exclude using fast-glob patterns. Multiple patterns can be comma-separated (e.g., "test/ **,\*.spec.js", "node\_modules/**,dist/\*\*"). These patterns supplement .gitignore and built-in exclusions.
- `topFilesLength`: (Optional, default: 10) Number of largest files by size to display in the metrics summary for codebase analysis.

**Example:**

json

```
{
  "directory": "/path/to/your/project",
  "compress": false,
  "includePatterns": "src/**/*.ts,**/*.md",
  "ignorePatterns": "**/*.log,tmp/",
  "topFilesLength": 10
}
```

### pack\_remote\_repository [​](https://repomix.com/guide/mcp-server\#pack-remote-repository)

This tool fetches, clones, and packages a GitHub repository into a consolidated XML file for AI analysis. It automatically clones the remote repository, analyzes its structure, and generates a comprehensive report.

**Parameters:**

- `remote`: (Required) GitHub repository URL or user/repo format (e.g., "yamadashy/repomix", " [https://github.com/user/repo](https://github.com/user/repo)", or " [https://github.com/user/repo/tree/branch](https://github.com/user/repo/tree/branch)")
- `compress`: (Optional, default: false) Enable Tree-sitter compression to extract essential code signatures and structure while removing implementation details. Reduces token usage by ~70% while preserving semantic meaning. Generally not needed since grep\_repomix\_output allows incremental content retrieval. Use only when you specifically need the entire codebase content for large repositories.
- `includePatterns`: (Optional) Specify files to include using fast-glob patterns. Multiple patterns can be comma-separated (e.g., " **/\*.{js,ts}", "src/**,docs/\*\*"). Only matching files will be processed.
- `ignorePatterns`: (Optional) Specify additional files to exclude using fast-glob patterns. Multiple patterns can be comma-separated (e.g., "test/ **,\*.spec.js", "node\_modules/**,dist/\*\*"). These patterns supplement .gitignore and built-in exclusions.
- `topFilesLength`: (Optional, default: 10) Number of largest files by size to display in the metrics summary for codebase analysis.

**Example:**

json

```
{
  "remote": "yamadashy/repomix",
  "compress": false,
  "includePatterns": "src/**/*.ts,**/*.md",
  "ignorePatterns": "**/*.log,tmp/",
  "topFilesLength": 10
}
```

### read\_repomix\_output [​](https://repomix.com/guide/mcp-server\#read-repomix-output)

This tool reads the contents of a Repomix-generated output file. Supports partial reading with line range specification for large files. This tool is designed for environments where direct file system access is limited.

**Parameters:**

- `outputId`: (Required) ID of the Repomix output file to read
- `startLine`: (Optional) Starting line number (1-based, inclusive). If not specified, reads from beginning.
- `endLine`: (Optional) Ending line number (1-based, inclusive). If not specified, reads to end.

**Features:**

- Specifically designed for web-based environments or sandboxed applications
- Retrieves the content of previously generated outputs using their ID
- Provides secure access to packed codebase without requiring file system access
- Supports partial reading for large files

**Example:**

json

```
{
  "outputId": "8f7d3b1e2a9c6054",
  "startLine": 100,
  "endLine": 200
}
```

### grep\_repomix\_output [​](https://repomix.com/guide/mcp-server\#grep-repomix-output)

This tool searches for patterns in a Repomix output file using grep-like functionality with JavaScript RegExp syntax. Returns matching lines with optional context lines around matches.

**Parameters:**

- `outputId`: (Required) ID of the Repomix output file to search
- `pattern`: (Required) Search pattern (JavaScript RegExp regular expression syntax)
- `contextLines`: (Optional, default: 0) Number of context lines to show before and after each match. Overridden by beforeLines/afterLines if specified.
- `beforeLines`: (Optional) Number of context lines to show before each match (like grep -B). Takes precedence over contextLines.
- `afterLines`: (Optional) Number of context lines to show after each match (like grep -A). Takes precedence over contextLines.
- `ignoreCase`: (Optional, default: false) Perform case-insensitive matching

**Features:**

- Uses JavaScript RegExp syntax for powerful pattern matching
- Supports context lines for better understanding of matches
- Allows separate control of before/after context lines
- Case-sensitive and case-insensitive search options

**Example:**

json

```
{
  "outputId": "8f7d3b1e2a9c6054",
  "pattern": "function\\s+\\w+\\(",
  "contextLines": 3,
  "ignoreCase": false
}
```

### file\_system\_read\_file and file\_system\_read\_directory [​](https://repomix.com/guide/mcp-server\#file-system-read-file-and-file-system-read-directory)

Repomix's MCP server provides two file system tools that allow AI assistants to safely interact with the local file system:

1. `file_system_read_file`

- Reads file contents from the local file system using absolute paths
- Includes built-in security validation to detect and prevent access to files containing sensitive information
- Implements security validation using [Secretlint](https://github.com/secretlint/secretlint)
- Prevents access to files containing sensitive information (API keys, passwords, secrets)
- Validates absolute paths to prevent directory traversal attacks
- Returns formatted content with clear error messages for invalid paths or security issues

2. `file_system_read_directory`

- Lists the contents of a directory using an absolute path
- Returns a formatted list showing files and subdirectories with clear indicators
- Shows both files and directories with clear indicators (`[FILE]` or `[DIR]`)
- Provides safe directory traversal with proper error handling
- Validates paths and ensures they are absolute
- Useful for exploring project structure and understanding codebase organization

Both tools incorporate robust security measures:

- Absolute path validation to prevent directory traversal attacks
- Permission checks to ensure proper access rights
- Integration with Secretlint for sensitive information detection
- Clear error messaging for better debugging and security awareness

**Example:**

typescript

```
// Reading a file
const fileContent = await tools.file_system_read_file({
  path: '/absolute/path/to/file.txt'
});

// Listing directory contents
const dirContent = await tools.file_system_read_directory({
  path: '/absolute/path/to/directory'
});
```

These tools are particularly useful when AI assistants need to:

- Analyze specific files in the codebase
- Navigate directory structures
- Verify file existence and accessibility
- Ensure secure file system operations

## Benefits of Using Repomix as an MCP Server [​](https://repomix.com/guide/mcp-server\#benefits-of-using-repomix-as-an-mcp-server)

Using Repomix as an MCP server offers several advantages:

1. **Direct Integration**: AI assistants can directly analyze your codebase without manual file preparation.
2. **Efficient Workflow**: Streamlines the process of code analysis by eliminating the need to manually generate and upload files.
3. **Consistent Output**: Ensures that the AI assistant receives the codebase in a consistent, optimized format.
4. **Advanced Features**: Leverages all of Repomix's features like code compression, token counting, and security checks.

Once configured, your AI assistant can directly use Repomix's capabilities to analyze codebases, making code analysis workflows more efficient.

Last updated: 2/17/26, 10:21 AM

Pager

[Previous pageSecurity](https://repomix.com/guide/security)

[Next pageClaude Code Plugins](https://repomix.com/guide/claude-code-plugins)

Released under the MIT License.

Copyright © 2024 Kazuki Yamada