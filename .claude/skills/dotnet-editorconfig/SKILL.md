---
name: dotnet-editorconfig
description: |
  Configure .NET code analysis, code style, formatting, and naming conventions using .editorconfig and .globalconfig files. 
  Use when creating, editing, or reviewing .editorconfig files for C#/.NET projects, configuring Roslyn analyzers (IDE/CA rules), setting code quality rules, defining naming conventions, formatting options, setting analysis severity levels, suppressing or escalating analyzer warnings, or configuring MSBuild code analysis properties. Covers all IDE style rules, CA quality rules, formatting (newlines, indentation, spacing, wrapping), naming rule syntax (IDE1006), MSBuild analysis properties (AnalysisLevel, AnalysisMode, EnforceCodeStyleInBuild), severity configuration (dotnet_diagnostic), and generated code exclusions.
---

# .NET EditorConfig & Code Analysis

Complete reference for configuring .NET code analysis, code style, formatting, and naming conventions via `.editorconfig` files.

## Architecture Overview

.NET code analysis has two pillars, both configured primarily through `.editorconfig`:

| Pillar           | Rule Prefix | Purpose                                                       | Default on Build         |
|------------------|-------------|---------------------------------------------------------------|--------------------------|
| **Code Quality** | `CA####`    | Security, performance, design, reliability issues             | Yes (.NET 5+)            |
| **Code Style**   | `IDE####`   | Consistent coding style, unnecessary code, formatting, naming | No (IDE only by default) |

Both are powered by **Roslyn analyzers** built into the .NET SDK.

## Configuration Files

### EditorConfig (`.editorconfig`)

File-based or folder-based configuration. Place at repo root with `root = true`.

```ini
root = true

[*.cs]
<option_name> = <option_value>
```

- Options apply to source files in the directory and subdirectories
- Deeper `.editorconfig` files override parent ones
- EditorConfig overrides Visual Studio user settings

### Global AnalyzerConfig (`.globalconfig`)

Project-level configuration. Useful when source files reside outside the project folder.

```ini
is_global = true
global_level = 100

dotnet_diagnostic.CA1822.severity = warning
```

### Precedence Rules

1. Command-line compiler options (`-nowarn`, `-warnaserror`) win over everything
2. EditorConfig wins over Global AnalyzerConfig
3. Deeper EditorConfig files win over shallower ones
4. Later entries in the same file win over earlier ones
5. For severity: rule ID > category > all rules

## MSBuild Properties (in `.csproj` or `Directory.Build.props`)

These configure analysis at the **bulk level** and override EditorConfig bulk settings.

| Property                            | Values                                             | Purpose                                                               |
|-------------------------------------|----------------------------------------------------|-----------------------------------------------------------------------|
| `EnableNETAnalyzers`                | `true`/`false`                                     | Enable/disable CA rules (default `true` for .NET 5+)                  |
| `AnalysisLevel`                     | `5`-`10`, `latest`, `preview`, `none`              | Lock rules to a specific SDK version                                  |
| `AnalysisMode`                      | `None`, `Default`, `Minimum`, `Recommended`, `All` | How many rules are enabled                                            |
| `AnalysisLevel` (compound)          | `latest-Recommended`, `10-All`, etc.               | Combines level + mode                                                 |
| `AnalysisMode<Category>`            | Same as AnalysisMode                               | Per-category: `AnalysisModePerformance`, `AnalysisModeSecurity`, etc. |
| `EnforceCodeStyleInBuild`           | `true`/`false`                                     | Enable IDE style rules on build (default `false`)                     |
| `CodeAnalysisTreatWarningsAsErrors` | `true`/`false`                                     | Prevent CA warnings from becoming errors with `-warnaserror`          |

```xml
<PropertyGroup>
  <AnalysisLevel>latest-Recommended</AnalysisLevel>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
</PropertyGroup>
```

### AnalysisMode Values

| Mode          | Behavior                                                                                                           |
|---------------|--------------------------------------------------------------------------------------------------------------------|
| `None`        | All rules disabled; opt-in individually                                                                            |
| `Default`     | Small set enabled as warnings; rest as suggestions or disabled                                                     |
| `Minimum`     | More aggressive than Default; highly recommended suggestions become warnings                                       |
| `Recommended` | More aggressive than Minimum; even more rules enabled as warnings                                                  |
| `All`         | All rules enabled as warnings (except a few legacy: CA1017, CA1045, CA1005, CA1014, CA1060, CA1021, CA1501-CA1509) |

