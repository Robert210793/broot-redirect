using Azure;
using Azure.Data.Tables;
using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Broot.Redirect.Infrastructure.Persistence
{
    /// <summary>
    /// Azure Table Storage entity for RedirectRule.
    /// Flat property mapping with JSON serialization for nested collections.
    /// PartitionKey is fixed "rule" for single-tenant deployment.
    /// RowKey is the Rule ID as GUID without hyphens.
    /// </summary>
    /// 
    public class RedirectRuleEntity : ITableEntity
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public string PartitionKey { get; set; } = "rule";

        public string RowKey { get; set; } = string.Empty;

        public DateTimeOffset? Timestamp { get; set; }

        public ETag ETag { get; set; }

        public string Matcher { get; set; } = string.Empty;

        public string? TargetUrl { get; set; }

        public string RedirectType { get; set; } = "partial";

        public string? InfoText { get; set; }

        public bool AutoRedirect { get; set; }

        public bool DiscardQueryParams { get; set; }

        public bool ForwardQueryParams { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public string? KeptQueryParamsJson { get; set; }

        public string? StaticQueryParamsJson { get; set; }

        public string? SearchAndReplaceJson { get; set; }

        /// <summary>
        /// Maps a domain RedirectRule to a Table Storage entity.
        /// </summary>
        public static RedirectRuleEntity FromDomainModel(RedirectRule rule)
        {
            var entity = new RedirectRuleEntity
            {
                PartitionKey = "rule",
                RowKey = rule.Id.ToString("N"),
                Matcher = rule.Matcher,
                TargetUrl = rule.TargetUrl,
                RedirectType = rule.RedirectType.ToString().ToLowerInvariant(),
                InfoText = rule.InfoText,
                AutoRedirect = rule.AutoRedirect,
                DiscardQueryParams = rule.DiscardQueryParams,
                ForwardQueryParams = rule.ForwardQueryParams,
                CreatedAt = rule.CreatedAt
            };

            if (rule.KeptQueryParams.Count > 0)
            {
                entity.KeptQueryParamsJson = JsonSerializer.Serialize(rule.KeptQueryParams, JsonOptions);
            }

            if (rule.StaticQueryParams.Count > 0)
            {
                entity.StaticQueryParamsJson = JsonSerializer.Serialize(rule.StaticQueryParams, JsonOptions);
            }

            if (rule.SearchAndReplace.Count > 0)
            {
                entity.SearchAndReplaceJson = JsonSerializer.Serialize(rule.SearchAndReplace, JsonOptions);
            }

            return entity;
        }

        /// <summary>
        /// Maps a Table Storage entity back to a domain RedirectRule.
        /// </summary>
        /// 
        public RedirectRule ToDomainModel()
        {
            var rule = new RedirectRule
            {
                Id = Guid.ParseExact(RowKey, "N"),
                Matcher = Matcher,
                TargetUrl = TargetUrl,
                RedirectType = ParseRedirectType(RedirectType),
                InfoText = InfoText,
                AutoRedirect = AutoRedirect,
                DiscardQueryParams = DiscardQueryParams,
                ForwardQueryParams = ForwardQueryParams,
                CreatedAt = CreatedAt
            };

            if (!string.IsNullOrEmpty(KeptQueryParamsJson))
            {
                var parsed = JsonSerializer.Deserialize<List<KeptQueryParam>>(KeptQueryParamsJson, JsonOptions);

                if (parsed != null)
                {
                    rule.KeptQueryParams = parsed;
                }
            }

            if (!string.IsNullOrEmpty(StaticQueryParamsJson))
            {
                var parsed = JsonSerializer.Deserialize<List<StaticQueryParam>>(StaticQueryParamsJson, JsonOptions);

                if (parsed != null)
                {
                    rule.StaticQueryParams = parsed;
                }
            }

            if (!string.IsNullOrEmpty(SearchAndReplaceJson))
            {
                var parsed = JsonSerializer.Deserialize<List<SearchAndReplaceEntry>>(SearchAndReplaceJson, JsonOptions);

                if (parsed != null)
                {
                    rule.SearchAndReplace = parsed;
                }
            }

            return rule;
        }

        private static Core.Models.RedirectType ParseRedirectType(string value)
        {
            return value.ToLowerInvariant() switch
            {
                "wildcard" => Core.Models.RedirectType.Wildcard,
                "partial" => Core.Models.RedirectType.Partial,
                "domain" => Core.Models.RedirectType.Domain,
                "regex" => Core.Models.RedirectType.Regex,
                _ => Core.Models.RedirectType.Partial
            };
        }
    }
}
