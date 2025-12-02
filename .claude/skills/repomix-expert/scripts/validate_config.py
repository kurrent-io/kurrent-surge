#!/usr/bin/env python3
"""
Repomix Configuration Validator

Validates repomix.config.json files for syntax and semantic correctness.
Checks for common errors and provides helpful error messages.

Usage:
    python3 validate_config.py [path/to/repomix.config.json]

If no path provided, looks for repomix.config.json in current directory.
"""

import json
import sys
from pathlib import Path
from typing import Any, Dict, List, Tuple

# Valid configuration options
VALID_STYLES = ['xml', 'markdown', 'json', 'plain']
VALID_ENCODINGS = ['o200k_base', 'cl100k_base', 'p50k_base', 'r50k_base']
VALID_OUTPUT_KEYS = {
    'filePath', 'style', 'parsableStyle', 'compress', 'headerText',
    'fileSummary', 'directoryStructure', 'files', 'removeComments',
    'removeEmptyLines', 'topFilesLength', 'tokenCountTree', 'showLineNumbers',
    'truncateBase64', 'copyToClipboard', 'includeEmptyDirectories', 'git'
}
VALID_GIT_KEYS = {
    'sortByChanges', 'sortByChangesMaxCommits', 'includeDiffs',
    'includeLogs', 'includeLogsCount'
}
VALID_INPUT_KEYS = {'maxFileSize'}
VALID_IGNORE_KEYS = {'useGitignore', 'useDefaultPatterns', 'customPatterns'}
VALID_SECURITY_KEYS = {'enableSecurityCheck'}
VALID_TOKEN_COUNT_KEYS = {'encoding'}


def load_config(config_path: Path) -> Tuple[Dict[str, Any], List[str]]:
    """Load and parse configuration file."""
    errors = []

    if not config_path.exists():
        errors.append(f"Configuration file not found: {config_path}")
        return {}, errors

    try:
        with open(config_path, 'r', encoding='utf-8') as f:
            config = json.load(f)
    except json.JSONDecodeError as e:
        errors.append(f"Invalid JSON syntax: {e}")
        return {}, errors
    except Exception as e:
        errors.append(f"Error reading file: {e}")
        return {}, errors

    return config, errors


def validate_output_section(output: Dict[str, Any]) -> List[str]:
    """Validate output configuration section."""
    errors = []

    # Check for unknown keys
    unknown_keys = set(output.keys()) - VALID_OUTPUT_KEYS
    if unknown_keys:
        errors.append(f"Unknown output keys: {', '.join(unknown_keys)}")

    # Validate style
    if 'style' in output:
        if output['style'] not in VALID_STYLES:
            errors.append(
                f"Invalid style '{output['style']}'. "
                f"Must be one of: {', '.join(VALID_STYLES)}"
            )

    # Validate boolean fields
    bool_fields = [
        'parsableStyle', 'compress', 'fileSummary', 'directoryStructure',
        'files', 'removeComments', 'removeEmptyLines', 'tokenCountTree',
        'showLineNumbers', 'truncateBase64', 'copyToClipboard',
        'includeEmptyDirectories'
    ]
    for field in bool_fields:
        if field in output and not isinstance(output[field], bool):
            errors.append(f"output.{field} must be a boolean")

    # Validate numeric fields
    if 'topFilesLength' in output:
        if not isinstance(output['topFilesLength'], int) or output['topFilesLength'] < 1:
            errors.append("output.topFilesLength must be a positive integer")

    # Validate filePath
    if 'filePath' in output:
        if not isinstance(output['filePath'], str) or not output['filePath']:
            errors.append("output.filePath must be a non-empty string")

    # Validate git section
    if 'git' in output:
        git = output['git']
        if not isinstance(git, dict):
            errors.append("output.git must be an object")
        else:
            errors.extend(validate_git_section(git))

    return errors


