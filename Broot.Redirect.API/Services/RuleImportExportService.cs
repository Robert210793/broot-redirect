using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using ClosedXML.Excel;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Broot.Redirect.API.Dtos;
using Microsoft.AspNetCore.Http;

namespace Broot.Redirect.API.Services
{
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

        private static ImportRuleEntry MapRowToEntry(Func<string, string?> getValue)
        {
            var entry = new ImportRuleEntry();

            var idValue = getValue("Id");

            entry.Id = string.IsNullOrWhiteSpace(idValue) ? null : idValue.Trim();

            entry.Matcher = UnsanitizeCsvValue(getValue("Matcher")) ?? string.Empty;
            entry.TargetUrl = UnsanitizeCsvValue(getValue("TargetUrl"));
            entry.RedirectType = NormalizeRedirectType(getValue("RedirectType"));
            entry.InfoText = UnsanitizeCsvValue(getValue("InfoText"));
            entry.CreatedAt = getValue("CreatedAt");

            entry.AutoRedirect = ParseBool(getValue("AutoRedirect"));
            entry.DiscardQueryParams = ParseBool(getValue("DiscardQueryParams"));
            entry.ForwardQueryParams = ParseBool(getValue("ForwardQueryParams"));

            entry.KeptQueryParams = ParseJsonCollection<KeptQueryParam>(getValue("KeptQueryParams"));
            entry.StaticQueryParams = ParseJsonCollection<StaticQueryParam>(getValue("StaticQueryParams"));
            entry.SearchAndReplace = ParseJsonCollection<SearchAndReplaceEntry>(getValue("SearchAndReplace"));

            return entry;
        }

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

        private static string? UnsanitizeCsvValue(string? value)
        {
            if (value != null && value.Length >= 2 && value[0] == '\''
                && (value[1] == '=' || value[1] == '+' || value[1] == '-' || value[1] == '@'))
            {
                return value[1..];
            }

            return value;
        }

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

        // --- Shared import helpers ---

        public static async Task<(List<ImportRuleEntry>? Entries, string? Error)> ParseImportEntries(
            string contentType,
            IFormFileCollection? files,
            Stream body,
            bool supportJson = true)
        {
            if (contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            {
                return await ParseFromFile(files, supportJson);
            }

            try
            {
                var entries = await ParseJsonFromStreamAsync(body);
                return (entries, null);
            }
            catch (JsonException exception)
            {
                return (null, $"Invalid JSON: {exception.Message}");
            }
        }

        private static async Task<(List<ImportRuleEntry>? Entries, string? Error)> ParseFromFile(
            IFormFileCollection? files,
            bool supportJson)
        {
            var file = files?.FirstOrDefault();

            if (file == null || file.Length == 0)
            {
                return (null, "No file uploaded");
            }

            try
            {
                using var stream = file.OpenReadStream();

                var entries = await ParseStreamByExtension(stream, file.FileName, supportJson);

                return (entries, null);
            }
            catch (InvalidOperationException exception)
            {
                return (null, exception.Message);
            }
            catch (Exception exception)
            {
                return (null, $"Failed to parse file: {exception.Message}");
            }
        }

        public static async Task<List<ImportRuleEntry>> ParseStreamByExtension(
            Stream stream,
            string fileName,
            bool supportJson)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            return extension switch
            {
                ".csv" => ParseCsv(stream),
                ".xlsx" => ParseXlsx(stream),
                ".xls" => ParseXlsx(stream),
                ".json" when supportJson => await ParseJsonFromStreamAsync(stream),
                _ => throw new InvalidOperationException(supportJson
                    ? $"Unsupported file format: {extension}. Use .json, .csv, .xlsx, or .xls."
                    : $"Unsupported file format: {extension}. Use .csv, .xlsx, or .xls.")
            };
        }

        public static async Task<List<ImportRuleEntry>> ParseJsonFromStreamAsync(Stream stream)
        {
            return await JsonSerializer.DeserializeAsync<List<ImportRuleEntry>>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<ImportRuleEntry>();
        }

