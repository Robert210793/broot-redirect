using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Models
{
    public sealed class RuleMatchResult
    {
        public required RedirectRule Rule { get; set; }

        public int Score { get; set; }

        public int Quality { get; set; }

        public MatchQualityLevel Level { get; set; }
    }
}
