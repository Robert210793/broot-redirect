using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Broot.Redirect.API.Configuration;

namespace Broot.Redirect.API.Services
{
    public sealed class BruteForceProtectionService : IDisposable
    {
        private readonly ConcurrentDictionary<string, AttemptInfo> _attempts = new();
        private readonly int _maxAttempts;
        private readonly TimeSpan _blockDuration;
        private readonly ILogger<BruteForceProtectionService> _logger;
        private readonly Timer _cleanupTimer;

        public BruteForceProtectionService(
            IOptions<BrootRedirectOptions> options,
            ILogger<BruteForceProtectionService> logger)
        {
            _maxAttempts = options.Value.LoginMaxAttempts;
            _blockDuration = TimeSpan.FromMinutes(options.Value.LoginBlockDurationMinutes);
            _logger = logger;

            _cleanupTimer = new Timer(
                CleanupExpiredEntries,
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5));
        }

        public bool IsBlocked(string ip)
        {
            if (!_attempts.TryGetValue(ip, out var info))
            {
                return false;
            }

            lock (info)
            {
                if (info.BlockedUntil == null)
                {
                    return false;
                }

                if (DateTimeOffset.UtcNow >= info.BlockedUntil)
                {
                    _attempts.TryRemove(ip, out _);

                    return false;
                }

                return true;
            }
        }

        public void RecordFailure(string ip)
        {
            var info = _attempts.GetOrAdd(ip, _ => new AttemptInfo());

            lock (info)
            {
                info.Attempts++;

                if (info.Attempts >= _maxAttempts)
                {
                    info.BlockedUntil = DateTimeOffset.UtcNow + _blockDuration;

                    _logger.LogWarning(
                        "IP {Ip} blocked after {Attempts} failed login attempts. Blocked until {BlockedUntil}",
                        ip,
                        info.Attempts,
                        info.BlockedUntil);
                }
            }
        }

        public void ResetAttempts(string ip)
        {
            _attempts.TryRemove(ip, out _);
        }

        public List<BlockedIpInfo> GetBlockedIps()
        {
            var now = DateTimeOffset.UtcNow;
            var result = new List<BlockedIpInfo>();

            foreach (var (ip, info) in _attempts)
            {
                lock (info)
                {
                    if (info.BlockedUntil != null && info.BlockedUntil > now)
                    {
                        result.Add(new BlockedIpInfo
                        {
                            Ip = ip,
                            Attempts = info.Attempts,
                            BlockedUntil = info.BlockedUntil.Value
                        });
                    }
                }
            }

            return result;
        }

        public void BlockIp(string ip)
        {
            var info = _attempts.GetOrAdd(ip, _ => new AttemptInfo());

            lock (info)
            {
                info.BlockedUntil = DateTimeOffset.UtcNow + _blockDuration;

                _logger.LogWarning("IP {Ip} manually blocked until {BlockedUntil}", ip, info.BlockedUntil);
            }
        }

        public void UnblockIp(string ip)
        {
            _attempts.TryRemove(ip, out _);

            _logger.LogInformation("IP {Ip} unblocked", ip);
        }

        public void ClearAll()
        {
            _attempts.Clear();

            _logger.LogInformation("All blocked IPs and attempt records cleared");
        }

        public void Dispose()
        {
            _cleanupTimer.Dispose();
        }

        private void CleanupExpiredEntries(object? state)
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var (ip, info) in _attempts)
            {
                lock (info)
                {
                    if (info.BlockedUntil != null && info.BlockedUntil <= now)
                    {
                        _attempts.TryRemove(ip, out _);
                    }
                }
            }
        }

        private sealed class AttemptInfo
        {
            public int Attempts { get; set; }

            public DateTimeOffset? BlockedUntil { get; set; }
        }

        public sealed class BlockedIpInfo
        {
            public required string Ip { get; set; }

            public int Attempts { get; set; }

            public DateTimeOffset BlockedUntil { get; set; }
        }
    }
}