using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Interfaces
{
    public interface IRuleMatchingService
    {
        ProcessedRule PreprocessRule(RedirectRule rule, RuleMatchingConfig config);

        RuleMatchResult? FindMatchingRule(
            string requestUrl,
            IReadOnlyList<ProcessedRule> rules,
            RuleMatchingConfig config);

        /// <summary>
        /// Resolves the best matching rule for a request URL using the cache's
        /// type-partitioned indexes: wildcard O(1) → partial/domain scan → regex scan.
        /// </summary>
        RuleMatchResult? ResolveMatch(string requestUrl, RuleMatchingConfig config);
    }
}
