namespace WebHooks.Publisher.Api
{
    using System;
    using WebHooks.Publisher.Domain;

    public class AddWebHookRequest
    {
        public Uri PayloadTargetUri { get; set; }

        public bool Enabled { get; set; }

        public SubscriptionChoice SubscriptionChoice { get; set; }

        public string[] SubscribeToEvents { get; set; }

        public string Secret { get; set; }
    }
}