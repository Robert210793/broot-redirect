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
    }
}
