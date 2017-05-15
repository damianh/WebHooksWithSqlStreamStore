namespace WebHooks.Publisher
{
    using System.Threading;
    using System.Threading.Tasks;

    public delegate Task<ReleaseLock> AcquireLock(CancellationToken cancellationToken);
}