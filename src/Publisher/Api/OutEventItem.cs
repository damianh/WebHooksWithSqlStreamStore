namespace WebHooks.Publisher.Api
{
    using System;

    public class OutEventItem
    {
        public Guid MessageId { get; set; }

        public string EventName { get; set; }

        public DateTime CreatedUtc { get; set; }

        public int Sequence { get; set; }
    }
}