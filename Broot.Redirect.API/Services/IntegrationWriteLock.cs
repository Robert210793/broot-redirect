namespace Broot.Redirect.API.Services
{
    public sealed class IntegrationWriteLock : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public Task WaitAsync(CancellationToken cancellationToken = default)
        {
            return _semaphore.WaitAsync(cancellationToken);
        }

        public void Release()
        {
            _semaphore.Release();
        }

        public void Dispose()
        {
            _semaphore.Dispose();
        }
    }
}
