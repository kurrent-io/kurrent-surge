using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Resources;

namespace ResourceTools
{
    /// <summary>
    /// Utility for comparing .resx resource files.
    /// </summary>
    public static class ResxComparer
    {
        /// <summary>
        /// Compares two .resx files and returns a detailed comparison result.
        /// </summary>
        public static ComparisonResult Compare(string baseFile, string compareFile)
        {
            var baseResources = LoadResources(baseFile);
            var compareResources = LoadResources(compareFile);

            var result = new ComparisonResult
            {
                BaseFile = baseFile,
                CompareFile = compareFile,
                BaseKeyCount = baseResources.Count,
                CompareKeyCount = compareResources.Count
            };

            // Find missing keys (in base but not in compare)
            result.MissingKeys = baseResources.Keys.Except(compareResources.Keys).ToList();

            // Find extra keys (in compare but not in base)
            result.ExtraKeys = compareResources.Keys.Except(baseResources.Keys).ToList();

            // Find keys with different values
            foreach (var key in baseResources.Keys.Intersect(compareResources.Keys))
            {
                string baseValue = baseResources[key]?.ToString();
                string compareValue = compareResources[key]?.ToString();

                if (baseValue != compareValue)
                {
                    result.DifferentValues.Add(key, (baseValue, compareValue));
                }
            }

            return result;
        }

        /// <summary>
        /// Finds keys that exist in the base file but are missing in culture-specific files.
        /// </summary>
        public static Dictionary<string, List<string>> FindMissingTranslations(
            string baseFile,
            params string[] cultureFiles)
        {
            var baseKeys = GetResourceKeys(baseFile);
            var result = new Dictionary<string, List<string>>();

            foreach (var cultureFile in cultureFiles)
            {
                var cultureName = Path.GetFileNameWithoutExtension(cultureFile);
                var cultureKeys = GetResourceKeys(cultureFile);
                var missingKeys = baseKeys.Except(cultureKeys).ToList();

                if (missingKeys.Count > 0)
                {
                    result[cultureName] = missingKeys;
                }
            }

            return result;
        }

        /// <summary>
        /// Generates a translation completeness report.
        /// </summary>
        public static CompletenessReport GenerateCompletenessReport(
            string baseFile,
            params string[] cultureFiles)
        {
            var baseKeys = GetResourceKeys(baseFile);
            var report = new CompletenessReport
            {
                BaseFile = baseFile,
                TotalKeys = baseKeys.Count
            };

            foreach (var cultureFile in cultureFiles)
            {
                var cultureName = ExtractCultureFromFileName(cultureFile);
                var cultureKeys = GetResourceKeys(cultureFile);
                var missingKeys = baseKeys.Except(cultureKeys).ToList();

                var cultureReport = new CultureCompletenessReport
                {
                    Culture = cultureName,
                    TranslatedKeys = cultureKeys.Count,
                    MissingKeys = missingKeys.Count,
                    CompletionPercentage = (double)cultureKeys.Count / baseKeys.Count * 100,
                    MissingKeysList = missingKeys
                };

                report.CultureReports.Add(cultureReport);
            }

            return report;
        }

        /// <summary>
        /// Exports comparison results to CSV format.
        /// </summary>
        public static void ExportComparisonToCsv(
            ComparisonResult result,
            string outputPath)
        {
            using (var writer = new StreamWriter(outputPath))
            {
                writer.WriteLine("Type,Key,BaseValue,CompareValue");

                foreach (var key in result.MissingKeys)
                {
                    writer.WriteLine($"Missing,{EscapeCsv(key)},Present,Missing");
                }

                foreach (var key in result.ExtraKeys)
                {
                    writer.WriteLine($"Extra,{EscapeCsv(key)},Missing,Present");
                }

                foreach (var kvp in result.DifferentValues)
                {
                    writer.WriteLine($"Different,{EscapeCsv(kvp.Key)},{EscapeCsv(kvp.Value.BaseValue)},{EscapeCsv(kvp.Value.CompareValue)}");
                }
            }
        }

        /// <summary>
        /// Prints a formatted comparison report to console.
        /// </summary>
        public static void PrintComparison(ComparisonResult result)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("Resource File Comparison");
            Console.WriteLine("========================================");
            Console.WriteLine($"Base File:    {result.BaseFile} ({result.BaseKeyCount} keys)");
            Console.WriteLine($"Compare File: {result.CompareFile} ({result.CompareKeyCount} keys)");
            Console.WriteLine();

