namespace WebHooks.Publisher.Api
{
    public class DeliveryEventsPage
    {
        public int Next { get; set; }

        public DeliveryEventItem[] Items { get; set; }
    }
}