using System.Text;
using System.Text.Json;
using Broot.Redirect.API.Dtos;
using Broot.Redirect.API.Services;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.API.Services
{
    public class RuleImportExportServiceTests
    {
        private static RedirectRule CreateRule(
            string matcher = "/path",
            RedirectType type = RedirectType.Wildcard,
            string targetUrl = "https://new.com/path")
        {
            return new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = matcher,
                TargetUrl = targetUrl,
                RedirectType = type,
                InfoText = "Test rule",
                AutoRedirect = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        public class GenerateCsvTests
        {
            [Fact]
            public void GenerateCsv_ValidRules_ProducesUtf8CsvWithBomAndCorrectHeaders()
            {
                var rules = new List<RedirectRule> { CreateRule() };

                var bytes = RuleImportExportService.GenerateCsv(rules);

                bytes[0].Should().Be(0xEF);
                bytes[1].Should().Be(0xBB);
                bytes[2].Should().Be(0xBF);

                var content = Encoding.UTF8.GetString(bytes);

                content.Should().Contain("ID");
                content.Should().Contain("Matcher");
                content.Should().Contain("Target URL");
                content.Should().Contain("Type");
            }

            [Fact]
            public void GenerateCsv_FieldsWithCommasAndQuotes_RoundTripsCorrectly()
            {
                var rule = new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "/path",
                    TargetUrl = "https://new.com/path",
                    RedirectType = RedirectType.Wildcard,
                    InfoText = "Contains \"quotes\" and, commas",
                    CreatedAt = DateTimeOffset.UtcNow
                };

                var bytes = RuleImportExportService.GenerateCsv(new[] { rule });

                using var stream = new MemoryStream(bytes);

                var entries = RuleImportExportService.ParseCsv(stream);

                entries.Should().HaveCount(1);
                entries[0].InfoText.Should().Be("Contains \"quotes\" and, commas");
            }
        }

        public class GenerateXlsxTests
        {
            [Fact]
            public void GenerateXlsx_ValidRules_ProducesXlsxWithRows()
            {
                var rule = CreateRule();
                var rules = new List<RedirectRule> { rule };

                var bytes = RuleImportExportService.GenerateXlsx(rules);

                using var stream = new MemoryStream(bytes);
                using var workbook = new XLWorkbook(stream);

                var worksheet = workbook.Worksheets.First();

                worksheet.Cell(1, 1).GetString().Should().Be("ID");
                worksheet.Cell(1, 2).GetString().Should().Be("Matcher");

                worksheet.Cell(2, 2).GetString().Should().Contain(rule.Matcher);
            }
        }

        public class ParseCsvTests
        {
            [Fact]
            public void ParseCsv_ValidCsv_ReturnsRules()
            {
                var csvContent = "Matcher,Target URL,Type\r\n/old-page,https://new.com/page,wildcard\r\n";

                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

                var entries = RuleImportExportService.ParseCsv(stream);

                entries.Should().HaveCount(1);
                entries[0].Matcher.Should().Be("/old-page");
                entries[0].TargetUrl.Should().Be("https://new.com/page");
                entries[0].RedirectType.Should().Be("wildcard");
            }

            [Fact]
            public void ParseCsv_GermanHeaders_MapsCorrectly()
            {
                var csvContent = "Quelle,Ziel,Typ\r\n/old-page,https://new.com/page,wildcard\r\n";

                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

                var entries = RuleImportExportService.ParseCsv(stream);

                entries.Should().HaveCount(1);
                entries[0].Matcher.Should().Be("/old-page");
                entries[0].TargetUrl.Should().Be("https://new.com/page");
                entries[0].RedirectType.Should().Be("wildcard");
            }

            [Fact]
            public void ParseCsv_EnglishHeaders_MapsCorrectly()
            {
                var csvContent = "Matcher,Target URL,Type,Auto Redirect\r\n/path,https://new.com,partial,true\r\n";

                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

                var entries = RuleImportExportService.ParseCsv(stream);

                entries.Should().HaveCount(1);
                entries[0].Matcher.Should().Be("/path");
                entries[0].RedirectType.Should().Be("partial");
                entries[0].AutoRedirect.Should().BeTrue();
            }

            [Fact]
            public void ParseCsv_ComplexJsonFields_DeserializesCorrectly()
            {
                var keptJson = "[{\"keyPattern\":\"utm_.*\",\"skipEncoding\":false}]";
                var staticJson = "[{\"key\":\"source\",\"value\":\"redirect\",\"skipEncoding\":false}]";
                var replaceJson = "[{\"search\":\"old\",\"replace\":\"new\",\"caseSensitive\":false}]";

                var csvContent =
                    "Matcher,Target URL,Type,Kept Query Params,Static Query Params,Search Replace\r\n" +
                    $"/old,https://new.com,wildcard,\"{keptJson.Replace("\"", "\"\"")}\",\"{staticJson.Replace("\"", "\"\"")}\",\"{replaceJson.Replace("\"", "\"\"")}\"\r\n";

                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

                var entries = RuleImportExportService.ParseCsv(stream);

                entries.Should().HaveCount(1);
                entries[0].KeptQueryParams.Should().NotBeNull();
                entries[0].KeptQueryParams!.Should().HaveCount(1);
                entries[0].KeptQueryParams![0].KeyPattern.Should().Be("utm_.*");
                entries[0].StaticQueryParams.Should().NotBeNull();
                entries[0].StaticQueryParams!.Should().HaveCount(1);
                entries[0].StaticQueryParams![0].Key.Should().Be("source");
                entries[0].SearchAndReplace.Should().NotBeNull();
                entries[0].SearchAndReplace!.Should().HaveCount(1);
                entries[0].SearchAndReplace![0].Search.Should().Be("old");
            }
        }

        public class ParseXlsxTests
        {
            [Fact]
            public void ParseXlsx_ValidXlsx_ReturnsRules()
            {
                var rules = new List<RedirectRule> { CreateRule() };
                var xlsxBytes = RuleImportExportService.GenerateXlsx(rules);

                using var stream = new MemoryStream(xlsxBytes);

                var entries = RuleImportExportService.ParseXlsx(stream);

                entries.Should().HaveCount(1);
                entries[0].Matcher.Should().Contain(rules[0].Matcher);
            }

            [Fact]
            public void ParseXlsx_EmptyRowsSkipped()
            {
                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add("Rules");

                ws.Cell(1, 1).Value = "Matcher";
                ws.Cell(1, 2).Value = "Target URL";
                ws.Cell(1, 3).Value = "Type";
                ws.Cell(2, 1).Value = "/path";
                ws.Cell(2, 2).Value = "https://new.com";
                ws.Cell(2, 3).Value = "wildcard";
                // Row 3 is empty
                ws.Cell(4, 1).Value = "/path2";
                ws.Cell(4, 2).Value = "https://new2.com";
                ws.Cell(4, 3).Value = "partial";

                using var ms = new MemoryStream();
                workbook.SaveAs(ms);
                ms.Position = 0;

                var entries = RuleImportExportService.ParseXlsx(ms);

                entries.Should().HaveCount(2);
            }
        }

        public class NormalizeRedirectTypeTests
        {
            [Theory]
            [InlineData("wildcard", "wildcard")]
            [InlineData("Wildcard", "wildcard")]
            [InlineData("complete", "wildcard")]
            [InlineData("partial", "partial")]
            [InlineData("Partial Match", "partial")]
            [InlineData("domain", "domain")]
            [InlineData("Domain Replace", "domain")]
            [InlineData("regex", "regex")]
            [InlineData("Regex Pattern", "regex")]
            public void ParseCsv_RedirectTypeVariants_NormalizedCorrectly(string input, string expected)
            {
                var csvContent = $"Matcher,Target URL,Type\r\n/path,https://new.com,{input}\r\n";
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

                var entries = RuleImportExportService.ParseCsv(stream);

                entries[0].RedirectType.Should().Be(expected);
            }
        }

        public class ParseBoolTests
        {
            [Theory]
            [InlineData("true", true)]
            [InlineData("1", true)]
            [InlineData("yes", true)]
            [InlineData("ja", true)]
            [InlineData("on", true)]
            [InlineData("false", false)]
            [InlineData("0", false)]
            [InlineData("no", false)]
            [InlineData("nein", false)]
            [InlineData("off", false)]
            public void ParseCsv_BoolVariants_ParsedCorrectly(string value, bool expected)
            {
                var csvContent = $"Matcher,Target URL,Type,Auto Redirect\r\n/path,https://new.com,wildcard,{value}\r\n";
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

                var entries = RuleImportExportService.ParseCsv(stream);

                entries[0].AutoRedirect.Should().Be(expected);
            }

            [Fact]
            public void ParseCsv_EmptyBoolField_ReturnsNull()
            {
                var csvContent = "Matcher,Target URL,Type,Auto Redirect\r\n/path,https://new.com,wildcard,\r\n";
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

                var entries = RuleImportExportService.ParseCsv(stream);

                entries[0].AutoRedirect.Should().BeNull();
            }
        }

        public class SanitizationTests
        {
            [Fact]
            public void GenerateCsv_FormulaInjectionMatcher_SanitizedWithLeadingQuote()
            {
                var rule = new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "=cmd|'/C calc'!A1",
                    TargetUrl = "/safe",
                    RedirectType = RedirectType.Partial,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                var bytes = RuleImportExportService.GenerateCsv(new[] { rule });

                // Round-trip should unsanitize
                using var stream = new MemoryStream(bytes);
                var entries = RuleImportExportService.ParseCsv(stream);

                entries[0].Matcher.Should().Be("=cmd|'/C calc'!A1");
            }

            [Fact]
            public void GenerateCsv_PlusPrefix_SanitizedAndRoundTrips()
            {
                var rule = new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "+dangerous",
                    TargetUrl = "/safe",
                    RedirectType = RedirectType.Partial,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                var bytes = RuleImportExportService.GenerateCsv(new[] { rule });
                using var stream = new MemoryStream(bytes);
                var entries = RuleImportExportService.ParseCsv(stream);

                entries[0].Matcher.Should().Be("+dangerous");
            }

            [Fact]
            public void GenerateCsv_NormalValue_NotSanitized()
            {
                var rule = new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "/normal-path",
                    TargetUrl = "/target",
                    RedirectType = RedirectType.Partial,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                var bytes = RuleImportExportService.GenerateCsv(new[] { rule });
                var content = Encoding.UTF8.GetString(bytes);

                content.Should().Contain("/normal-path");
                content.Should().NotContain("'/normal-path");
            }
        }

        public class JsonCollectionTests
        {
            [Fact]
            public void ParseCsv_InvalidJsonCollection_ReturnsNull()
            {
                var csvContent = "Matcher,Target URL,Type,Kept Query Params\r\n/path,https://new.com,wildcard,not-valid-json\r\n";
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

                var entries = RuleImportExportService.ParseCsv(stream);

                entries[0].KeptQueryParams.Should().BeNull();
            }

            [Fact]
            public void ParseCsv_EmptyJsonCollection_ReturnsNull()
            {
                var csvContent = "Matcher,Target URL,Type,Kept Query Params\r\n/path,https://new.com,wildcard,\r\n";
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

                var entries = RuleImportExportService.ParseCsv(stream);

                entries[0].KeptQueryParams.Should().BeNull();
            }
        }

        public class CsvRoundTripTests
        {
            [Fact]
            public void GenerateAndParseCsv_FullRoundTrip_PreservesAllFields()
            {
                var rule = new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "/round-trip",
                    TargetUrl = "https://new.com/page",
                    RedirectType = RedirectType.Wildcard,
                    InfoText = "Test info",
                    AutoRedirect = true,
                    DiscardQueryParams = true,
                    ForwardQueryParams = false,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                var bytes = RuleImportExportService.GenerateCsv(new[] { rule });
                using var stream = new MemoryStream(bytes);
                var entries = RuleImportExportService.ParseCsv(stream);

                entries.Should().HaveCount(1);
                entries[0].Matcher.Should().Be("/round-trip");
                entries[0].TargetUrl.Should().Be("https://new.com/page");
                entries[0].RedirectType.Should().Be("wildcard");
                entries[0].InfoText.Should().Be("Test info");
                entries[0].AutoRedirect.Should().BeTrue();
                entries[0].DiscardQueryParams.Should().BeTrue();
                entries[0].ForwardQueryParams.Should().BeFalse();
                entries[0].Id.Should().Be(rule.Id.ToString());
            }
        }

        public class ParseImportEntriesTests
        {
            [Fact]
            public async Task ParseImportEntries_JsonBody_ParsesEntries()
            {
                var entries = new List<ImportRuleEntry>
                {
                    new ImportRuleEntry { Matcher = "/test", TargetUrl = "https://new.com" }
                };

                var json = JsonSerializer.Serialize(entries);
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

                var (result, error) = await RuleImportExportService.ParseImportEntries(
                    "application/json", null, stream);

                error.Should().BeNull();
                result.Should().HaveCount(1);
                result![0].Matcher.Should().Be("/test");
            }

            [Fact]
            public async Task ParseImportEntries_InvalidJson_ReturnsError()
            {
                var stream = new MemoryStream(Encoding.UTF8.GetBytes("not json"));

                var (result, error) = await RuleImportExportService.ParseImportEntries(
                    "application/json", null, stream);

                error.Should().Contain("Invalid JSON");
                result.Should().BeNull();
            }

            [Fact]
            public async Task ParseImportEntries_MultipartNoFile_ReturnsError()
            {
                var files = new FormFileCollection();

                var (result, error) = await RuleImportExportService.ParseImportEntries(
                    "multipart/form-data", files, Stream.Null);

                error.Should().Be("No file uploaded");
                result.Should().BeNull();
            }

            [Fact]
            public async Task ParseImportEntries_MultipartCsvFile_ParsesEntries()
            {
                var csvContent = "Matcher,Target URL,Type\n/old,https://new.com,partial\n";
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
                var file = new FormFile(stream, 0, stream.Length, "file", "rules.csv");

                var files = new FormFileCollection { file };

                var (result, error) = await RuleImportExportService.ParseImportEntries(
                    "multipart/form-data", files, Stream.Null);

                error.Should().BeNull();
                result.Should().HaveCount(1);
                result![0].Matcher.Should().Be("/old");
            }

            [Fact]
            public async Task ParseImportEntries_UnsupportedExtension_ReturnsError()
            {
                var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));
                var file = new FormFile(stream, 0, stream.Length, "file", "rules.txt");

                var files = new FormFileCollection { file };

                var (result, error) = await RuleImportExportService.ParseImportEntries(
                    "multipart/form-data", files, Stream.Null);

                error.Should().Contain("Unsupported file format");
                result.Should().BeNull();
            }

            [Fact]
            public async Task ParseImportEntries_MultipartJsonFile_ParsesWhenSupported()
            {
                var entries = new List<ImportRuleEntry>
                {
                    new ImportRuleEntry { Matcher = "/json-file" }
                };

                var json = JsonSerializer.Serialize(entries);
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                var file = new FormFile(stream, 0, stream.Length, "file", "rules.json");

                var files = new FormFileCollection { file };

                var (result, error) = await RuleImportExportService.ParseImportEntries(
                    "multipart/form-data", files, Stream.Null, supportJson: true);

                error.Should().BeNull();
                result.Should().HaveCount(1);
            }
        }

        public class ValidateImportEntryTests
        {
            private readonly RuleValidationService _validationService = new();

            [Fact]
            public void ValidateImportEntry_ValidEntry_ReturnsRequest()
            {
                var entry = new ImportRuleEntry
                {
                    Matcher = "/old-page",
                    TargetUrl = "https://new.com",
                    RedirectType = "wildcard"
                };

                var (request, error) = RuleImportExportService.ValidateImportEntry(entry, false, _validationService);

                error.Should().BeNull();
                request.Should().NotBeNull();
                request!.Matcher.Should().Be("/old-page");
            }

            [Fact]
            public void ValidateImportEntry_EmptyMatcher_ReturnsError()
            {
                var entry = new ImportRuleEntry { Matcher = "", TargetUrl = "https://new.com" };

                var (request, error) = RuleImportExportService.ValidateImportEntry(entry, false, _validationService);

                error.Should().Be("Matcher is required");
                request.Should().BeNull();
            }

            [Fact]
            public void ValidateImportEntry_InvalidRedirectType_ReturnsError()
            {
                var entry = new ImportRuleEntry
                {
                    Matcher = "/test",
                    RedirectType = "bogus"
                };

                var (request, error) = RuleImportExportService.ValidateImportEntry(entry, false, _validationService);

                error.Should().Contain("Invalid redirect type");
                request.Should().BeNull();
            }

            [Fact]
            public void ValidateImportEntry_NullRedirectType_DefaultsToPartial()
            {
                var entry = new ImportRuleEntry
                {
                    Matcher = "/test",
                    TargetUrl = "/new",
                    RedirectType = null
                };

                var (request, error) = RuleImportExportService.ValidateImportEntry(entry, false, _validationService);

                error.Should().BeNull();
                request!.RedirectType.Should().Be("partial");
            }

            [Fact]
            public void ValidateImportEntry_EncodeUrls_EncodesMatcherAndTarget()
            {
                var entry = new ImportRuleEntry
                {
                    Matcher = "/path with spaces",
                    TargetUrl = "/target with spaces",
                    RedirectType = "partial"
                };

                var (request, error) = RuleImportExportService.ValidateImportEntry(entry, true, _validationService);

                error.Should().BeNull();
                request!.Matcher.Should().Contain("path%20with%20spaces");
                request.TargetUrl.Should().Contain("target%20with%20spaces");
            }

            [Fact]
            public void ValidateImportEntry_ValidationServiceRejectsEntry_ReturnsError()
            {
                var entry = new ImportRuleEntry
                {
                    Matcher = "/valid",
                    TargetUrl = "no-protocol",
                    RedirectType = "wildcard"
                };

                var (request, error) = RuleImportExportService.ValidateImportEntry(entry, false, _validationService);

                error.Should().NotBeNull();
                request.Should().BeNull();
            }

            [Fact]
            public void ValidateImportEntry_NullCollections_DefaultToEmpty()
            {
                var entry = new ImportRuleEntry
                {
                    Matcher = "/test",
                    TargetUrl = "/new",
                    RedirectType = "partial",
                    KeptQueryParams = null,
                    StaticQueryParams = null,
                    SearchAndReplace = null
                };

                var (request, error) = RuleImportExportService.ValidateImportEntry(entry, false, _validationService);

                error.Should().BeNull();
                request!.KeptQueryParams.Should().BeEmpty();
                request.StaticQueryParams.Should().BeEmpty();
                request.SearchAndReplace.Should().BeEmpty();
            }
        }

        public class ResolveExistingRuleTests
        {
            [Fact]
            public void ResolveExistingRule_ById_ReturnsRule()
            {
                var ruleId = Guid.NewGuid();
                var existingRule = new RedirectRule { Id = ruleId, Matcher = "/existing" };

                var cacheService = Substitute.For<IRuleCacheService>();
                cacheService.GetById(ruleId).Returns(existingRule);

                var entry = new ImportRuleEntry { Id = ruleId.ToString(), Matcher = "/existing" };
                var lookup = new Dictionary<string, RedirectRule>(StringComparer.OrdinalIgnoreCase);

                var result = RuleImportExportService.ResolveExistingRule(entry, cacheService, lookup);

                result.Should().Be(existingRule);
            }

            [Fact]
            public void ResolveExistingRule_ByMatcher_ReturnsRule()
            {
                var existingRule = new RedirectRule { Id = Guid.NewGuid(), Matcher = "/existing" };

                var cacheService = Substitute.For<IRuleCacheService>();
                var lookup = new Dictionary<string, RedirectRule>(StringComparer.OrdinalIgnoreCase)
                {
                    ["/existing"] = existingRule
                };

                var entry = new ImportRuleEntry { Matcher = "/existing" };

                var result = RuleImportExportService.ResolveExistingRule(entry, cacheService, lookup);

                result.Should().Be(existingRule);
            }

            [Fact]
            public void ResolveExistingRule_NoMatch_ReturnsNull()
            {
                var cacheService = Substitute.For<IRuleCacheService>();
                var lookup = new Dictionary<string, RedirectRule>(StringComparer.OrdinalIgnoreCase);

                var entry = new ImportRuleEntry { Matcher = "/new-rule" };

                var result = RuleImportExportService.ResolveExistingRule(entry, cacheService, lookup);

                result.Should().BeNull();
            }

            [Fact]
            public void ResolveExistingRule_InvalidGuid_FallsBackToMatcher()
            {
                var existingRule = new RedirectRule { Id = Guid.NewGuid(), Matcher = "/existing" };

                var cacheService = Substitute.For<IRuleCacheService>();
                var lookup = new Dictionary<string, RedirectRule>(StringComparer.OrdinalIgnoreCase)
                {
                    ["/existing"] = existingRule
                };

                var entry = new ImportRuleEntry { Id = "not-a-guid", Matcher = "/existing" };

                var result = RuleImportExportService.ResolveExistingRule(entry, cacheService, lookup);

                result.Should().Be(existingRule);
            }
        }

        public class BuildMatcherLookupTests
        {
            [Fact]
            public void BuildMatcherLookup_ReturnsFirstMatchPerMatcher()
            {
                var rule1 = new RedirectRule { Id = Guid.NewGuid(), Matcher = "/path" };
                var rule2 = new RedirectRule { Id = Guid.NewGuid(), Matcher = "/path" };

                var lookup = RuleImportExportService.BuildMatcherLookup(new[] { rule1, rule2 });

                lookup.Should().ContainKey("/path");
                lookup["/path"].Should().Be(rule1);
            }

            [Fact]
            public void BuildMatcherLookup_CaseInsensitive()
            {
                var rule = new RedirectRule { Id = Guid.NewGuid(), Matcher = "/Path" };

                var lookup = RuleImportExportService.BuildMatcherLookup(new[] { rule });

                lookup.Should().ContainKey("/path");
            }
        }

        public class ApplySearchAndSortingTests
        {
            private static readonly List<RedirectRule> TestRules = new()
            {
                new RedirectRule { Matcher = "/beta", TargetUrl = "https://b.com", InfoText = "Beta info", RedirectType = RedirectType.Partial, CreatedAt = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                new RedirectRule { Matcher = "/alpha", TargetUrl = "https://a.com", InfoText = "Alpha info", RedirectType = RedirectType.Wildcard, CreatedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                new RedirectRule { Matcher = "/gamma", TargetUrl = "https://g.com", InfoText = "Gamma info", RedirectType = RedirectType.Domain, CreatedAt = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero) }
            };

            [Fact]
            public void ApplySearch_MatchesMatcher()
            {
                var result = RuleImportExportService.ApplySearch(TestRules, "alpha").ToList();

                result.Should().HaveCount(1);
                result[0].Matcher.Should().Be("/alpha");
            }

            [Fact]
            public void ApplySearch_MatchesTargetUrl()
            {
                var result = RuleImportExportService.ApplySearch(TestRules, "b.com").ToList();

                result.Should().HaveCount(1);
                result[0].Matcher.Should().Be("/beta");
            }

            [Fact]
            public void ApplySearch_MatchesInfoText()
            {
                var result = RuleImportExportService.ApplySearch(TestRules, "Gamma info").ToList();

                result.Should().HaveCount(1);
                result[0].Matcher.Should().Be("/gamma");
            }

            [Fact]
            public void ApplySearch_NullOrEmpty_ReturnsAll()
            {
                RuleImportExportService.ApplySearch(TestRules, null).Should().HaveCount(3);
                RuleImportExportService.ApplySearch(TestRules, "").Should().HaveCount(3);
                RuleImportExportService.ApplySearch(TestRules, "  ").Should().HaveCount(3);
            }

            [Fact]
            public void ApplySorting_ByMatcherAsc()
            {
                var result = RuleImportExportService.ApplySorting(TestRules, "matcher", "asc").ToList();

                result[0].Matcher.Should().Be("/alpha");
                result[2].Matcher.Should().Be("/gamma");
            }

            [Fact]
            public void ApplySorting_ByMatcherDesc()
            {
                var result = RuleImportExportService.ApplySorting(TestRules, "matcher", "desc").ToList();

                result[0].Matcher.Should().Be("/gamma");
            }

            [Fact]
            public void ApplySorting_ByTargetUrlAsc()
            {
                var result = RuleImportExportService.ApplySorting(TestRules, "targeturl", "asc").ToList();

                result[0].TargetUrl.Should().Be("https://a.com");
            }

            [Fact]
            public void ApplySorting_ByRedirectTypeDesc()
            {
                var result = RuleImportExportService.ApplySorting(TestRules, "redirecttype", "desc").ToList();

                result[0].RedirectType.Should().Be(RedirectType.Wildcard);
            }

            [Fact]
            public void ApplySorting_ByCreatedAtAsc()
            {
                var result = RuleImportExportService.ApplySorting(TestRules, "createdAt", "asc").ToList();

                result[0].Matcher.Should().Be("/gamma");
            }

            [Fact]
            public void ApplySorting_UnknownField_DefaultsToCreatedAt()
            {
                var result = RuleImportExportService.ApplySorting(TestRules, "unknown", "asc").ToList();

                result[0].Matcher.Should().Be("/gamma");
            }
        }

        public class ParseStreamByExtensionTests
        {
            [Fact]
            public async Task ParseStreamByExtension_CsvFile_ParsesCorrectly()
            {
                var csv = "Matcher,Target URL,Type\n/test,https://new.com,partial\n";
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

                var result = await RuleImportExportService.ParseStreamByExtension(stream, "rules.csv", true);

                result.Should().HaveCount(1);
                result[0].Matcher.Should().Be("/test");
            }

            [Fact]
            public async Task ParseStreamByExtension_JsonFile_ParsesCorrectly()
            {
                var entries = new List<ImportRuleEntry>
                {
                    new ImportRuleEntry { Matcher = "/json-test" }
                };

                var json = JsonSerializer.Serialize(entries);
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

                var result = await RuleImportExportService.ParseStreamByExtension(stream, "rules.json", true);

                result.Should().HaveCount(1);
                result[0].Matcher.Should().Be("/json-test");
            }

            [Fact]
            public async Task ParseStreamByExtension_JsonNotSupported_Throws()
            {
                var stream = new MemoryStream(Encoding.UTF8.GetBytes("[]"));

                Func<Task> act = () => RuleImportExportService.ParseStreamByExtension(stream, "rules.json", false);

                await act.Should().ThrowAsync<InvalidOperationException>()
                    .WithMessage("*Unsupported file format*");
            }

            [Fact]
            public async Task ParseStreamByExtension_UnsupportedExtension_Throws()
            {
                var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));

                Func<Task> act = () => RuleImportExportService.ParseStreamByExtension(stream, "rules.xml", true);

                await act.Should().ThrowAsync<InvalidOperationException>()
                    .WithMessage("*Unsupported file format*");
            }

            [Fact]
            public async Task ParseStreamByExtension_XlsExtension_ParsesAsXlsx()
            {
                // Generate a valid xlsx, save as .xls extension scenario
                var rule = new RedirectRule
                {
                    Id = Guid.NewGuid(),
                    Matcher = "/xls-test",
                    TargetUrl = "https://new.com",
                    RedirectType = RedirectType.Partial,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                var xlsxBytes = RuleImportExportService.GenerateXlsx(new[] { rule });
                var stream = new MemoryStream(xlsxBytes);

                var result = await RuleImportExportService.ParseStreamByExtension(stream, "rules.xls", true);

                result.Should().HaveCount(1);
                result[0].Matcher.Should().Be("/xls-test");
            }
        }

        public class HelperMethodTests
        {
            [Fact]
            public void TryParseRedirectType_Valid_ReturnsTrue()
            {
                RuleImportExportService.TryParseRedirectType("partial", out var result).Should().BeTrue();
                result.Should().Be(RedirectType.Partial);
            }

            [Fact]
            public void TryParseRedirectType_Invalid_ReturnsFalse()
            {
                RuleImportExportService.TryParseRedirectType("bogus", out _).Should().BeFalse();
            }

            [Fact]
            public void PercentEncodePreservingSlashes_EncodesSpaces()
            {
                var result = RuleImportExportService.PercentEncodePreservingSlashes("/path with spaces/file name");

                result.Should().Be("/path%20with%20spaces/file%20name");
            }

            [Fact]
            public void PercentEncodePreservingSlashes_PreservesSlashes()
            {
                var result = RuleImportExportService.PercentEncodePreservingSlashes("/a/b/c");

                result.Should().Be("/a/b/c");
            }
        }
    }
}
