namespace WebHooks.Publisher.Domain
{
    using System;
    using System.Collections.Generic;

    internal class WebHookMemento
    {
        public Guid Id;

        public Uri PayloadTargetUri;

        public bool Enabled;

        public SubscriptionChoice SubscriptionChoice;

        public HashSet<string> SubscribeToEvents;

        public string Secret;

        public DateTime CreatedUtc;

        public DateTime UpdatedUtc;
    }
}