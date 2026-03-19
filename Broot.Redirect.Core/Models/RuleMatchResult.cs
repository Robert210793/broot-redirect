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

        /// <summary>
        /// Match quality percentage from 0 to 100.
        /// 100 = exact match, 75 = extra query params, 50 = partial/prefix match.
        /// </summary>
        /// 
        public int Quality { get; set; }

        public MatchQualityLevel Level { get; set; }
    }
}
