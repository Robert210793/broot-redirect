using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Models
{
    public sealed class RuleMatchingConfig
    {
        public int WeightPathSegment { get; set; } = 10;

        public int WeightQueryPair { get; set; } = 3;

        public int PenaltyWildcard { get; set; } = -1;

        public int BonusExactMatch { get; set; } = 5;

        public TrailingSlashPolicy TrailingSlashPolicy { get; set; } = TrailingSlashPolicy.Ignore;

        public bool CaseSensitivePath { get; set; }

        public bool CaseSensitiveQuery { get; set; }
    }

    public enum TrailingSlashPolicy
    {
        Ignore,
        Strict
    }
}
