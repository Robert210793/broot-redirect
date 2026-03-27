using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Broot.Redirect.Tests.Helpers
{
    public class MockSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public string Id => "test-session";

        public bool IsAvailable => true;

        public IEnumerable<string> Keys => _store.Keys;

        public void Clear() => _store.Clear();

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Remove(string key) => _store.Remove(key);

        public void Set(string key, byte[] value) => _store[key] = value;

        public bool TryGetValue(string key, out byte[]? value) => _store.TryGetValue(key, out value);
    }

    public class MockSessionFeature : ISessionFeature
    {
        public MockSessionFeature(ISession session)
        {
            Session = session;
        }

        public ISession Session { get; set; }
    }

    public static class MockSessionExtensions
    {
        public static void InstallMockSession(this HttpContext context, MockSession? session = null)
        {
            var mockSession = session ?? new MockSession();

            context.Features.Set<ISessionFeature>(new MockSessionFeature(mockSession));
        }
    }
}
