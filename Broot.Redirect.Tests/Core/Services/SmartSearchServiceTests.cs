using Broot.Redirect.Core.Models;
using Broot.Redirect.Core.Services;
using FluentAssertions;
using Xunit;

namespace Broot.Redirect.Tests.Core.Services
{
    public class SmartSearchServiceTests
    {
        public class BuildSearchUrlTests
        {
            private readonly SmartSearchService _sut = new();

            [Fact]
            public void BuildSearchUrl_EmptySmartSearchUrl_ReturnsNull()
            {
                var settings = new AppSettings
                {
                    SmartSearchUrl = ""
                };

                var result = _sut.BuildSearchUrl("/old/page", settings);

                result.Should().BeNull();
            }

            [Fact]
            public void BuildSearchUrl_ValidPath_ReturnsEncodedSearchUrl()
            {
                var settings = new AppSettings
                {
                    SmartSearchUrl = "https://new.com/search?q=",
                    SmartSearchSkipEncoding = false
                };

                var result = _sut.BuildSearchUrl("/old/my-article-name", settings);

                result.Should().Be("https://new.com/search?q=my%20article%20name");
            }

            [Fact]
            public void BuildSearchUrl_SkipEncoding_DoesNotEncode()
            {
                var settings = new AppSettings
                {
                    SmartSearchUrl = "https://new.com/search?q=",
                    SmartSearchSkipEncoding = true
                };

                var result = _sut.BuildSearchUrl("/old/my-article-name", settings);

                result.Should().Be("https://new.com/search?q=my article name");
            }

            [Fact]
            public void BuildSearchUrl_CustomRegex_UsesRegexMatch()
            {
                var settings = new AppSettings
                {
                    SmartSearchUrl = "https://new.com/search?q=",
                    SmartSearchRegex = @"/archive/(\d{4})/",
                    SmartSearchSkipEncoding = false
                };

                var result = _sut.BuildSearchUrl("/archive/2024/report", settings);

                result.Should().Be("https://new.com/search?q=2024");
            }

            [Fact]
            public void BuildSearchUrl_InvalidRegex_FallsBackToLastSegment()
            {
                var settings = new AppSettings
                {
                    SmartSearchUrl = "https://new.com/search?q=",
                    SmartSearchRegex = "[invalid",
                    SmartSearchSkipEncoding = false
                };

                var result = _sut.BuildSearchUrl("/old/my-article-name", settings);

                result.Should().Be("https://new.com/search?q=my%20article%20name");
            }
        }

        public class ExtractLastPathSegmentTests
        {
            [Fact]
            public void ExtractLastPathSegment_FileExtension_StripsExtension()
            {
                var result = SmartSearchService.ExtractLastPathSegment("/docs/report.pdf");

                result.Should().Be("report");
            }

            [Fact]
            public void ExtractLastPathSegment_HyphensAndUnderscores_ReplacesWithSpaces()
            {
                var result = SmartSearchService.ExtractLastPathSegment("/my-cool_page");

                result.Should().Be("my cool page");
            }

            [Fact]
            public void ExtractLastPathSegment_RootPath_ReturnsNull()
            {
                var result = SmartSearchService.ExtractLastPathSegment("/");

                result.Should().BeNull();
            }

            [Fact]
            public void ExtractLastPathSegment_EmptySegments_ReturnsNull()
            {
                var result = SmartSearchService.ExtractLastPathSegment("");

                result.Should().BeNull();
            }

            [Fact]
            public void ExtractLastPathSegment_PercentEncoded_DecodesSegment()
            {
                var result = SmartSearchService.ExtractLastPathSegment("/M%C3%BCnchen");

                result.Should().Be("München");
            }

            [Fact]
            public void ExtractLastPathSegment_MultipleSegments_ReturnsLast()
            {
                var result = SmartSearchService.ExtractLastPathSegment("/a/b/last-segment");

                result.Should().Be("last segment");
            }

            [Fact]
            public void ExtractLastPathSegment_MultipleHyphens_CollapsesSpaces()
            {
                var result = SmartSearchService.ExtractLastPathSegment("/my---page");

                result.Should().NotContain("  ");
            }

            [Fact]
            public void ExtractLastPathSegment_PathWithoutLeadingSlash_StillWorks()
            {
                var result = SmartSearchService.ExtractLastPathSegment("some-page");

                result.Should().Be("some page");
            }
        }

        public class BuildSearchUrlEdgeCaseTests
        {
            private readonly SmartSearchService _sut = new();

            [Fact]
            public void BuildSearchUrl_RootPath_ReturnsNull()
            {
                var settings = new AppSettings
                {
                    SmartSearchUrl = "https://search.com/q=",
                    SmartSearchSkipEncoding = false
                };

                var result = _sut.BuildSearchUrl("/", settings);

                result.Should().BeNull();
            }

            [Fact]
            public void BuildSearchUrl_SameRegexCalledTwice_UsesCachedRegex()
            {
                var settings = new AppSettings
                {
                    SmartSearchUrl = "https://search.com/q=",
                    SmartSearchRegex = @"/item/(\d+)",
                    SmartSearchSkipEncoding = false
                };

                var result1 = _sut.BuildSearchUrl("/item/123", settings);
                var result2 = _sut.BuildSearchUrl("/item/456", settings);

                result1.Should().Be("https://search.com/q=123");
                result2.Should().Be("https://search.com/q=456");
            }

