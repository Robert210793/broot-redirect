using Broot.Redirect.API.Configuration;
using Broot.Redirect.API.Controllers;
using Broot.Redirect.API.Dtos;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.API.Controllers
{
    public class TrackingControllerTests
    {
        private readonly ITrackingRepository _repository;
        private readonly TrackingController _sut;

        public TrackingControllerTests()
        {
            _repository = Substitute.For<ITrackingRepository>();
            var logger = Substitute.For<ILogger<TrackingController>>();
            var options = Options.Create(new BrootRedirectOptions { TrackingRetentionDays = 30 });

            _sut = new TrackingController(_repository, logger, options);
        }

        public class TrackTests : TrackingControllerTests
        {
            [Fact]
            public async Task Track_ValidRequest_ReturnsOk()
            {
                _repository
                    .CreateAsync(Arg.Any<TrackingEntry>(), Arg.Any<CancellationToken>())
                    .Returns(Guid.NewGuid());

                var request = new TrackRequest
                {
                    OldUrl = "http://old.com/path",
                    NewUrl = "https://new.com/path",
                    Path = "/path"
                };

                var result = await _sut.Track(request) as OkObjectResult;

                result.Should().NotBeNull();

                await _repository.Received(1).CreateAsync(Arg.Any<TrackingEntry>(), Arg.Any<CancellationToken>());
            }
        }

        public class FeedbackTests : TrackingControllerTests
        {
            [Fact]
            public async Task Feedback_ValidRequest_ReturnsOk()
            {
                var trackingId = Guid.NewGuid();

                var existing = new TrackingEntry
                {
                    Id = trackingId,
                    OldUrl = "http://old.com",
                    NewUrl = "https://new.com",
                    Path = "/path"
                };

                _repository
                    .GetByIdAsync(trackingId, Arg.Any<CancellationToken>())
                    .Returns(existing);

                var request = new FeedbackRequest
                {
                    TrackingId = trackingId.ToString("N"),
                    Feedback = "OK"
                };

                var result = await _sut.Feedback(request) as OkObjectResult;

                result.Should().NotBeNull();

                await _repository.Received(1).UpdateAsync(Arg.Any<TrackingEntry>(), Arg.Any<CancellationToken>());
            }

            [Fact]
            public async Task Feedback_InvalidFeedbackValue_ReturnsBadRequest()
            {
                var request = new FeedbackRequest
                {
                    TrackingId = Guid.NewGuid().ToString("N"),
                    Feedback = "INVALID"
                };

                var result = await _sut.Feedback(request);

                result.Should().BeOfType<BadRequestObjectResult>();
            }

            [Fact]
            public async Task Feedback_InvalidTrackingIdFormat_ReturnsBadRequest()
            {
                var request = new FeedbackRequest
                {
                    TrackingId = "not-valid-format",
                    Feedback = "OK"
                };

                var result = await _sut.Feedback(request);

                result.Should().BeOfType<BadRequestObjectResult>();
            }

            [Fact]
            public async Task Feedback_TrackingEntryNotFound_ReturnsNotFound()
            {
                var trackingId = Guid.NewGuid();

                _repository
                    .GetByIdAsync(trackingId, Arg.Any<CancellationToken>())
                    .Returns((TrackingEntry?)null);

                var request = new FeedbackRequest
                {
                    TrackingId = trackingId.ToString("N"),
                    Feedback = "NOK"
                };

                var result = await _sut.Feedback(request);

                result.Should().BeOfType<NotFoundObjectResult>();
            }

            [Fact]
            public async Task Feedback_WithUserProposedUrl_SetsIt()
            {
                var trackingId = Guid.NewGuid();

                var existing = new TrackingEntry
                {
                    Id = trackingId,
                    OldUrl = "http://old.com",
                    NewUrl = "https://new.com",
                    Path = "/path"
                };

                _repository
                    .GetByIdAsync(trackingId, Arg.Any<CancellationToken>())
                    .Returns(existing);

                var request = new FeedbackRequest
                {
                    TrackingId = trackingId.ToString("N"),
                    Feedback = "NOK",
                    UserProposedUrl = "https://correct.com/path"
                };

                await _sut.Feedback(request);

                await _repository.Received(1).UpdateAsync(
                    Arg.Is<TrackingEntry>(entry => entry.UserProposedUrl == "https://correct.com/path"),
                    Arg.Any<CancellationToken>());
            }
        }

        public class GetStatsTests : TrackingControllerTests
        {
            [Fact]
            public async Task GetStats_ReturnsOkWithStats()
            {
                var stats = new TrackingStats
                {
                    TotalVisits = 100,
                    MatchedVisits = 80,
                    UnmatchedVisits = 20,
                    MatchRate = 80.0,
                    TopRules = new List<TopRuleStat>
                    {
                        new TopRuleStat { RuleId = "rule-1", Count = 50 }
                    }
                };

                _repository
                    .GetStatsAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                    .Returns(stats);

                var result = await _sut.GetStats() as OkObjectResult;

                result.Should().NotBeNull();

                var response = result!.Value as StatsResponse;

                response!.TotalVisits.Should().Be(100);
                response.TopRules.Should().HaveCount(1);
            }
        }

        public class GetEntriesTests : TrackingControllerTests
        {
            [Fact]
            public async Task GetEntries_ReturnsOkWithPaginatedEntries()
            {
                var entries = new List<TrackingEntry>
                {
                    new TrackingEntry
                    {
                        Id = Guid.NewGuid(),
                        OldUrl = "http://old.com",
                        NewUrl = "https://new.com",
                        Path = "/path",
                        Timestamp = DateTimeOffset.UtcNow
                    }
                };

                _repository
                    .GetPagedAsync(
                        Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(),
                        Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(),
                        Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                    .Returns((entries, 1));

                var result = await _sut.GetEntries() as OkObjectResult;

                result.Should().NotBeNull();

                var response = result!.Value as PaginatedTrackingResponse;

                response!.Total.Should().Be(1);
                response.Entries.Should().HaveCount(1);
            }

            [Fact]
            public async Task GetEntries_InvalidPage_DefaultsToOne()
            {
                _repository
                    .GetPagedAsync(
                        1, Arg.Any<int>(), Arg.Any<string?>(),
                        Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(),
                        Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                    .Returns((new List<TrackingEntry>(), 0));

                var result = await _sut.GetEntries(page: -5) as OkObjectResult;

                var response = result!.Value as PaginatedTrackingResponse;

                response!.CurrentPage.Should().Be(1);
            }
        }

        public class DeleteAllTests : TrackingControllerTests
        {
            [Fact]
            public async Task DeleteAll_ReturnsOkWithDeletedCount()
            {
                _repository
                    .DeleteAllAsync(Arg.Any<CancellationToken>())
                    .Returns(42);

                var result = await _sut.DeleteAll() as OkObjectResult;

                result.Should().NotBeNull();
            }
        }

        public class ExportEntriesTests : TrackingControllerTests
        {
            [Fact]
            public async Task ExportEntries_Json_ReturnsJsonFile()
            {
                _repository
                    .GetAllFilteredAsync(
                        Arg.Any<int>(), Arg.Any<string?>(),
                        Arg.Any<int?>(), Arg.Any<int?>(),
                        Arg.Any<string?>(), Arg.Any<string?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(new List<TrackingEntry>());

                var result = await _sut.ExportEntries(format: "json") as FileContentResult;

                result.Should().NotBeNull();
                result!.ContentType.Should().Be("application/json");
            }

            [Fact]
            public async Task ExportEntries_Csv_ReturnsCsvFile()
            {
                _repository
                    .GetAllFilteredAsync(
                        Arg.Any<int>(), Arg.Any<string?>(),
                        Arg.Any<int?>(), Arg.Any<int?>(),
                        Arg.Any<string?>(), Arg.Any<string?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(new List<TrackingEntry>
                    {
                        new TrackingEntry
                        {
                            Id = Guid.NewGuid(),
                            OldUrl = "http://old.com",
                            NewUrl = "https://new.com",
                            Path = "/path",
                            Timestamp = DateTimeOffset.UtcNow,
                            Feedback = "OK"
                        }
                    });

                var result = await _sut.ExportEntries(format: "csv") as FileContentResult;

                result.Should().NotBeNull();
                result!.ContentType.Should().Be("text/csv");

                var content = System.Text.Encoding.UTF8.GetString(result.FileContents);

                content.Should().Contain("Timestamp");
                content.Should().Contain("/path");
            }

            [Fact]
            public async Task ExportEntries_CsvWithSpecialChars_EscapesCorrectly()
            {
                _repository
                    .GetAllFilteredAsync(
                        Arg.Any<int>(), Arg.Any<string?>(),
                        Arg.Any<int?>(), Arg.Any<int?>(),
                        Arg.Any<string?>(), Arg.Any<string?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(new List<TrackingEntry>
                    {
                        new TrackingEntry
                        {
                            Id = Guid.NewGuid(),
                            OldUrl = "http://old.com/path,with,commas",
                            NewUrl = "https://new.com",
                            Path = "/path",
                            Timestamp = DateTimeOffset.UtcNow
                        }
                    });

                var result = await _sut.ExportEntries(format: "csv") as FileContentResult;

                var content = System.Text.Encoding.UTF8.GetString(result!.FileContents);

                content.Should().Contain("\"http://old.com/path,with,commas\"");
            }
        }

        public class GetTrendTests : TrackingControllerTests
        {
            [Fact]
            public async Task GetTrend_ValidParams_ReturnsOk()
            {
                _repository
                    .GetTrendAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(new List<TrendDataPoint>
                    {
                        new TrendDataPoint { Date = "2024-01-01", Ok = 10, Nok = 2, Total = 12 }
                    });

                var result = await _sut.GetTrend(days: 7, aggregation: "day") as OkObjectResult;

                result.Should().NotBeNull();

                var response = result!.Value as TrendResponse;

                response!.Days.Should().Be(7);
                response.DataPoints.Should().HaveCount(1);
            }

            [Fact]
            public async Task GetTrend_InvalidDays_DefaultsTo30()
            {
                _repository
                    .GetTrendAsync(30, Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(new List<TrendDataPoint>());

                var result = await _sut.GetTrend(days: -1) as OkObjectResult;

                var response = result!.Value as TrendResponse;

                response!.Days.Should().Be(30);
            }

            [Fact]
            public async Task GetTrend_InvalidAggregation_DefaultsToDay()
            {
                _repository
                    .GetTrendAsync(Arg.Any<int>(), "day", Arg.Any<CancellationToken>())
                    .Returns(new List<TrendDataPoint>());

                var result = await _sut.GetTrend(aggregation: "invalid") as OkObjectResult;

                var response = result!.Value as TrendResponse;

                response!.Aggregation.Should().Be("day");
            }
        }
    }
}