def validate_git_section(git: Dict[str, Any]) -> List[str]:
    """Validate git configuration section."""
    errors = []

    # Check for unknown keys
    unknown_keys = set(git.keys()) - VALID_GIT_KEYS
    if unknown_keys:
        errors.append(f"Unknown git keys: {', '.join(unknown_keys)}")

    # Validate boolean fields
    bool_fields = ['sortByChanges', 'includeDiffs', 'includeLogs']
    for field in bool_fields:
        if field in git and not isinstance(git[field], bool):
            errors.append(f"output.git.{field} must be a boolean")

    # Validate numeric fields
    if 'sortByChangesMaxCommits' in git:
        if not isinstance(git['sortByChangesMaxCommits'], int) or git['sortByChangesMaxCommits'] < 1:
            errors.append("output.git.sortByChangesMaxCommits must be a positive integer")

    if 'includeLogsCount' in git:
        if not isinstance(git['includeLogsCount'], int) or git['includeLogsCount'] < 1:
            errors.append("output.git.includeLogsCount must be a positive integer")

    return errors


def validate_input_section(input_config: Dict[str, Any]) -> List[str]:
    """Validate input configuration section."""
    errors = []

    # Check for unknown keys
    unknown_keys = set(input_config.keys()) - VALID_INPUT_KEYS
    if unknown_keys:
        errors.append(f"Unknown input keys: {', '.join(unknown_keys)}")

    # Validate maxFileSize
    if 'maxFileSize' in input_config:
        max_size = input_config['maxFileSize']
        if not isinstance(max_size, int) or max_size < 1:
            errors.append("input.maxFileSize must be a positive integer")

    return errors


def validate_ignore_section(ignore: Dict[str, Any]) -> List[str]:
    """Validate ignore configuration section."""
    errors = []

    # Check for unknown keys
    unknown_keys = set(ignore.keys()) - VALID_IGNORE_KEYS
    if unknown_keys:
        errors.append(f"Unknown ignore keys: {', '.join(unknown_keys)}")

    # Validate boolean fields
    bool_fields = ['useGitignore', 'useDefaultPatterns']
    for field in bool_fields:
        if field in ignore and not isinstance(ignore[field], bool):
            errors.append(f"ignore.{field} must be a boolean")

    # Validate customPatterns
    if 'customPatterns' in ignore:
        patterns = ignore['customPatterns']
        if not isinstance(patterns, list):
            errors.append("ignore.customPatterns must be an array")
        elif not all(isinstance(p, str) for p in patterns):
            errors.append("All ignore.customPatterns must be strings")

    return errors


def validate_security_section(security: Dict[str, Any]) -> List[str]:
    """Validate security configuration section."""
    errors = []

    # Check for unknown keys
    unknown_keys = set(security.keys()) - VALID_SECURITY_KEYS
    if unknown_keys:
        errors.append(f"Unknown security keys: {', '.join(unknown_keys)}")

    # Validate enableSecurityCheck
    if 'enableSecurityCheck' in security:
        if not isinstance(security['enableSecurityCheck'], bool):
            errors.append("security.enableSecurityCheck must be a boolean")

    return errors


def validate_token_count_section(token_count: Dict[str, Any]) -> List[str]:
    """Validate tokenCount configuration section."""
    errors = []

    # Check for unknown keys
    unknown_keys = set(token_count.keys()) - VALID_TOKEN_COUNT_KEYS
    if unknown_keys:
        errors.append(f"Unknown tokenCount keys: {', '.join(unknown_keys)}")

    # Validate encoding
    if 'encoding' in token_count:
        encoding = token_count['encoding']
        if encoding not in VALID_ENCODINGS:
            errors.append(
                f"Invalid encoding '{encoding}'. "
                f"Must be one of: {', '.join(VALID_ENCODINGS)}"
            )

    return errors


def validate_include_patterns(include: Any) -> List[str]:
    """Validate include patterns."""
    errors = []

    if not isinstance(include, list):
        errors.append("include must be an array")
    elif not all(isinstance(p, str) for p in include):
        errors.append("All include patterns must be strings")

    return errors


