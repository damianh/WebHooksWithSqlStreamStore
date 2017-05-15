namespace WebHooks.Subscriber.Api
{
    using System;

    public class AddSubscriptionResponse
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public string PayloadTargetRelativeUri { get; set; }

        public string Secret { get; set; }
    }
}