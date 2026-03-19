using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Infrastructure.Cache
{
    public class GlobalRuleCacheService : IGlobalRuleCacheService
    {
        private readonly ILogger<GlobalRuleCacheService> _logger;

        private List<GlobalRule> _rules = new();

        private Dictionary<Guid, GlobalRule> _rulesById = new();

        private readonly ReaderWriterLockSlim _lock = new();

        private volatile bool _isWarmedUp;

        public GlobalRuleCacheService(ILogger<GlobalRuleCacheService> logger)
        {
            _logger = logger;
        }

        public bool IsWarmedUp => _isWarmedUp;

        public int RuleCount
        {
            get
            {
                _lock.EnterReadLock();

                try
                {
                    return _rules.Count;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }

        public IReadOnlyList<GlobalRule> GetAll()
        {
            _lock.EnterReadLock();

            try
            {
                return _rules;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public GlobalRule? GetById(Guid id)
        {
            _lock.EnterReadLock();

            try
            {
                _rulesById.TryGetValue(id, out var rule);

                return rule;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Initialize(IReadOnlyList<GlobalRule> rules)
        {
            RebuildFromList(rules);

            _isWarmedUp = true;

            _logger.LogInformation("Global rule cache initialized with {RuleCount} rules.", rules.Count);
        }

        public void AddRule(GlobalRule rule)
        {
            _lock.EnterWriteLock();

            try
            {
                _rulesById[rule.Id] = rule;

                RebuildSortedList();
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            _logger.LogDebug("Added global rule {RuleId} to cache.", rule.Id);
        }

        public void UpdateRule(GlobalRule rule)
        {
            _lock.EnterWriteLock();

            try
            {
                _rulesById[rule.Id] = rule;

                RebuildSortedList();
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            _logger.LogDebug("Updated global rule {RuleId} in cache.", rule.Id);
        }

        public void RemoveRule(Guid ruleId)
        {
            _lock.EnterWriteLock();

            try
            {
                _rulesById.Remove(ruleId);

                RebuildSortedList();
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            _logger.LogDebug("Removed global rule {RuleId} from cache.", ruleId);
        }

        private void RebuildFromList(IReadOnlyList<GlobalRule> rules)
        {
            _lock.EnterWriteLock();

            try
            {
                _rulesById = new Dictionary<Guid, GlobalRule>();

                foreach (var rule in rules)
                {
                    _rulesById[rule.Id] = rule;
                }

                RebuildSortedList();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private void RebuildSortedList()
        {
            var sorted = _rulesById.Values.ToList();

            sorted.Sort((a, b) =>
            {
                var priorityComparison = a.Priority.CompareTo(b.Priority);

                if (priorityComparison != 0)
                {
                    return priorityComparison;
                }

                return a.CreatedAt.CompareTo(b.CreatedAt);
            });

            _rules = sorted;
        }
    }
}
