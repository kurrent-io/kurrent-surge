This file is a merged representation of a subset of the codebase, containing specifically included files, combined into a single document by Repomix.
The content has been processed where empty lines have been removed.

# Summary

## Purpose

This is a reference codebase organized into multiple files for AI consumption.
It is designed to be easily searchable using grep and other text-based tools.

## File Structure

This skill contains the following reference files:

| File                   | Contents                                          |
|------------------------|---------------------------------------------------|
| `project-structure.md` | Directory tree with line counts per file          |
| `files.md`             | All file contents (search with `## File: <path>`) |
| `tech-stack.md`        | Languages, frameworks, and dependencies           |
| `summary.md`           | This file - purpose and format explanation        |

## Usage Guidelines

- This file should be treated as read-only. Any changes should be made to the
  original repository files, not this packed version.
- When processing this file, use the file path to distinguish
  between different files in the repository.
- Be aware that this file may contain sensitive information. Handle it with
  the same level of security as you would the original repository.

- Pay special attention to the Repository Instruction. These contain important context and guidelines specific to this project.

## Notes

- Some files may have been excluded based on .gitignore rules and Repomix's configuration
- Binary files are not included in this packed representation. Please refer to the Repository Structure section for a complete list of file paths, including binary files
- Only files matching these patterns are included: src/**/*.cs, src/**/*.csproj, test/**/*.cs, test/**/*.csproj, proto/**/*.proto, *.slnx, Directory.Build.props, Directory.Packages.props
- Files matching patterns in .gitignore are excluded
- Files matching default ignore patterns are excluded
- Empty lines have been removed from all files
- Files are sorted by Git change count (files with more changes are at the bottom)

## Statistics

576 files | 65,767 lines

| Language         | Files |  Lines |
|------------------|------:|-------:|
| C#               |   520 | 58,029 |
| Protocol Buffers |    42 |  7,000 |
| CSPROJ           |    11 |    527 |
| PROPS            |     2 |    167 |
| SLNX             |     1 |     44 |

**Largest files:**
- `src/Kurrent.Grpc.LoadBalancing/asdsad.cs` (1,883 lines)
- `src/KurrentDB.Client/Kurrent/Talyn/TalynBus.cs` (1,178 lines)
- `src/Variant.SourceGenerators/VariantResultsGenerator.cs` (939 lines)
- `src/KurrentDB.Client/Kurrent.Client/Modules/Streams/Operations/StreamsClient.Process.cs` (913 lines)
- `proto/kurrentdb/protocol/v0/.scratch/processors_draft.proto` (876 lines)
- `test/Kurrent.Client.Tests/ValueObjectTests.cs` (844 lines)
- `src/KurrentDB.Client/Kurrent.Client/Modules/Streams/Models/Filters/ExpressionFilter.Parser.cs` (687 lines)
- `test/Kurrent.Client.Tests/Talyn/TalynRegistryTests.cs` (684 lines)
- `src/KurrentDB.Client/Kurrent.Client/Modules/Streams/Metadata/Metadata.cs` (677 lines)
- `src/KurrentDB.Client/Kurrent/DuckDb/DuckDBConnectionStringTests.cs` (620 lines)