def validate_config(config: Dict[str, Any]) -> List[str]:
    """Validate entire configuration."""
    errors = []
    warnings = []

    # Validate top-level sections
    if 'output' in config:
        if not isinstance(config['output'], dict):
            errors.append("output must be an object")
        else:
            errors.extend(validate_output_section(config['output']))

    if 'input' in config:
        if not isinstance(config['input'], dict):
            errors.append("input must be an object")
        else:
            errors.extend(validate_input_section(config['input']))

    if 'ignore' in config:
        if not isinstance(config['ignore'], dict):
            errors.append("ignore must be an object")
        else:
            errors.extend(validate_ignore_section(config['ignore']))

    if 'security' in config:
        if not isinstance(config['security'], dict):
            errors.append("security must be an object")
        else:
            errors.extend(validate_security_section(config['security']))

    if 'tokenCount' in config:
        if not isinstance(config['tokenCount'], dict):
            errors.append("tokenCount must be an object")
        else:
            errors.extend(validate_token_count_section(config['tokenCount']))

    if 'include' in config:
        errors.extend(validate_include_patterns(config['include']))

    # Check for unknown top-level keys
    valid_top_level_keys = {
        '$schema', 'input', 'output', 'include', 'ignore',
        'security', 'tokenCount'
    }
    unknown_keys = set(config.keys()) - valid_top_level_keys
    if unknown_keys:
        errors.append(f"Unknown top-level keys: {', '.join(unknown_keys)}")

    # Warnings (not errors but potentially issues)
    if 'security' in config:
        if config['security'].get('enableSecurityCheck') is False:
            warnings.append("Security check is disabled - sensitive data may be exposed")

    if 'output' in config:
        if config['output'].get('compress') is False and 'include' not in config:
            warnings.append(
                "Compression disabled without include filter - "
                "output may be very large"
            )

    return errors + [f"WARNING: {w}" for w in warnings]


def main():
    """Main entry point."""
    # Determine config path
    if len(sys.argv) > 1:
        config_path = Path(sys.argv[1])
    else:
        # Look for config files in priority order
        candidates = [
            'repomix.config.json',
            'repomix.config.js',
            'repomix.config.ts'
        ]
        config_path = None
        for candidate in candidates:
            if Path(candidate).exists():
                config_path = Path(candidate)
                break

        if config_path is None:
            print("❌ No configuration file found")
            print("   Looking for: repomix.config.json/js/ts")
            print("\nUsage: python3 validate_config.py [path/to/config.json]")
            sys.exit(1)

    # Note for TypeScript/JavaScript configs
    if config_path.suffix in ['.ts', '.js']:
        print(f"ℹ️  TypeScript/JavaScript config detected: {config_path}")
        print("   This validator only checks JSON files.")
        print("   TypeScript configs have built-in type checking.")
        sys.exit(0)

    print(f"🔍 Validating: {config_path}")
    print()

    # Load configuration
    config, load_errors = load_config(config_path)
    if load_errors:
        print("❌ Configuration file errors:")
        for error in load_errors:
            print(f"   - {error}")
        sys.exit(1)

    # Validate configuration
    validation_errors = validate_config(config)

    if not validation_errors:
        print("✅ Configuration is valid!")
        print()
        print("Summary:")
        if 'output' in config:
            print(f"   - Output style: {config['output'].get('style', 'xml')}")
            print(f"   - Compression: {config['output'].get('compress', False)}")
        if 'include' in config:
            print(f"   - Include patterns: {len(config['include'])}")
        if 'ignore' in config:
            custom = len(config['ignore'].get('customPatterns', []))
            print(f"   - Custom ignore patterns: {custom}")
        sys.exit(0)
    else:
        print("❌ Configuration has issues:")
        print()
        for error in validation_errors:
            if error.startswith("WARNING:"):
                print(f"   ⚠️  {error}")
            else:
                print(f"   - {error}")
        sys.exit(1)


if __name__ == '__main__':
    main()
