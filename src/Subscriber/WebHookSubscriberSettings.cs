using System;
using SqlStreamStore;

namespace WebHooks.Subscriber
{
    public class WebHookSubscriberSettings
    {
        public const int DefaultMaxSubscriptionCount = 10;

        public WebHookSubscriberSettings(IStreamStore streamStore)
        {
            StreamStore = streamStore;
        }

        public IStreamStore StreamStore { get; }

        /// <summary>
        ///     An abstraction to control DateTime. Used in tests.
        /// </summary>
        public GetUtcNow GetUtcNow { get; set; } = () => DateTime.UtcNow;

        /// <summary>
        ///     Get or sets the maximum number of subscriptions.
        /// </summary>
        public int MaxSubscriptionCount { get; set; } = DefaultMaxSubscriptionCount;

        /// <summary>
        ///     The vendor segment in the custom headers "X-Vendor-WebHook..."
        /// </summary>
        public string Vendor { get; set; } = "Vendor";

        /// <summary>
        ///     Set to true for the webhook receiver endpoint to return an error. Used for testing.
        /// </summary>
        public bool ReturnErrorOnReceive { get; set; }
    }
}