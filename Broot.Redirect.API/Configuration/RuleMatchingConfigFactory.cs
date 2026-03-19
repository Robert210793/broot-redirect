using Broot.Redirect.Core.Models;

namespace Broot.Redirect.API.Configuration
{
    /// <summary>
    /// Builds RuleMatchingConfig from the appsettings-bound SmartRedirectOptions.
    /// Centralizes the string-to-enum conversion for TrailingSlashPolicy.
    /// </summary>
    /// 
    public static class RuleMatchingConfigFactory
    {
        public static RuleMatchingConfig Create(BrootRedirectOptions options)
        {
            if (!Enum.TryParse<TrailingSlashPolicy>(options.TrailingSlashPolicy, ignoreCase: true, out var trailingSlashPolicy))
            {
                trailingSlashPolicy = TrailingSlashPolicy.Ignore;
            }

            return new RuleMatchingConfig
            {
                CaseSensitivePath = options.CaseSensitivePath,
                CaseSensitiveQuery = options.CaseSensitiveQuery,
                TrailingSlashPolicy = trailingSlashPolicy,
                WeightPathSegment = options.WeightPathSegment,
                WeightQueryPair = options.WeightQueryPair,
                PenaltyWildcard = options.PenaltyWildcard,
                BonusExactMatch = options.BonusExactMatch
            };
        }
    }
}