## Severity Configuration

### Severity Levels

| Value        | Build Behavior   | IDE Behavior                  |
|--------------|------------------|-------------------------------|
| `error`      | Build fails      | Red squiggle                  |
| `warning`    | Build warning    | Green squiggle                |
| `suggestion` | Build message    | Gray dots                     |
| `silent`     | Not visible      | Code generation still applies |
| `none`       | Fully suppressed | No effect                     |
| `default`    | Use SDK default  | Use SDK default               |

### Syntax for Setting Severity

```ini
# Single rule
dotnet_diagnostic.CA1822.severity = warning
dotnet_diagnostic.IDE0040.severity = warning

# Category of rules (only affects enabled-by-default rules)
dotnet_analyzer_diagnostic.category-Performance.severity = warning
dotnet_analyzer_diagnostic.category-Style.severity = suggestion

# All rules (only affects enabled-by-default rules)
dotnet_analyzer_diagnostic.severity = suggestion
```

### Severity in Style Options (.NET 9+)

Starting .NET 9, you can inline severity in style options:

```ini
dotnet_style_require_accessibility_modifiers = always:warning
csharp_style_var_for_built_in_types = true:suggestion
```

For .NET 8 and earlier, use `dotnet_diagnostic.<ID>.severity` syntax for build enforcement.

## Code Style Rules (IDE)

Full rule index and options: see [references/style-rules-index.md](references/style-rules-index.md)

### Categories

| Category             | Description                             | Example Rules                              |
|----------------------|-----------------------------------------|--------------------------------------------|
| **Language rules**   | C#/VB language construct preferences    | `var`, pattern matching, expression bodies |
| **Formatting rules** | Layout and structure (IDE0055)          | Newlines, indentation, spacing, wrapping   |
| **Naming rules**     | Identifier naming conventions (IDE1006) | PascalCase, _camelCase prefixes            |
| **Unnecessary code** | Dead code detection                     | Unused members, unreachable code           |

### Key Language Style Options

