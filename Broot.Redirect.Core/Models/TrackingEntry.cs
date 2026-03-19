using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Models
{
    public sealed class TrackingEntry
    {
        public Guid Id { get; set; }

        public string OldUrl { get; set; } = string.Empty;

        public string NewUrl { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        public string? UserAgent { get; set; }

        public string? Referrer { get; set; }

        public string? RuleId { get; set; }

        public int MatchQuality { get; set; }

        public string? Feedback { get; set; }

        public string? UserProposedUrl { get; set; }

        public string? RedirectStrategy { get; set; }
    }
}
