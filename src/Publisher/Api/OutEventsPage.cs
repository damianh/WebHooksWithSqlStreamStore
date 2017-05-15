namespace WebHooks.Publisher.Api
{
    public class OutEventsPage
    {
        public int Next { get; set; }

        public OutEventItem[] Items { get; set; }
    }
}