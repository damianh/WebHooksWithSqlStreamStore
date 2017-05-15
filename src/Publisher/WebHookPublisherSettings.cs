namespace WebHooks.Publisher
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using SqlStreamStore;

    public class WebHookPublisherSettings
    {
        private static readonly AcquireLock DefaultAcquireLock = ct => Task.FromResult(new ReleaseLock(_ => { }));
        public const int DefaultMaxWebHookCount = 10;

        public WebHookPublisherSettings(IStreamStore streamStore)
        {
            StreamStore = streamStore ?? throw new ArgumentNullException(nameof(streamStore));
        }

        /// <summary>
        ///     Gets or sets the HTTP message handler. Used when delivering events. Typically overrridden in tests.
        /// </summary>
        /// <value>
        ///     The HTTP message handler.
        /// </value>
        public HttpMessageHandler HttpMessageHandler { get; set; } = new HttpClientHandler
        {
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.GZip,
            AllowAutoRedirect = true
        };

        /// <summary>
        /// Gets the acquire lock.
        /// </summary>
        /// <value>
        /// The acquire lock.
        /// </value>
        public AcquireLock AcquireLock { get; set; } = DefaultAcquireLock;

        /// <summary>
        ///     The stream store instance used by the publisher(s).
        /// </summary>
        /// <value>
        ///     The stream store.
        /// </value>
        public IStreamStore StreamStore { get; }

        /// <summary>
        ///     Gets or sets a delegate to get the current UTC Date Time.
        /// </summary>
        public GetUtcNow GetUtcNow { get; set; } = () => DateTime.UtcNow;

        /// <summary>
        ///     Gets or sets the maximum web hook count.
        /// </summary>
        public int MaxWebHookCount { get; set; } = DefaultMaxWebHookCount;

        /// <summary>
        ///     Get or sets the maximum count of messages allowed in a webhook's out stream.
        ///     When exceeded the  oldest messages are automatically purged. Default is 10000.
        /// </summary>
        public int OutStreamMaxCount { get; set; } = 10000;

        /// <summary>
        ///     Get or sets the maximum count of messages retained in a webhooks delivery stream.
        /// </summary>
        public int DeliveryStreamMaxCount { get; set; } = 1000;

        /// <summary>
        ///     Gets or sets the maximum delay a message is retried. This overrides the delay
        ///     calculated by exponential backoff if it is smaller. Default is 1 hour.
        /// </summary>
        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        ///     Gets or sets the maximum duration that an attempt will be made to deliver
        ///     an event. When exceeded, the webhook is disabled and out stream will be
        ///     purged.
        /// </summary>
        public TimeSpan MaxDeliveryAttemptDuration { get; set; } = TimeSpan.FromDays(7);

        /// <summary>
        ///     Used in the custom HTTP Headers "X-{Vendor}-WebHook..."
        /// </summary>
        public string Vendor { get; set; } = "Vendor";
    }
}