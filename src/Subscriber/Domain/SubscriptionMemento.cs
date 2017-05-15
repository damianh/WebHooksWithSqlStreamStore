namespace WebHooks.Subscriber.Domain
{
    using System;

    public class SubscriptionMemento
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public string Secret { get; set; }

        public DateTime CreatedUtc { get; set; }
    }
}