            if (result.MissingKeys.Count > 0)
            {
                Console.WriteLine($"Missing Keys ({result.MissingKeys.Count}):");
                foreach (var key in result.MissingKeys)
                {
                    Console.WriteLine($"  - {key}");
                }
                Console.WriteLine();
            }

            if (result.ExtraKeys.Count > 0)
            {
                Console.WriteLine($"Extra Keys ({result.ExtraKeys.Count}):");
                foreach (var key in result.ExtraKeys)
                {
                    Console.WriteLine($"  + {key}");
                }
                Console.WriteLine();
            }

            if (result.DifferentValues.Count > 0)
            {
                Console.WriteLine($"Different Values ({result.DifferentValues.Count}):");
                foreach (var kvp in result.DifferentValues)
                {
                    Console.WriteLine($"  {kvp.Key}:");
                    Console.WriteLine($"    Base:    {kvp.Value.BaseValue}");
                    Console.WriteLine($"    Compare: {kvp.Value.CompareValue}");
                }
                Console.WriteLine();
            }

            if (result.IsIdentical)
            {
                Console.WriteLine("✓ Files are identical");
            }
            else
            {
                Console.WriteLine($"✗ Files differ ({result.TotalDifferences} differences)");
            }
        }

        /// <summary>
        /// Prints a completeness report to console.
        /// </summary>
        public static void PrintCompletenessReport(CompletenessReport report)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("Translation Completeness Report");
            Console.WriteLine("========================================");
            Console.WriteLine($"Base File: {report.BaseFile} ({report.TotalKeys} keys)");
            Console.WriteLine();

            foreach (var cultureReport in report.CultureReports.OrderByDescending(c => c.CompletionPercentage))
            {
                Console.WriteLine($"Culture: {cultureReport.Culture}");
                Console.WriteLine($"  Translated: {cultureReport.TranslatedKeys} / {report.TotalKeys} ({cultureReport.CompletionPercentage:F1}%)");
                Console.WriteLine($"  Missing: {cultureReport.MissingKeys}");

                if (cultureReport.MissingKeysList.Count > 0 && cultureReport.MissingKeysList.Count <= 10)
                {
                    Console.WriteLine("  Missing keys:");
                    foreach (var key in cultureReport.MissingKeysList)
                    {
                        Console.WriteLine($"    - {key}");
                    }
                }

                Console.WriteLine();
            }
        }

        // Helper methods

        private static Dictionary<string, object> LoadResources(string resxPath)
        {
            var resources = new Dictionary<string, object>();

            using (var reader = new ResXResourceReader(resxPath))
            {
                foreach (DictionaryEntry entry in reader)
                {
                    resources[(string)entry.Key] = entry.Value;
                }
            }

            return resources;
        }

        private static HashSet<string> GetResourceKeys(string resxPath)
        {
            var keys = new HashSet<string>();

            using (var reader = new ResXResourceReader(resxPath))
            {
                foreach (DictionaryEntry entry in reader)
                {
                    keys.Add((string)entry.Key);
                }
            }

            return keys;
        }

        private static string ExtractCultureFromFileName(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var parts = fileName.Split('.');

            if (parts.Length >= 2)
            {
                return parts[parts.Length - 1];
            }

            return fileName;
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }
    }

    /// <summary>
    /// Result of comparing two resource files.
    /// </summary>
    public class ComparisonResult
    {
        public string BaseFile { get; set; }
        public string CompareFile { get; set; }
        public int BaseKeyCount { get; set; }
        public int CompareKeyCount { get; set; }
        public List<string> MissingKeys { get; set; } = new List<string>();
        public List<string> ExtraKeys { get; set; } = new List<string>();
        public Dictionary<string, (string BaseValue, string CompareValue)> DifferentValues { get; set; } = new Dictionary<string, (string, string)>();

        public int TotalDifferences => MissingKeys.Count + ExtraKeys.Count + DifferentValues.Count;
        public bool IsIdentical => TotalDifferences == 0;
    }

    /// <summary>
    /// Translation completeness report.
    /// </summary>
    public class CompletenessReport
    {
        public string BaseFile { get; set; }
        public int TotalKeys { get; set; }
        public List<CultureCompletenessReport> CultureReports { get; set; } = new List<CultureCompletenessReport>();
    }

    /// <summary>
    /// Completeness report for a specific culture.
    /// </summary>
    public class CultureCompletenessReport
    {
        public string Culture { get; set; }
        public int TranslatedKeys { get; set; }
        public int MissingKeys { get; set; }
        public double CompletionPercentage { get; set; }
        public List<string> MissingKeysList { get; set; } = new List<string>();
    }
}