            [Fact]
            public void BuildSearchUrl_RegexWithNoCapturingGroup_FallsBackToLastSegment()
            {
                var settings = new AppSettings
                {
                    SmartSearchUrl = "https://search.com/q=",
                    SmartSearchRegex = @"/item/\d+",
                    SmartSearchSkipEncoding = false
                };

                var result = _sut.BuildSearchUrl("/item/123", settings);

                result.Should().Be("https://search.com/q=123");
            }

            [Fact]
            public void BuildSearchUrl_RegexDoesNotMatch_FallsBackToLastSegment()
            {
                var settings = new AppSettings
                {
                    SmartSearchUrl = "https://search.com/q=",
                    SmartSearchRegex = @"/archive/(\d{4})/",
                    SmartSearchSkipEncoding = false
                };

                var result = _sut.BuildSearchUrl("/other/my-page", settings);

                result.Should().Be("https://search.com/q=my%20page");
            }

            [Fact]
            public void BuildSearchUrl_RegexMatchGroupEmpty_FallsBackToLastSegment()
            {
                var settings = new AppSettings
                {
                    SmartSearchUrl = "https://search.com/q=",
                    SmartSearchRegex = @"/item/()",
                    SmartSearchSkipEncoding = false
                };

                var result = _sut.BuildSearchUrl("/item/my-product", settings);

                // Empty capture group -> falls back to last segment
                result.Should().Be("https://search.com/q=my%20product");
            }

            [Fact]
            public void BuildSearchUrl_DifferentRegex_InvalidatesCacheAndUsesNew()
            {
                var settings1 = new AppSettings
                {
                    SmartSearchUrl = "https://search.com/q=",
                    SmartSearchRegex = @"/item/(\d+)",
                    SmartSearchSkipEncoding = false
                };

                var result1 = _sut.BuildSearchUrl("/item/123", settings1);
                result1.Should().Be("https://search.com/q=123");

                var settings2 = new AppSettings
                {
                    SmartSearchUrl = "https://search.com/q=",
                    SmartSearchRegex = @"/product/(\w+)",
                    SmartSearchSkipEncoding = false
                };

                var result2 = _sut.BuildSearchUrl("/product/shoes", settings2);
                result2.Should().Be("https://search.com/q=shoes");
            }

            [Fact]
            public void BuildSearchUrl_NullSmartSearchUrl_ReturnsNull()
            {
                var settings = new AppSettings
                {
                    SmartSearchUrl = null
                };

                var result = _sut.BuildSearchUrl("/some/path", settings);

                result.Should().BeNull();
            }
        }

        public class ExtractSearchTermEdgeCaseTests
        {
            [Fact]
            public void ExtractLastPathSegment_OnlyDashes_ReturnsNull()
            {
                var result = SmartSearchService.ExtractLastPathSegment("/---");

                // "---" becomes "   " after replace, which trims to empty
                result.Should().BeNull();
            }

            [Fact]
            public void ExtractLastPathSegment_TrailingSlash_IgnoresEmpty()
            {
                var result = SmartSearchService.ExtractLastPathSegment("/path/segment/");

                result.Should().Be("segment");
            }

            [Fact]
            public void ExtractLastPathSegment_QueryString_Ignored()
            {
                var result = SmartSearchService.ExtractLastPathSegment("/path/page?q=test");

                result.Should().Be("page");
            }

            [Fact]
            public void ExtractLastPathSegment_DotAtStart_NoExtensionStrip()
            {
                var result = SmartSearchService.ExtractLastPathSegment("/path/.hidden");

                result.Should().Be(".hidden");
            }
        }

        public class RegexCatchBlockTests
        {
            private readonly SmartSearchService _sut = new();

            [Fact]
            public void BuildSearchUrl_RegexCapturesPercentEncodedValue_UnescapesIt()
            {
                var settings = new AppSettings
                {
                    SmartSearchUrl = "https://search.com/q=",
                    SmartSearchRegex = @"/item/(.+)",
                    SmartSearchSkipEncoding = true
                };

                var result = _sut.BuildSearchUrl("/item/M%C3%BCnchen", settings);

                // Group captures "M%C3%BCnchen", Uri.UnescapeDataString returns "München"
                result.Should().Be("https://search.com/q=München");
            }

            [Fact]
            public void BuildSearchUrl_RegexCapturesBadPercentEncoding_ReturnRawValue()
            {
                var settings = new AppSettings
                {
                    SmartSearchUrl = "https://search.com/q=",
                    SmartSearchRegex = @"/item/(.+)",
                    SmartSearchSkipEncoding = true
                };

                // %ZZ is not valid percent encoding — Uri.UnescapeDataString will throw
                var result = _sut.BuildSearchUrl("/item/%ZZbad", settings);

                // Should fall back to the raw match value
                result.Should().Be("https://search.com/q=%ZZbad");
            }

            [Fact]
            public void ExtractSearchTerm_RegexReturnsEmpty_FallsBackToLastSegment()
            {
                // Regex with empty capture group
                var sut = new SmartSearchService();

                var result = sut.ExtractSearchTerm("/path/my-page", @"/path/()");

                // Empty group -> fallback to last segment
                result.Should().Be("my page");
            }

            [Fact]
            public void ExtractSearchTerm_NullRegex_UsesLastSegment()
            {
                var sut = new SmartSearchService();

                var result = sut.ExtractSearchTerm("/path/my-page", null);

                result.Should().Be("my page");
            }

            [Fact]
            public void ExtractSearchTerm_WhitespaceRegex_UsesLastSegment()
            {
                var sut = new SmartSearchService();

                var result = sut.ExtractSearchTerm("/path/my-page", "   ");

                result.Should().Be("my page");
            }
        }
    }
}
