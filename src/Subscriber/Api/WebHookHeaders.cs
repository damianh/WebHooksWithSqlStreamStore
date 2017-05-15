namespace WebHooks.Subscriber.Api
{
    public class WebHookHeaders
    {
        public WebHookHeaders(string vendor)
        {
            EventNameHeader = $"X-{vendor}-WebHook-EventName";
            MessageIdHeader = $"X-{vendor}-WebHook-MessageId";
            SequenceHeader = $"X-{vendor}-WebHook-Sequence";
        }

        public string EventNameHeader { get; }

        public string MessageIdHeader { get; }

        public string SequenceHeader { get; }
    }
}