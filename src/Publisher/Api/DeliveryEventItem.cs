namespace WebHooks.Publisher.Api
{
    using System;

    public class DeliveryEventItem
    {
        public Guid MessageId { get; set; }

        public Guid EventMessageId { get; set; }

        public int EventSequence { get; set; }

        public bool Success { get; set; }

        public string EventName { get; set; }

        public DateTime CreatedUtc { get; set; }

        public int Sequence { get; set; }

        public string ErrorMessage { get; set; }
    }
}