```ini
[*.cs]
# var preferences
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = true:suggestion

# Expression-bodied members
csharp_style_expression_bodied_methods = false:silent
csharp_style_expression_bodied_constructors = false:silent
csharp_style_expression_bodied_properties = true:suggestion
csharp_style_expression_bodied_accessors = true:suggestion
csharp_style_expression_bodied_lambdas = true:suggestion
csharp_style_expression_bodied_local_functions = false:silent

# Pattern matching
csharp_style_pattern_matching_over_as_with_null_check = true:suggestion
csharp_style_pattern_matching_over_is_with_cast_check = true:suggestion
csharp_style_prefer_switch_expression = true:suggestion
csharp_style_prefer_pattern_matching = true:suggestion
csharp_style_prefer_not_pattern = true:suggestion
csharp_style_prefer_extended_property_pattern = true:suggestion

# Null checking
csharp_style_conditional_delegate_call = true:suggestion
csharp_style_throw_expression = true:suggestion

# Code block preferences
csharp_prefer_braces = true:suggestion
csharp_prefer_simple_using_statement = true:suggestion
csharp_style_namespace_declarations = file_scoped:warning
csharp_style_prefer_primary_constructors = true:suggestion
csharp_style_prefer_top_level_statements = true:suggestion

# Modifier preferences
csharp_prefer_static_local_function = true:suggestion
csharp_preferred_modifier_order = public,private,protected,internal,file,static,extern,new,virtual,abstract,sealed,override,readonly,unsafe,required,volatile,async:suggestion
csharp_style_prefer_readonly_struct = true:suggestion
csharp_style_prefer_readonly_struct_member = true:suggestion

# Expression-level
csharp_prefer_simple_default_expression = true:suggestion
csharp_style_deconstructed_variable_declaration = true:suggestion
csharp_style_implicit_object_creation_when_type_is_apparent = true:suggestion
csharp_style_inlined_variable_declaration = true:suggestion
csharp_style_prefer_index_operator = true:suggestion
csharp_style_prefer_range_operator = true:suggestion
csharp_style_prefer_tuple_swap = true:suggestion
csharp_style_prefer_utf8_string_literals = true:suggestion
csharp_style_prefer_local_over_anonymous_function = true:suggestion
csharp_style_prefer_null_check_over_type_check = true:suggestion
csharp_style_unused_value_assignment_preference = discard_variable:suggestion
csharp_style_unused_value_expression_statement_preference = discard_variable:silent

# .NET style (applies to C# and VB)
dotnet_style_qualification_for_field = false:suggestion
dotnet_style_qualification_for_property = false:suggestion
dotnet_style_qualification_for_method = false:suggestion
dotnet_style_qualification_for_event = false:suggestion
dotnet_style_predefined_type_for_locals_parameters_members = true:suggestion
dotnet_style_predefined_type_for_member_access = true:suggestion
dotnet_style_require_accessibility_modifiers = for_non_interface_members:suggestion
dotnet_style_readonly_field = true:suggestion
dotnet_style_parentheses_in_arithmetic_binary_operators = always_for_clarity:suggestion
dotnet_style_parentheses_in_relational_binary_operators = always_for_clarity:suggestion
dotnet_style_parentheses_in_other_binary_operators = always_for_clarity:suggestion
dotnet_style_parentheses_in_other_operators = never_if_unnecessary:suggestion
dotnet_style_object_initializer = true:suggestion
dotnet_style_collection_initializer = true:suggestion
dotnet_style_prefer_collection_expression = when_types_loosely_match:suggestion
dotnet_style_explicit_tuple_names = true:suggestion
dotnet_style_prefer_inferred_tuple_names = true:suggestion
dotnet_style_prefer_inferred_anonymous_type_member_names = true:suggestion
dotnet_style_prefer_auto_properties = true:suggestion
dotnet_style_prefer_conditional_expression_over_assignment = true:suggestion
dotnet_style_prefer_conditional_expression_over_return = true:suggestion
dotnet_style_prefer_compound_assignment = true:suggestion
dotnet_style_prefer_simplified_boolean_expressions = true:suggestion
dotnet_style_prefer_simplified_interpolation = true:suggestion
dotnet_style_coalesce_expression = true:suggestion
dotnet_style_null_propagation = true:suggestion
dotnet_style_prefer_is_null_check_over_reference_equality_method = true:suggestion
dotnet_style_namespace_match_folder = true:suggestion
dotnet_code_quality_unused_parameters = all:suggestion
dotnet_remove_unnecessary_suppression_exclusions = none
dotnet_style_prefer_foreach_explicit_cast_in_source = when_strongly_typed

# using directives
csharp_using_directive_placement = outside_namespace:warning
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false

# File header
file_header_template = unset
```

### Collection Expression Options (IDE0028, IDE0300-IDE0306)

`dotnet_style_prefer_collection_expression` values:
- `true` / `when_types_loosely_match` - Prefer collection expressions even with implicit conversions
- `when_types_exactly_match` - Only when types match exactly
- `false` - Do not prefer collection expressions

## Formatting Rules (IDE0055)

Full options reference: see [references/formatting-options.md](references/formatting-options.md)

### C# Newline Options

```ini
[*.cs]
# Brace placement: all, none, or comma-separated list of:
#   accessors, anonymous_methods, anonymous_types, control_blocks,
#   events, indexers, lambdas, local_functions, methods,
#   object_collection_array_initializers, properties, types
csharp_new_line_before_open_brace = all

csharp_new_line_before_else = true
csharp_new_line_before_catch = true
csharp_new_line_before_finally = true
csharp_new_line_before_members_in_object_initializers = true
csharp_new_line_before_members_in_anonymous_types = true
csharp_new_line_between_query_expression_clauses = true
```

### C# Indentation Options

```ini
[*.cs]
csharp_indent_case_contents = true
csharp_indent_switch_labels = true
csharp_indent_labels = one_less_than_current  # flush_left | one_less_than_current | no_change
csharp_indent_block_contents = true
csharp_indent_braces = false
csharp_indent_case_contents_when_block = true
```

### C# Spacing Options

