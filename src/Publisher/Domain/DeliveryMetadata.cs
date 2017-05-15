namespace WebHooks.Publisher.Domain
{
    using System;

    public class DeliveryMetadata
    {
        public Guid EventId { get; set; }

        public int AttemptCount { get; set; }

        public bool DeliverySuccess { get; set; }

        public int Sequence { get; set; }
    }
}