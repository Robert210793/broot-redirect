using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Models
{
    public sealed class ProcessedRule
    {
        public required RedirectRule Rule { get; set; }

        public string[] NormalizedPath { get; set; } = Array.Empty<string>();

        public Dictionary<string, List<string>> NormalizedQuery { get; set; } = new();

        public bool IsDomainMatcher { get; set; }
    }
}
