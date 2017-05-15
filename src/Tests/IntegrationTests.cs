namespace WebHooks
{
    using System;
    using System.Net.Http;
    using System.Threading.Tasks;
    using SqlStreamStore;
    using WebHooks.Publisher;
    using WebHooks.Publisher.Api;
    using WebHooks.Publisher.Domain;
    using WebHooks.Subscriber;
    using WebHooks.Subscriber.Api;
    using Xunit;

    public class IntegrationTests : IDisposable
    {
        private readonly WebHookPublisher _webHookPublisher;
        private readonly WebHookSubscriber _webHookSubscriber;
        private readonly InMemoryStreamStore _publisherStreamStore;
        private readonly InMemoryStreamStore _subscriberStreamStore;
        private readonly HttpClient _publisherClient;
        private readonly HttpClient _subscriberClient;

        public IntegrationTests()
        {
            // Configure subscriber
            _subscriberStreamStore = new InMemoryStreamStore();
            var subscriberSettings = new WebHookSubscriberSettings(_subscriberStreamStore);
            _webHookSubscriber = new WebHookSubscriber(subscriberSettings);
            var subscriberHandler = new OwinHttpMessageHandler(_webHookSubscriber.AppFunc);
            _subscriberClient = new HttpClient(subscriberHandler)
            {
                BaseAddress = new Uri("http://subscriber.example.com")
            };

            // Configure publisher
            _publisherStreamStore = new InMemoryStreamStore();
            var publisherSettings = new WebHookPublisherSettings(_publisherStreamStore)
            {
                HttpMessageHandler = subscriberHandler
            };
            _webHookPublisher = new WebHookPublisher(publisherSettings);
            var publisherHandler = new OwinHttpMessageHandler(_webHookPublisher.AppFunc);
            _publisherClient = new HttpClient(publisherHandler)
            {
                BaseAddress = new Uri("http://publisher.example.com")
            };
        }

        [Fact]
        public async Task A_subscriber_should_received_a_published_event()
        {
            var addSubscriptionRequest = new AddSubscriptionRequest
            {
                Name = "MySub"
            };
            var response = await _subscriberClient.PostAsJson(addSubscriptionRequest, "hooks");
            var addSubscriptionResponse = await response.Content.ReadAs<AddSubscriptionResponse>();

            var addWebHookRequest = new AddWebHookRequest
            {
                Secret = addSubscriptionResponse.Secret,
                PayloadTargetUri =
                    new Uri($"http://subscriber.example.com/{addSubscriptionResponse.PayloadTargetRelativeUri}"),
                Enabled = true,
                SubscriptionChoice = SubscriptionChoice.Everything
            };
            await _publisherClient.PostAsJson(addWebHookRequest, "hooks");

            var eventName = "foo";
            var messageId = Guid.NewGuid();
            var json = "{ \"id\": 1 }";
            await _webHookPublisher.QueueEvent(messageId, eventName, json);
            await _webHookPublisher.DeliverNow();
        }

        public void Dispose()
        {
            _webHookSubscriber.Dispose();
            _webHookPublisher.Dispose();

            _subscriberStreamStore.Dispose();
            _publisherStreamStore.Dispose();
        }
    }
}