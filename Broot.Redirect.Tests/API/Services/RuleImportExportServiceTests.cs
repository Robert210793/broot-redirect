using System.Text;
using Broot.Redirect.API.Services;
using Broot.Redirect.Core.Models;
using ClosedXML.Excel;
using FluentAssertions;
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
    }
}