```ini
[*.cs]
csharp_space_after_cast = false
csharp_space_after_keywords_in_control_flow_statements = true
csharp_space_between_parentheses = false  # or: control_flow_statements, expressions, type_casts
csharp_space_before_colon_in_inheritance_clause = true
csharp_space_after_colon_in_inheritance_clause = true
csharp_space_around_binary_operators = before_and_after  # before_and_after | none | ignore
csharp_space_between_method_declaration_parameter_list_parentheses = false
csharp_space_between_method_declaration_empty_parameter_list_parentheses = false
csharp_space_between_method_declaration_name_and_open_parenthesis = false
csharp_space_between_method_call_parameter_list_parentheses = false
csharp_space_between_method_call_empty_parameter_list_parentheses = false
csharp_space_between_method_call_name_and_opening_parenthesis = false
csharp_space_after_comma = true
csharp_space_before_comma = false
csharp_space_after_dot = false
csharp_space_before_dot = false
csharp_space_after_semicolon_in_for_statement = true
csharp_space_before_semicolon_in_for_statement = false
csharp_space_around_declaration_statements = false
csharp_space_before_open_square_brackets = false
csharp_space_between_empty_square_brackets = false
csharp_space_between_square_brackets = false
```

### C# Wrapping Options

```ini
[*.cs]
csharp_preserve_single_line_statements = true
csharp_preserve_single_line_blocks = true
```

### .NET Formatting Options

```ini
[*.{cs,vb}]
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false
```

### Experimental New-line Options

```ini
[*.{cs,vb}]
dotnet_style_allow_multiple_blank_lines_experimental = false:warning
dotnet_style_allow_statement_immediately_after_block_experimental = false:warning

[*.cs]
csharp_style_allow_embedded_statements_on_same_line_experimental = false:warning
csharp_style_allow_blank_lines_between_consecutive_braces_experimental = false:warning
csharp_style_allow_blank_line_after_colon_in_constructor_initializer_experimental = false:warning
csharp_style_allow_blank_line_after_token_in_conditional_expression_experimental = false:warning
csharp_style_allow_blank_line_after_token_in_arrow_expression_clause_experimental = false:warning
```

## Naming Rules (IDE1006)

Full syntax reference: see [references/naming-rules.md](references/naming-rules.md)

### Structure

A naming rule has three components defined in `.editorconfig`:

```ini
# 1. Symbol group - WHAT to apply the rule to
dotnet_naming_symbols.<group_name>.applicable_kinds = <kinds>
dotnet_naming_symbols.<group_name>.applicable_accessibilities = <accessibilities>
dotnet_naming_symbols.<group_name>.required_modifiers = <modifiers>

# 2. Naming style - HOW it should be named
dotnet_naming_style.<style_name>.capitalization = <capitalization>
dotnet_naming_style.<style_name>.required_prefix = <prefix>
dotnet_naming_style.<style_name>.required_suffix = <suffix>
dotnet_naming_style.<style_name>.word_separator = <separator>

# 3. Naming rule - CONNECTS symbol group to style with severity
dotnet_naming_rule.<rule_name>.symbols = <group_name>
dotnet_naming_rule.<rule_name>.style = <style_name>
dotnet_naming_rule.<rule_name>.severity = <severity>
```

### Symbol Group Values

| Property                     | Values                                                                                                                                                                 |
|------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `applicable_kinds`           | `*`, `namespace`, `class`, `struct`, `interface`, `enum`, `property`, `method`, `field`, `event`, `delegate`, `parameter`, `type_parameter`, `local`, `local_function` |
| `applicable_accessibilities` | `*`, `public`, `internal`/`friend`, `private`, `protected`, `protected_internal`/`protected_friend`, `private_protected`, `local`                                      |
| `required_modifiers`         | `abstract`/`must_inherit`, `async`, `const`, `readonly`, `static`/`shared`                                                                                             |

### Capitalization Values

| Value              | Example       |
|--------------------|---------------|
| `pascal_case`      | `MyProperty`  |
| `camel_case`       | `myProperty`  |
| `first_word_upper` | `My property` |
| `all_upper`        | `MY_PROPERTY` |
| `all_lower`        | `my_property` |

### Common Naming Conventions Example

