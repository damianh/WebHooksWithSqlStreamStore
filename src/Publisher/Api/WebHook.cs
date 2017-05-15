namespace WebHooks.Publisher.Api
{
    using System;

    public class WebHook
    {
        public string Id;

        public Uri PayloadTargetUri;

        public bool Enabled;

        public int SubscriptionChoice;

        public string[] SubscribeToEvents;

        public bool HasSecret;

        public DateTime CreatedUtc;

        public DateTime UpdatedUtc;
    }
}