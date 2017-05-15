namespace WebHooks.Subscriber.Api
{
    using System;

    public class WebHookSubscription
    {
        public string Name { get; set; }

        public string PayloadTargetRelativeUri { get; set; }

        public DateTime CreatedUtc { get; set; }
    }
}