```ini
[*.{cs,vb}]

# Interfaces must begin with I
dotnet_naming_symbols.interface.applicable_kinds = interface
dotnet_naming_symbols.interface.applicable_accessibilities = *
dotnet_naming_style.begins_with_i.required_prefix = I
dotnet_naming_style.begins_with_i.capitalization = pascal_case
dotnet_naming_rule.interface_should_be_begins_with_i.symbols = interface
dotnet_naming_rule.interface_should_be_begins_with_i.style = begins_with_i
dotnet_naming_rule.interface_should_be_begins_with_i.severity = warning

# Types should be PascalCase
dotnet_naming_symbols.types.applicable_kinds = class, struct, interface, enum, delegate
dotnet_naming_symbols.types.applicable_accessibilities = *
dotnet_naming_style.pascal_case.capitalization = pascal_case
dotnet_naming_rule.types_should_be_pascal_case.symbols = types
dotnet_naming_rule.types_should_be_pascal_case.style = pascal_case
dotnet_naming_rule.types_should_be_pascal_case.severity = warning

# Public/protected members should be PascalCase
dotnet_naming_symbols.public_members.applicable_kinds = property, method, event, delegate
dotnet_naming_symbols.public_members.applicable_accessibilities = public, protected, protected_internal
dotnet_naming_style.pascal_case_style.capitalization = pascal_case
dotnet_naming_rule.public_members_pascal_case.symbols = public_members
dotnet_naming_rule.public_members_pascal_case.style = pascal_case_style
dotnet_naming_rule.public_members_pascal_case.severity = warning

# Constants should be PascalCase
dotnet_naming_symbols.constants.applicable_kinds = field, local
dotnet_naming_symbols.constants.required_modifiers = const
dotnet_naming_symbols.constants.applicable_accessibilities = *
dotnet_naming_style.constant_style.capitalization = pascal_case
dotnet_naming_rule.constants_should_be_pascal_case.symbols = constants
dotnet_naming_rule.constants_should_be_pascal_case.style = constant_style
dotnet_naming_rule.constants_should_be_pascal_case.severity = warning

# Private fields should be _camelCase
dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private
dotnet_naming_style.underscore_camel_case.capitalization = camel_case
dotnet_naming_style.underscore_camel_case.required_prefix = _
dotnet_naming_rule.private_fields_underscore.symbols = private_fields
dotnet_naming_rule.private_fields_underscore.style = underscore_camel_case
dotnet_naming_rule.private_fields_underscore.severity = warning

# Private static fields - same _camelCase convention as instance fields
dotnet_naming_symbols.private_static_fields.applicable_kinds = field
dotnet_naming_symbols.private_static_fields.applicable_accessibilities = private
dotnet_naming_symbols.private_static_fields.required_modifiers = static
dotnet_naming_rule.private_static_fields_underscore.symbols = private_static_fields
dotnet_naming_rule.private_static_fields_underscore.style = underscore_camel_case
dotnet_naming_rule.private_static_fields_underscore.severity = warning

# Parameters should be camelCase
dotnet_naming_symbols.parameters.applicable_kinds = parameter
dotnet_naming_symbols.parameters.applicable_accessibilities = *
dotnet_naming_style.camel_case_style.capitalization = camel_case
dotnet_naming_rule.parameters_camel_case.symbols = parameters
dotnet_naming_rule.parameters_camel_case.style = camel_case_style
dotnet_naming_rule.parameters_camel_case.severity = warning

# Locals should be camelCase
dotnet_naming_symbols.locals.applicable_kinds = local
dotnet_naming_symbols.locals.applicable_accessibilities = local
dotnet_naming_rule.locals_camel_case.symbols = locals
dotnet_naming_rule.locals_camel_case.style = camel_case_style
dotnet_naming_rule.locals_camel_case.severity = suggestion

# Type parameters should start with T
dotnet_naming_symbols.type_parameters.applicable_kinds = type_parameter
dotnet_naming_symbols.type_parameters.applicable_accessibilities = *
dotnet_naming_style.begins_with_t.required_prefix = T
dotnet_naming_style.begins_with_t.capitalization = pascal_case
dotnet_naming_rule.type_parameters_t_prefix.symbols = type_parameters
dotnet_naming_rule.type_parameters_t_prefix.style = begins_with_t
dotnet_naming_rule.type_parameters_t_prefix.severity = warning

# Async methods should end with Async
dotnet_naming_symbols.async_methods.applicable_kinds = method
dotnet_naming_symbols.async_methods.applicable_accessibilities = *
dotnet_naming_symbols.async_methods.required_modifiers = async
dotnet_naming_style.async_suffix.capitalization = pascal_case
dotnet_naming_style.async_suffix.required_suffix = Async
dotnet_naming_rule.async_methods_should_end_with_async.symbols = async_methods
dotnet_naming_rule.async_methods_should_end_with_async.style = async_suffix
dotnet_naming_rule.async_methods_should_end_with_async.severity = suggestion
```

### Rule Ordering

