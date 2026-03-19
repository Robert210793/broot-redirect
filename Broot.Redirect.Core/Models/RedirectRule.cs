using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Models
{
    public sealed class RedirectRule
    {
        public Guid Id { get; set; }

        public required string Matcher { get; set; }

        public string TargetUrl { get; set; } = string.Empty;

        public RedirectType RedirectType { get; set; } = RedirectType.Partial;

        public string InfoText { get; set; } = string.Empty;

        public bool AutoRedirect { get; set; }

        public bool DiscardQueryParams { get; set; }

        public bool ForwardQueryParams { get; set; }

        public List<KeptQueryParam> KeptQueryParams { get; set; } = new();

        public List<StaticQueryParam> StaticQueryParams { get; set; } = new();

        public List<SearchAndReplaceEntry> SearchAndReplace { get; set; } = new();

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
