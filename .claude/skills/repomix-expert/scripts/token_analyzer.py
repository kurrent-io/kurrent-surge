#!/usr/bin/env python3
"""
Repomix Token Analyzer

Analyzes token distribution across files in a Repomix packed output.
Identifies largest consumers of tokens and provides optimization recommendations.

Usage:
    python3 token_analyzer.py [path/to/repomix-output.xml|json]

Supports XML and JSON output formats.
"""

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Dict, List, Tuple
from collections import defaultdict


class TokenStats:
    """Statistics about token usage."""

    def __init__(self):
        self.file_tokens: Dict[str, int] = {}
        self.total_tokens: int = 0
        self.total_files: int = 0

    def add_file(self, path: str, tokens: int):
        """Add file token count."""
        self.file_tokens[path] = tokens
        self.total_tokens += tokens
        self.total_files += 1

    def get_top_files(self, n: int = 10) -> List[Tuple[str, int, float]]:
        """Get top N files by token count with percentage."""
        sorted_files = sorted(
            self.file_tokens.items(),
            key=lambda x: x[1],
            reverse=True
        )
        return [
            (path, tokens, (tokens / self.total_tokens * 100) if self.total_tokens > 0 else 0)
            for path, tokens in sorted_files[:n]
        ]

    def get_directory_stats(self) -> Dict[str, Tuple[int, int]]:
        """Get token statistics by directory."""
        dir_stats = defaultdict(lambda: [0, 0])  # [tokens, file_count]

        for path, tokens in self.file_tokens.items():
            # Extract directory
            parts = Path(path).parts
            if len(parts) > 1:
                directory = str(Path(*parts[:-1]))
            else:
                directory = "/"

            dir_stats[directory][0] += tokens
            dir_stats[directory][1] += 1

        # Convert to tuples and sort by tokens
        return {
            dir: (tokens, count)
            for dir, (tokens, count) in dir_stats.items()
        }

    def get_extension_stats(self) -> Dict[str, Tuple[int, int]]:
        """Get token statistics by file extension."""
        ext_stats = defaultdict(lambda: [0, 0])  # [tokens, file_count]

        for path, tokens in self.file_tokens.items():
            ext = Path(path).suffix or "(no extension)"
            ext_stats[ext][0] += tokens
            ext_stats[ext][1] += 1

        return {
            ext: (tokens, count)
            for ext, (tokens, count) in ext_stats.items()
        }


def parse_xml(xml_path: Path) -> TokenStats:
    """Parse XML format Repomix output."""
    stats = TokenStats()

    try:
        tree = ET.parse(xml_path)
        root = tree.getroot()

        # Get total tokens from metrics if available
        metrics = root.find('metrics')
        if metrics is not None:
            total_tokens_elem = metrics.find('totalTokens')
            if total_tokens_elem is not None:
                stats.total_tokens = int(total_tokens_elem.text)

        # Parse files
        files = root.find('files')
        if files is None:
            print("❌ No files section found in XML")
            return stats

        for file_elem in files.findall('file'):
            path_elem = file_elem.find('path')
            if path_elem is None:
                continue

            path = path_elem.text

            # Try to get tokens from file element
            tokens_elem = file_elem.find('tokens')
            if tokens_elem is not None:
                tokens = int(tokens_elem.text)
            else:
                # Estimate from content if tokens not available
                content_elem = file_elem.find('content')
                if content_elem is not None:
                    # Rough estimation: 1 token per 4 characters
                    tokens = len(content_elem.text or "") // 4
                else:
                    tokens = 0

            stats.add_file(path, tokens)

    except Exception as e:
        print(f"❌ Error parsing XML: {e}")
        sys.exit(1)

    return stats


def parse_json(json_path: Path) -> TokenStats:
    """Parse JSON format Repomix output."""
    stats = TokenStats()

    try:
        with open(json_path, 'r', encoding='utf-8') as f:
            data = json.load(f)

        # Get metrics if available
        if 'metrics' in data:
            stats.total_tokens = data['metrics'].get('totalTokens', 0)

        # Parse files
        if 'files' not in data:
            print("❌ No files section found in JSON")
            return stats

        for file_data in data['files']:
            path = file_data.get('path', '')
            tokens = file_data.get('tokens')

            if tokens is None:
                # Estimate from content
                content = file_data.get('content', '')
                tokens = len(content) // 4  # Rough estimation

            stats.add_file(path, tokens)

    except Exception as e:
        print(f"❌ Error parsing JSON: {e}")
        sys.exit(1)

    return stats


def format_number(n: int) -> str:
    """Format large numbers with commas."""
    return f"{n:,}"


def print_top_files(stats: TokenStats, n: int = 20):
    """Print top N files by token count."""
    print(f"\n📊 Top {n} Files by Token Count")
    print("=" * 80)

    top_files = stats.get_top_files(n)
    if not top_files:
        print("No files found")
        return

    for i, (path, tokens, percentage) in enumerate(top_files, 1):
        bar_width = int(percentage / 2)  # Scale to 50 chars max
        bar = "█" * bar_width
        print(f"{i:2d}. {path}")
        print(f"    {format_number(tokens):>8s} tokens ({percentage:5.2f}%) {bar}")


