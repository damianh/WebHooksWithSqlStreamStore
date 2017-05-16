namespace WebHooks.Publisher.Domain
{
    public class WebHookHeaders
    {
        public WebHookHeaders(string vendor)
        {
            EventNameHeader = $"X-{vendor}-WebHook-EventName";
            MessageIdHeader = $"X-{vendor}-WebHook-MessageId";
            SequenceHeader = $"X-{vendor}-WebHook-Sequence";
            SignatureHeader = $"X-{vendor}-WebHook-Signature";
        }

        public string EventNameHeader { get; }

        public string MessageIdHeader { get; }

        public string SequenceHeader { get; }

        public string SignatureHeader { get; }
    }
}