        public static (CreateRuleRequest? Request, string? Error) ValidateImportEntry(
            ImportRuleEntry entry,
            RuleValidationService validationService)
        {
            if (string.IsNullOrWhiteSpace(entry.Matcher))
            {
                return (null, "Matcher is required");
            }

            var redirectType = entry.RedirectType ?? "partial";

            if (!TryParseRedirectType(redirectType, out _))
            {
                return (null, $"Invalid redirect type: '{redirectType}'");
            }

            // Always normalize: unescape then re-encode to handle both raw and pre-encoded input
            var matcher = PercentEncodePreservingSlashes(entry.Matcher);
            var targetUrl = entry.TargetUrl != null ? PercentEncodePreservingSlashes(entry.TargetUrl) : null;

            var request = new CreateRuleRequest
            {
                Matcher = matcher,
                TargetUrl = targetUrl,
                RedirectType = redirectType,
                InfoText = entry.InfoText,
                AutoRedirect = entry.AutoRedirect ?? false,
                DiscardQueryParams = entry.DiscardQueryParams ?? false,
                ForwardQueryParams = entry.ForwardQueryParams ?? false,
                KeptQueryParams = entry.KeptQueryParams ?? new List<KeptQueryParam>(),
                StaticQueryParams = entry.StaticQueryParams ?? new List<StaticQueryParam>(),
                SearchAndReplace = entry.SearchAndReplace ?? new List<SearchAndReplaceEntry>()
            };

            var validationErrors = validationService.ValidateCreate(request);

            if (validationErrors.Count > 0)
            {
                return (null, validationErrors[0]);
            }

            return (request, null);
        }

        public static RedirectRule? ResolveExistingRule(
            ImportRuleEntry entry,
            IRuleCacheService cacheService,
            Dictionary<string, RedirectRule> matcherLookup)
        {
            if (!string.IsNullOrEmpty(entry.Id) && Guid.TryParse(entry.Id, out var parsedId))
            {
                var byId = cacheService.GetById(parsedId);

                if (byId != null)
                {
                    return byId;
                }
            }

            matcherLookup.TryGetValue(entry.Matcher, out var byMatcher);

            return byMatcher;
        }

        public static Dictionary<string, RedirectRule> BuildMatcherLookup(IReadOnlyList<RedirectRule> rules)
        {
            return rules
                .GroupBy(rule => rule.Matcher, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        public static bool TryParseRedirectType(string value, out RedirectType redirectType)
        {
            return Enum.TryParse(value, ignoreCase: true, out redirectType);
        }

        public static string PercentEncodePreservingSlashes(string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var parsedUri)
                && (parsedUri.Scheme == "http" || parsedUri.Scheme == "https"))
            {
                // Absolute URL: encode only the path segments, preserve scheme + authority + query + fragment
                var encodedPath = EncodePathSegments(parsedUri.AbsolutePath);

                var result = $"{parsedUri.Scheme}://{parsedUri.Authority}{encodedPath}";

                if (!string.IsNullOrEmpty(parsedUri.Query))
                {
                    result += parsedUri.Query;
                }

                if (!string.IsNullOrEmpty(parsedUri.Fragment))
                {
                    result += parsedUri.Fragment;
                }

                return result;
            }

            // Relative path: encode each segment
            return EncodePathSegments(value);
        }

        private static string EncodePathSegments(string path)
        {
            var segments = path.Split('/');

            for (var index = 0; index < segments.Length; index++)
            {
                string unescaped;

                try { unescaped = Uri.UnescapeDataString(segments[index]); }
                catch { unescaped = segments[index]; }

                // Uri.EscapeDataString encodes everything except unreserved chars (RFC 3986).
                // Path segments also allow sub-delims, ":" and "@", so restore those.
                segments[index] = Uri.EscapeDataString(unescaped)
                    .Replace("%3A", ":")
                    .Replace("%40", "@");
            }

            return string.Join('/', segments);
        }

        public static IEnumerable<RedirectRule> ApplySearch(IEnumerable<RedirectRule> rules, string? search)
        {
            var cleanSearch = search?.Trim();

            if (string.IsNullOrEmpty(cleanSearch))
            {
                return rules;
            }

            return rules.Where(rule =>
                (rule.Matcher?.Contains(cleanSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (rule.TargetUrl?.Contains(cleanSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (rule.InfoText?.Contains(cleanSearch, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        public static IEnumerable<RedirectRule> ApplySorting(
            IEnumerable<RedirectRule> rules,
            string sortBy,
            string sortOrder)
        {
            return sortBy.ToLowerInvariant() switch
            {
                "matcher" => sortOrder == "asc"
                    ? rules.OrderBy(rule => rule.Matcher, StringComparer.OrdinalIgnoreCase)
                    : rules.OrderByDescending(rule => rule.Matcher, StringComparer.OrdinalIgnoreCase),

                "targeturl" => sortOrder == "asc"
                    ? rules.OrderBy(rule => rule.TargetUrl, StringComparer.OrdinalIgnoreCase)
                    : rules.OrderByDescending(rule => rule.TargetUrl, StringComparer.OrdinalIgnoreCase),

                "redirecttype" => sortOrder == "asc"
                    ? rules.OrderBy(rule => rule.RedirectType.ToString())
                    : rules.OrderByDescending(rule => rule.RedirectType.ToString()),

                _ => sortOrder == "asc"
                    ? rules.OrderBy(rule => rule.CreatedAt)
                    : rules.OrderByDescending(rule => rule.CreatedAt)
            };
        }
    }
}