using Azure;
using Azure.Data.Tables;
using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Infrastructure.Persistence
{
    /// <summary>
    /// Azure Table Storage entity for GlobalRule.
    /// PartitionKey = "GlobalRule", RowKey = Id (GUID without hyphens).
    /// All properties are scalar -- no JSON serialization needed.
    /// </summary>
    public class GlobalRuleEntity : ITableEntity
    {
        public const string DefaultPartitionKey = "GlobalRule";

        public string PartitionKey { get; set; } = DefaultPartitionKey;

        public string RowKey { get; set; } = string.Empty;

        public DateTimeOffset? Timestamp { get; set; }

        public ETag ETag { get; set; }

        public string Search { get; set; } = string.Empty;

        public string Replace { get; set; } = string.Empty;

        public bool CaseSensitive { get; set; }

        public int Priority { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>
        /// Maps a domain GlobalRule to a Table Storage entity.
        /// </summary>
        public static GlobalRuleEntity FromDomainModel(GlobalRule rule)
        {
            return new GlobalRuleEntity
            {
                PartitionKey = DefaultPartitionKey,
                RowKey = rule.Id.ToString("N"),
                Search = rule.Search,
                Replace = rule.Replace,
                CaseSensitive = rule.CaseSensitive,
                Priority = rule.Priority,
                CreatedAt = rule.CreatedAt
            };
        }

        /// <summary>
        /// Maps a Table Storage entity back to a domain GlobalRule.
        /// </summary>
        public GlobalRule ToDomainModel()
        {
            return new GlobalRule
            {
                Id = Guid.ParseExact(RowKey, "N"),
                Search = Search,
                Replace = Replace,
                CaseSensitive = CaseSensitive,
                Priority = Priority,
                CreatedAt = CreatedAt
            };
        }
    }
}
