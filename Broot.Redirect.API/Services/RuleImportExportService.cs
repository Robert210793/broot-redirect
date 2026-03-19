using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using ClosedXML.Excel;
using Broot.Redirect.Core.Models;
using Broot.Redirect.API.Dtos;

namespace Broot.Redirect.API.Services
{
    /// <summary>
    /// Handles CSV and XLSX import/export for redirect rules.
    /// Complex collection fields (KeptQueryParams, StaticQueryParams, SearchAndReplace)
    /// are serialized as JSON strings in CSV/XLSX cells.
    /// Includes CSV injection protection on export.
    /// </summary>
    public static class RuleImportExportService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private static readonly Dictionary<string, string[]> ColumnMapping = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Id"] = new[] { "ID", "id" },
            ["Matcher"] = new[] { "Matcher", "matcher", "Quelle", "Source" },
            ["TargetUrl"] = new[] { "Target URL", "targetUrl", "TargetUrl", "Ziel", "Target" },
            ["RedirectType"] = new[] { "Type", "redirectType", "RedirectType", "Typ" },
            ["InfoText"] = new[] { "Info", "infoText", "InfoText", "Beschreibung" },
            ["AutoRedirect"] = new[] { "Auto Redirect", "autoRedirect", "AutoRedirect", "Automatisch" },
            ["DiscardQueryParams"] = new[] { "Discard Query Params", "discardQueryParams", "DiscardQueryParams", "Parameter entfernen" },
            ["ForwardQueryParams"] = new[] { "Keep Query Params", "forwardQueryParams", "ForwardQueryParams", "Parameter behalten" },
            ["KeptQueryParams"] = new[] { "Kept Query Params", "keptQueryParams", "KeptQueryParams", "Parameter Ausnahmen" },
            ["StaticQueryParams"] = new[] { "Static Query Params", "staticQueryParams", "StaticQueryParams", "Statische Parameter" },
            ["SearchAndReplace"] = new[] { "Search Replace", "searchAndReplace", "SearchAndReplace", "Suchen Ersetzen" },
            ["CreatedAt"] = new[] { "CreatedAt", "createdAt", "Created At", "Erstellt" }
        };

        public static byte[] GenerateCsv(IReadOnlyList<RedirectRule> rules)
        {
            using var memoryStream = new MemoryStream();
            using var writer = new StreamWriter(memoryStream, new UTF8Encoding(true));
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true
            });

            csv.WriteField("ID");
            csv.WriteField("Matcher");
            csv.WriteField("Target URL");
            csv.WriteField("Type");
            csv.WriteField("Info");
            csv.WriteField("Auto Redirect");
            csv.WriteField("Discard Query Params");
            csv.WriteField("Keep Query Params");
            csv.WriteField("Kept Query Params");
            csv.WriteField("Static Query Params");
            csv.WriteField("Search Replace");
            csv.WriteField("CreatedAt");

            csv.NextRecord();

            foreach (var rule in rules)
            {
                csv.WriteField(rule.Id.ToString());
                csv.WriteField(SanitizeForCsv(rule.Matcher));
                csv.WriteField(SanitizeForCsv(rule.TargetUrl));
                csv.WriteField(rule.RedirectType.ToString().ToLowerInvariant());
                csv.WriteField(SanitizeForCsv(rule.InfoText));
                csv.WriteField(rule.AutoRedirect ? "true" : "false");
                csv.WriteField(rule.DiscardQueryParams ? "true" : "false");
                csv.WriteField(rule.ForwardQueryParams ? "true" : "false");
                csv.WriteField(SerializeCollection(rule.KeptQueryParams));
                csv.WriteField(SerializeCollection(rule.StaticQueryParams));
                csv.WriteField(SerializeCollection(rule.SearchAndReplace));
                csv.WriteField(rule.CreatedAt.ToString("o"));

                csv.NextRecord();
            }

            writer.Flush();

            return memoryStream.ToArray();
        }

        public static byte[] GenerateXlsx(IReadOnlyList<RedirectRule> rules)
        {
            using var workbook = new XLWorkbook();

            var worksheet = workbook.Worksheets.Add("Rules");

            var headers = new[]
            {
                "ID", "Matcher", "Target URL", "Type", "Info",
                "Auto Redirect", "Discard Query Params", "Keep Query Params",
                "Kept Query Params", "Static Query Params", "Search Replace", "CreatedAt"
            };

            for (var column = 0; column < headers.Length; column++)
            {
                var cell = worksheet.Cell(1, column + 1);

                cell.Value = headers[column];
                cell.Style.Font.Bold = true;
            }

            for (var rowIndex = 0; rowIndex < rules.Count; rowIndex++)
            {
                var rule = rules[rowIndex];
                var row = rowIndex + 2;

                worksheet.Cell(row, 1).Value = rule.Id.ToString();
                worksheet.Cell(row, 2).Value = SanitizeForCsv(rule.Matcher);
                worksheet.Cell(row, 3).Value = SanitizeForCsv(rule.TargetUrl);
                worksheet.Cell(row, 4).Value = rule.RedirectType.ToString().ToLowerInvariant();
                worksheet.Cell(row, 5).Value = SanitizeForCsv(rule.InfoText);
                worksheet.Cell(row, 6).Value = rule.AutoRedirect;
                worksheet.Cell(row, 7).Value = rule.DiscardQueryParams;
                worksheet.Cell(row, 8).Value = rule.ForwardQueryParams;
                worksheet.Cell(row, 9).Value = SerializeCollection(rule.KeptQueryParams);
                worksheet.Cell(row, 10).Value = SerializeCollection(rule.StaticQueryParams);
                worksheet.Cell(row, 11).Value = SerializeCollection(rule.SearchAndReplace);
                worksheet.Cell(row, 12).Value = rule.CreatedAt.ToString("o");
            }

            worksheet.Columns().AdjustToContents(1, Math.Min(rules.Count + 1, 100));

            using var memoryStream = new MemoryStream();

            workbook.SaveAs(memoryStream);

            return memoryStream.ToArray();
        }

        public static List<ImportRuleEntry> ParseCsv(Stream fileStream)
        {
            using var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                HeaderValidated = null,
                MissingFieldFound = null,
                TrimOptions = TrimOptions.Trim
            });

            csv.Read();
            csv.ReadHeader();

            var headerRecord = csv.HeaderRecord ?? Array.Empty<string>();

            var entries = new List<ImportRuleEntry>();

            while (csv.Read())
            {
                var entry = MapRowToEntry(field => GetFieldValue(csv, headerRecord, field));

                entries.Add(entry);
            }

            return entries;
        }

        public static List<ImportRuleEntry> ParseXlsx(Stream fileStream)
        {
            using var workbook = new XLWorkbook(fileStream);

            var worksheet = workbook.Worksheets.First();

            var headerRow = worksheet.Row(1);
            var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

            var headers = new string[lastColumn];

            for (var column = 1; column <= lastColumn; column++)
            {
                headers[column - 1] = headerRow.Cell(column).GetString().Trim();
            }

            var entries = new List<ImportRuleEntry>();
            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

            for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
            {
                var row = worksheet.Row(rowNumber);

                if (row.IsEmpty())
                {
                    continue;
                }

                var entry = MapRowToEntry(field =>
                {
                    var columnIndex = FindColumnIndex(headers, field);

                    if (columnIndex < 0)
                    {
                        return null;
                    }

                    var cellValue = row.Cell(columnIndex + 1).GetString().Trim();

                    return string.IsNullOrEmpty(cellValue) ? null : cellValue;
                });

                entries.Add(entry);
            }

            return entries;
        }

        /// <summary>
        /// Maps a row (from either CSV or XLSX) to an ImportRuleEntry.
        /// The getValue function abstracts the difference between CSV and XLSX access.
        /// </summary>
        private static ImportRuleEntry MapRowToEntry(Func<string, string?> getValue)
        {
            var entry = new ImportRuleEntry();

            var idValue = getValue("Id");

            entry.Id = string.IsNullOrWhiteSpace(idValue) ? null : idValue.Trim();

            entry.Matcher = getValue("Matcher") ?? string.Empty;
            entry.TargetUrl = getValue("TargetUrl");
            entry.RedirectType = NormalizeRedirectType(getValue("RedirectType"));
            entry.InfoText = getValue("InfoText");
            entry.CreatedAt = getValue("CreatedAt");

            entry.AutoRedirect = ParseBool(getValue("AutoRedirect"));
            entry.DiscardQueryParams = ParseBool(getValue("DiscardQueryParams"));
            entry.ForwardQueryParams = ParseBool(getValue("ForwardQueryParams"));

            entry.KeptQueryParams = ParseJsonCollection<KeptQueryParam>(getValue("KeptQueryParams"));
            entry.StaticQueryParams = ParseJsonCollection<StaticQueryParam>(getValue("StaticQueryParams"));
            entry.SearchAndReplace = ParseJsonCollection<SearchAndReplaceEntry>(getValue("SearchAndReplace"));

            return entry;
        }

        /// <summary>
        /// Gets a field value from a CsvReader using flexible column mapping.
        /// Tries all known header variations for the given field name.
        /// </summary>
        private static string? GetFieldValue(CsvReader csv, string[] headers, string fieldName)
        {
            if (!ColumnMapping.TryGetValue(fieldName, out var variations))
            {
                return null;
            }

            foreach (var variation in variations)
            {
                var index = Array.FindIndex(headers, header =>
                    string.Equals(header, variation, StringComparison.OrdinalIgnoreCase));

                if (index >= 0)
                {
                    var value = csv.GetField(index);

                    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the column index in XLSX headers using flexible column mapping.
        /// </summary>
        private static int FindColumnIndex(string[] headers, string fieldName)
        {
            if (!ColumnMapping.TryGetValue(fieldName, out var variations))
            {
                return -1;
            }

            foreach (var variation in variations)
            {
                var index = Array.FindIndex(headers, header =>
                    string.Equals(header, variation, StringComparison.OrdinalIgnoreCase));

                if (index >= 0)
                {
                    return index;
                }
            }

            return -1;
        }

        private static string? NormalizeRedirectType(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var lower = value.Trim().ToLowerInvariant();

            if (lower.Contains("wild") || lower == "complete")
            {
                return "wildcard";
            }

            if (lower.Contains("part"))
            {
                return "partial";
            }

            if (lower.Contains("domain"))
            {
                return "domain";
            }

            if (lower.Contains("regex"))
            {
                return "regex";
            }

            return value.Trim();
        }

        private static bool? ParseBool(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var lower = value.Trim().ToLowerInvariant();

            if (lower is "true" or "1" or "yes" or "ja" or "on")
            {
                return true;
            }

            if (lower is "false" or "0" or "no" or "nein" or "off")
            {
                return false;
            }

            return null;
        }

        private static List<T>? ParseJsonCollection<T>(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<List<T>>(value, JsonOptions);

                return parsed;
            }
            catch
            {
                return null;
            }
        }

        private static string SerializeCollection<T>(IReadOnlyList<T>? collection)
        {
            if (collection == null || collection.Count == 0)
            {
                return string.Empty;
            }

            return JsonSerializer.Serialize(collection, JsonOptions);
        }

        /// <summary>
        /// Prevents CSV/Excel formula injection by prepending a single quote
        /// to values starting with =, +, -, or @.
        /// </summary>
        /// 
        private static string SanitizeForCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Length > 0 && (value[0] == '=' || value[0] == '+' || value[0] == '-' || value[0] == '@'))
            {
                return "'" + value;
            }

            return value;
        }
    }
}