def print_directory_stats(stats: TokenStats):
    """Print token statistics by directory."""
    print("\n📁 Token Distribution by Directory")
    print("=" * 80)

    dir_stats = stats.get_directory_stats()
    sorted_dirs = sorted(dir_stats.items(), key=lambda x: x[1][0], reverse=True)

    for directory, (tokens, file_count) in sorted_dirs[:15]:
        percentage = (tokens / stats.total_tokens * 100) if stats.total_tokens > 0 else 0
        avg_tokens = tokens // file_count if file_count > 0 else 0
        bar_width = int(percentage / 2)
        bar = "█" * bar_width

        print(f"\n{directory}")
        print(f"  {format_number(tokens):>8s} tokens ({percentage:5.2f}%) "
              f"- {file_count} files (avg: {format_number(avg_tokens)} tokens/file)")
        print(f"  {bar}")


def print_extension_stats(stats: TokenStats):
    """Print token statistics by file extension."""
    print("\n📄 Token Distribution by File Extension")
    print("=" * 80)

    ext_stats = stats.get_extension_stats()
    sorted_exts = sorted(ext_stats.items(), key=lambda x: x[1][0], reverse=True)

    for ext, (tokens, file_count) in sorted_exts:
        percentage = (tokens / stats.total_tokens * 100) if stats.total_tokens > 0 else 0
        avg_tokens = tokens // file_count if file_count > 0 else 0
        bar_width = int(percentage / 2)
        bar = "█" * bar_width

        print(f"\n{ext:20s} {format_number(tokens):>8s} tokens ({percentage:5.2f}%)")
        print(f"                     {file_count} files (avg: {format_number(avg_tokens)} tokens/file)")
        print(f"  {bar}")


def print_recommendations(stats: TokenStats):
    """Print optimization recommendations."""
    print("\n💡 Optimization Recommendations")
    print("=" * 80)

    top_files = stats.get_top_files(5)
    ext_stats = stats.get_extension_stats()

    recommendations = []

    # Check if top files dominate
    if top_files:
        top_5_percentage = sum(pct for _, _, pct in top_files)
        if top_5_percentage > 50:
            recommendations.append(
                f"⚠️  Top 5 files account for {top_5_percentage:.1f}% of tokens. "
                "Consider filtering these files or analyzing them separately."
            )

    # Check for large file extensions
    for ext, (tokens, _) in sorted(ext_stats.items(), key=lambda x: x[1][0], reverse=True)[:3]:
        percentage = (tokens / stats.total_tokens * 100) if stats.total_tokens > 0 else 0
        if percentage > 30:
            recommendations.append(
                f"⚠️  {ext} files account for {percentage:.1f}% of tokens. "
                "Consider using --compress or filtering these files."
            )

    # Check for compression opportunity
    total_percentage = 100
    if total_percentage > 0:
        recommendations.append(
            "💡 Enable Tree-sitter compression with --compress to reduce token usage by ~70%."
        )

    # Check for comment removal opportunity
    recommendations.append(
        "💡 Use --remove-comments to reduce token usage by 10-30%."
    )

    # Check for filtering opportunity
    if stats.total_files > 100:
        recommendations.append(
            f"💡 {stats.total_files} files detected. "
            "Use --include patterns to focus on specific files."
        )

    if recommendations:
        for rec in recommendations:
            print(f"\n{rec}")
    else:
        print("\n✅ Token distribution looks good. No specific recommendations.")


def print_summary(stats: TokenStats):
    """Print summary statistics."""
    print("\n📈 Summary")
    print("=" * 80)
    print(f"Total Files:  {format_number(stats.total_files)}")
    print(f"Total Tokens: {format_number(stats.total_tokens)}")

    if stats.total_files > 0:
        avg_tokens = stats.total_tokens // stats.total_files
        print(f"Average:      {format_number(avg_tokens)} tokens/file")

    # Estimated context usage
    if stats.total_tokens > 0:
        context_windows = {
            'Claude 3.5 Sonnet': 200_000,
            'GPT-4': 128_000,
            'GPT-3.5 Turbo': 16_000,
        }

        print("\n📊 Context Window Usage:")
        for model, window_size in context_windows.items():
            percentage = (stats.total_tokens / window_size * 100)
            if percentage <= 100:
                status = "✅"
            elif percentage <= 150:
                status = "⚠️"
            else:
                status = "❌"
            print(f"  {status} {model:20s}: {percentage:6.2f}% ({format_number(window_size)} tokens)")


def main():
    """Main entry point."""
    if len(sys.argv) < 2:
        print("❌ No input file specified")
        print("\nUsage: python3 token_analyzer.py <repomix-output.xml|json>")
        print("\nExample:")
        print("  python3 token_analyzer.py repomix-output.xml")
        print("  python3 token_analyzer.py packed-repo.json")
        sys.exit(1)

    input_path = Path(sys.argv[1])

    if not input_path.exists():
        print(f"❌ File not found: {input_path}")
        sys.exit(1)

    print(f"🔍 Analyzing: {input_path}")

    # Determine format and parse
    if input_path.suffix == '.xml':
        stats = parse_xml(input_path)
    elif input_path.suffix == '.json':
        stats = parse_json(input_path)
    else:
        print(f"❌ Unsupported format: {input_path.suffix}")
        print("   Supported formats: .xml, .json")
        sys.exit(1)

    if stats.total_files == 0:
        print("❌ No files found in output")
        sys.exit(1)

    # Print analysis
    print_summary(stats)
    print_top_files(stats, 20)
    print_directory_stats(stats)
    print_extension_stats(stats)
    print_recommendations(stats)

    print("\n" + "=" * 80)
    print("✅ Analysis complete")


if __name__ == '__main__':
    main()
