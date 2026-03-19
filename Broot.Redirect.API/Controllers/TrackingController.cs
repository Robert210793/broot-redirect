using Broot.Redirect.API.Configuration;
using Broot.Redirect.API.Dtos;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Broot.Redirect.API.Controllers
{
    [ApiController]
    public class TrackingController : ControllerBase
    {
        private readonly ITrackingRepository _repository;
        private readonly ILogger<TrackingController> _logger;
        private readonly int _retentionDays;

        public TrackingController(
            ITrackingRepository repository,
            ILogger<TrackingController> logger,
            IOptions<BrootRedirectOptions> options)
        {
            _repository = repository;
            _logger = logger;
            _retentionDays = options.Value.TrackingRetentionDays;
        }

        [HttpPost("api/track")]
        public async Task<IActionResult> Track([FromBody] TrackRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { error = "Invalid tracking data." });
            }

            var entry = new TrackingEntry
            {
                Id = Guid.NewGuid(),
                OldUrl = request.OldUrl,
                NewUrl = request.NewUrl,
                Path = request.Path,
                Timestamp = DateTimeOffset.UtcNow,
                UserAgent = request.UserAgent,
                Referrer = request.Referrer,
                RuleId = request.RuleId,
                MatchQuality = request.MatchQuality,
                Feedback = request.Feedback,
                RedirectStrategy = request.RedirectStrategy
            };

            var id = await _repository.CreateAsync(entry);

            _logger.LogDebug("Tracked visit {EntryId} for path '{Path}'.", id, request.Path);

            return Ok(new TrackResponse { Id = id.ToString("N") });
        }

        [HttpPost("api/feedback")]
        public async Task<IActionResult> Feedback([FromBody] FeedbackRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { error = "Invalid feedback data." });
            }

            var validFeedback = new[] { "OK", "NOK" };

            if (!validFeedback.Contains(request.Feedback))
            {
                return BadRequest(new { error = "Feedback must be 'OK' or 'NOK'." });
            }

            if (!Guid.TryParseExact(request.TrackingId, "N", out var trackingGuid))
            {
                return BadRequest(new { error = "Invalid tracking ID format." });
            }

            var existing = await _repository.GetByIdAsync(trackingGuid);

            if (existing == null)
            {
                return NotFound(new { error = "Tracking entry not found." });
            }

            existing.Feedback = request.Feedback;

            if (!string.IsNullOrWhiteSpace(request.UserProposedUrl))
            {
                existing.UserProposedUrl = request.UserProposedUrl;
            }

            await _repository.UpdateAsync(existing);

            _logger.LogDebug("Updated feedback for tracking entry {EntryId}: {Feedback}.", trackingGuid, request.Feedback);

            return Ok(new { success = true });
        }

        [HttpGet("api/stats")]
        public async Task<IActionResult> GetStats()
        {
            var stats = await _repository.GetStatsAsync(_retentionDays);

            var response = new StatsResponse
            {
                TotalVisits = stats.TotalVisits,
                MatchedVisits = stats.MatchedVisits,
                UnmatchedVisits = stats.UnmatchedVisits,
                MatchRate = stats.MatchRate,
                FeedbackOk = stats.FeedbackOk,
                FeedbackNok = stats.FeedbackNok,
                FeedbackAutoRedirect = stats.FeedbackAutoRedirect,
                FeedbackNone = stats.FeedbackNone,
                TopRules = stats.TopRules.Select(topRule => new TopRuleStatDto
                {
                    RuleId = topRule.RuleId,
                    Count = topRule.Count
                }).ToList()
            };

            return Ok(response);
        }

        [HttpGet("api/stats/entries")]
        public async Task<IActionResult> GetEntries(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 50,
            [FromQuery] string? search = null)
        {
            if (page < 1)
            {
                page = 1;
            }

            if (limit < 1 || limit > 200)
            {
                limit = 50;
            }

            var (entries, totalCount) = await _repository.GetPagedAsync(page, limit, search, _retentionDays);

            var totalPages = (int)Math.Ceiling((double)totalCount / limit);

            var response = new PaginatedTrackingResponse
            {
                Entries = entries.Select(entry => new TrackingEntryDto
                {
                    Id = entry.Id.ToString("N"),
                    OldUrl = entry.OldUrl,
                    NewUrl = entry.NewUrl,
                    Path = entry.Path,
                    Timestamp = entry.Timestamp.ToString("o"),
                    UserAgent = entry.UserAgent,
                    Referrer = entry.Referrer,
                    RuleId = entry.RuleId,
                    MatchQuality = entry.MatchQuality,
                    Feedback = entry.Feedback,
                    UserProposedUrl = entry.UserProposedUrl,
                    RedirectStrategy = entry.RedirectStrategy
                }).ToList(),
                Total = totalCount,
                TotalPages = totalPages,
                CurrentPage = page
            };

            return Ok(response);
        }
    }
}