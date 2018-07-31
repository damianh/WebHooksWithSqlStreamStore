using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SqlStreamStore;
using WebHooks.Publisher;
using WebHooks.Publisher.Api;
using WebHooks.Publisher.Domain;
using WebHooks.Subscriber;
using WebHooks.Subscriber.Api;
using Xunit;

namespace WebHooks
{
    public class IntegrationTests : IDisposable
    {
        public IntegrationTests()
        {
            // Configure subscriber
            _subscriberStreamStore = new InMemoryStreamStore();
            var subscriberSettings = new WebHookSubscriberSettings(_subscriberStreamStore);
            var subscriberWebHostBuilder = new WebHostBuilder()
                .UseStartup<WebHookSubscriberStartup>()
                .ConfigureServices(services => services.AddSingleton(subscriberSettings));
            var subscriberTestServer = new TestServer(subscriberWebHostBuilder);
            _subscriberClient = new HttpClient(subscriberTestServer.CreateHandler())
            {
                BaseAddress = new Uri("http://subscriber.example.com")
            };

            // Configure publisher
            _publisherStreamStore = new InMemoryStreamStore();
            var publisherSettings = new WebHookPublisherSettings(_publisherStreamStore)
            {
                HttpMessageHandler = subscriberTestServer.CreateHandler()
            };
            _webHookPublisher = new WebHookPublisher(publisherSettings);
            var publisherWebHostBuilder = new WebHostBuilder()
                .UseStartup<WebHookPublisherStartup>()
                .ConfigureServices(services => services.AddSingleton(publisherSettings));
            var publisherTestServer = new TestServer(publisherWebHostBuilder);
            _publisherClient = new HttpClient(publisherTestServer.CreateHandler())
            {
                BaseAddress = new Uri("http://publisher.example.com")
            };
        }

        public void Dispose()
        {
            _webHookPublisher.Dispose();

            _subscriberStreamStore.Dispose();
            _publisherStreamStore.Dispose();
        }

        private readonly WebHookPublisher _webHookPublisher;
        private readonly InMemoryStreamStore _publisherStreamStore;
        private readonly InMemoryStreamStore _subscriberStreamStore;
        private readonly HttpClient _publisherClient;
        private readonly HttpClient _subscriberClient;

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
    }
}