Rules are automatically ordered by specificity (most specific wins):
- More specific `applicable_kinds` beats broader ones
- More specific `required_modifiers` beats fewer
- If overlap exists, create an explicit intersection rule

**Important**: Naming rule severity in `.editorconfig` is only respected in IDEs, not on build. To enforce on build, use `dotnet_diagnostic.IDE1006.severity = warning`.

## Code Quality Rules (CA)

Full category reference: see [references/code-quality-categories.md](references/code-quality-categories.md)

### Rule Categories

| Category         | Prefix                                      | Description                     |
|------------------|---------------------------------------------|---------------------------------|
| Design           | CA1000-CA1070                               | API design guidelines           |
| Documentation    | CA1200                                      | XML documentation               |
| Globalization    | CA1300-CA1311                               | Internationalization            |
| Interoperability | CA1400-CA1422                               | COM and P/Invoke                |
| Maintainability  | CA1500-CA1509                               | Code maintainability            |
| Naming           | CA1700-CA1727                               | Naming guidelines               |
| Performance      | CA1800-CA1870                               | Performance optimizations       |
| Reliability      | CA2000-CA2022                               | Exception and resource handling |
| Security         | CA2100-CA2166, CA3000-CA3147, CA5300-CA5405 | Security vulnerabilities        |
| Usage            | CA2200-CA2265                               | Correct API usage               |

### Code Quality Rule Options

Configure which API surfaces rules apply to:

```ini
[*.cs]
# For all CA rules
dotnet_code_quality.api_surface = public

# For a category
dotnet_code_quality.Naming.api_surface = public, internal

# For a specific rule
dotnet_code_quality.CA1040.api_surface = public
```

`api_surface` values: `public`, `internal`/`friend`, `private`, `all` (comma-separated)

### Excluding Generated Code

```ini
[*.MyGenerated.cs]
generated_code = true
```

Files matching `*.designer.cs` or `*.generated.cs` are excluded by default.

## Core EditorConfig Properties

These are standard EditorConfig properties (not .NET-specific):

```ini
root = true

[*]
indent_style = space
indent_size = 4
tab_width = 4
end_of_line = crlf
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true

[*.{cs,vb}]
# .NET-specific rules go here

[*.cs]
# C#-specific rules go here

[*.{xml,csproj,props,targets}]
indent_size = 2

[*.json]
indent_size = 2
```

## Creating an EditorConfig File

### Using `dotnet` CLI

```bash
dotnet new editorconfig
```

### Using Visual Studio

**Project > Add New Item > editorconfig File (.NET)**

### From Visual Studio Settings

**Tools > Options > Text Editor > C# > Code Style > General > Generate .editorconfig file from settings**

## Enabling Code Style on Build

1. Set MSBuild property:
    ```xml
    <PropertyGroup>
      <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    </PropertyGroup>
    ```

2. Configure severity in `.editorconfig`:
    ```ini
    [*.cs]
    # Enable entire Style category as warnings
    dotnet_analyzer_diagnostic.category-Style.severity = warning
    
    # Or enable specific rules
    dotnet_diagnostic.IDE0040.severity = warning
    dotnet_diagnostic.IDE0161.severity = warning
    ```

A handful of IDE rules only run in the Visual Studio IDE for performance reasons and cannot be enforced on build.

## Learn More (Dynamic Lookup)

For detailed information on specific rules or topics, use these queries:

| Topic                  | Query                                                                                                                          |
|------------------------|--------------------------------------------------------------------------------------------------------------------------------|
| Specific IDE rule      | `microsoft_docs_search(query="IDE0XXX dotnet code style rule")`                                                                |
| Specific CA rule       | `microsoft_docs_search(query="CA1XXX dotnet code quality rule")`                                                               |
| Third-party analyzers  | `microsoft_docs_search(query="StyleCop Roslynator Meziantou analyzer dotnet")`                                                 |
| dotnet format CLI      | `microsoft_docs_search(query="dotnet format command line tool editorconfig")`                                                  |
| Global AnalyzerConfig  | `microsoft_docs_fetch(url="https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-files")`          |
| Full CA rule list      | `microsoft_docs_fetch(url="https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/")`               |
| Full IDE rule list     | `microsoft_docs_fetch(url="https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/")`                 |
| MSBuild analysis props | `microsoft_docs_fetch(url="https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#code-analysis-properties")` |
| Warning waves          | `microsoft_docs_search(query="dotnet csharp warning waves compiler")`                                                          |
