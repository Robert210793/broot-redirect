using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Broot.Redirect.API.Dtos
{
    public class TrackRequest
    {
        [Required]
        [MaxLength(8000)]
        public string OldUrl { get; set; } = string.Empty;

        [Required]
        [MaxLength(8000)]
        public string NewUrl { get; set; } = string.Empty;

        [Required]
        [MaxLength(8000)]
        public string Path { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? UserAgent { get; set; }

        [MaxLength(2000)]
        public string? Referrer { get; set; }

        public string? RuleId { get; set; }

        public int MatchQuality { get; set; }

        public string? Feedback { get; set; }

        public string? RedirectStrategy { get; set; }
    }

    public class TrackResponse
    {
        public string Id { get; set; } = string.Empty;
    }

    public class FeedbackRequest
    {
        [Required]
        public string TrackingId { get; set; } = string.Empty;

        [Required]
        public string Feedback { get; set; } = string.Empty;

        [MaxLength(8000)]
        public string? UserProposedUrl { get; set; }
    }

    public class StatsResponse
    {
        public int TotalVisits { get; set; }

        public int MatchedVisits { get; set; }

        public int UnmatchedVisits { get; set; }

        public double MatchRate { get; set; }

        public int FeedbackOk { get; set; }

        public int FeedbackNok { get; set; }

        public int FeedbackAutoRedirect { get; set; }

        public int FeedbackNone { get; set; }

        public List<TopRuleStatDto> TopRules { get; set; } = new();
    }

    public class TopRuleStatDto
    {
        public string RuleId { get; set; } = string.Empty;

        public int Count { get; set; }
    }

    public class PaginatedTrackingResponse
    {
        public List<TrackingEntryDto> Entries { get; set; } = new();

        public int Total { get; set; }

        public int TotalPages { get; set; }

        public int CurrentPage { get; set; }
    }

    public class TrackingEntryDto
    {
        public string Id { get; set; } = string.Empty;

        public string OldUrl { get; set; } = string.Empty;

        public string NewUrl { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public string Timestamp { get; set; } = string.Empty;

        public string? UserAgent { get; set; }

        public string? Referrer { get; set; }

        public string? RuleId { get; set; }

        public int MatchQuality { get; set; }

        public string? Feedback { get; set; }

        public string? UserProposedUrl { get; set; }

        public string? RedirectStrategy { get; set; }
    }
}