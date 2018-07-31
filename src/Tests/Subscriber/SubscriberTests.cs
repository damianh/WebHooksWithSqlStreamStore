using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SqlStreamStore;
using SqlStreamStore.Streams;
using WebHooks.Publisher;
using WebHooks.Subscriber.Api;
using Xunit;

namespace WebHooks.Subscriber
{
    public class SubscriberTests : IDisposable
    {
        public SubscriberTests()
        {
            _streamStore = new InMemoryStreamStore();
            _subscriberSettings = new WebHookSubscriberSettings(_streamStore);
            var subscriberWebHostBuilder = new WebHostBuilder()
                .UseStartup<WebHookSubscriberStartup>()
                .ConfigureServices(services => services.AddSingleton(_subscriberSettings));
            var subscriberTestServer = new TestServer(subscriberWebHostBuilder);
            _client = new HttpClient(subscriberTestServer.CreateHandler())
            {
                BaseAddress = new Uri("http://subscriber.example.com")
            };
            _client.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));
        }

        public void Dispose()
        {
            _client.Dispose();
            _streamStore.Dispose();
        }

        private readonly HttpClient _client;
        private readonly InMemoryStreamStore _streamStore;
        private readonly WebHookSubscriberSettings _subscriberSettings;

        private void SetCustomHeaders(HttpRequestMessage postEventRequest, string eventName, Guid messageId,
            string signature)
        {
            var headers = new WebHookHeaders(_subscriberSettings.Vendor);
            postEventRequest.Headers.TryAddWithoutValidation(headers.EventNameHeader, eventName);
            postEventRequest.Headers.TryAddWithoutValidation(headers.MessageIdHeader, messageId.ToString("d"));
            postEventRequest.Headers.TryAddWithoutValidation(headers.SequenceHeader, "1");
            postEventRequest.Headers.TryAddWithoutValidation(headers.SignatureHeader, signature);
        }

        [Fact]
        public async Task Can_add_subscription()
        {
            var request = new AddSubscriptionRequest
            {
                Name = "foo"
            };

            var response = await _client.PostAsJson(request, "hooks");
            var subscriptionResponse = await response.Content.ReadAs<AddSubscriptionResponse>();

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            response.Headers.Location.ShouldNotBeNull();
            subscriptionResponse.Name.ShouldBe(request.Name);
            subscriptionResponse.Secret.ShouldNotBeNullOrWhiteSpace();
            subscriptionResponse.PayloadTargetRelativeUri.ShouldNotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task Can_delete_subscription()
        {
            var request = new AddSubscriptionRequest
            {
                Name = "foo"
            };
            var response = await _client.PostAsJson(request, "hooks");

            response = await _client.DeleteAsync(response.Headers.Location);
            var getResponse = await _client.GetAsync(response.Headers.Location);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Can_get_subscription()
        {
            var request = new AddSubscriptionRequest
            {
                Name = "foo"
            };
            var response = await _client.PostAsJson(request, "hooks");

            response = await _client.GetAsync(response.Headers.Location);
            var subscription = await response.Content.ReadAs<WebHookSubscription>();

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            subscription.Name.ShouldBe(request.Name);
            subscription.CreatedUtc.ShouldBeGreaterThan(DateTime.MinValue);
            subscription.PayloadTargetRelativeUri.ShouldNotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task Can_receive_event()
        {
            // Arrange
            var request = new AddSubscriptionRequest
            {
                Name = "foo"
            };
            var addSubscription = await _client.PostAsJson(request, "hooks");
            var addSubscriptionResponse = await addSubscription.Content.ReadAs<AddSubscriptionResponse>();
            var eventName = "foo";
            var messageId = Guid.NewGuid();
            var json = "{ \"id\": 1 }";
            var signature = PayloadSignature.CreateSignature(json, addSubscriptionResponse.Secret);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var postEventRequest = new HttpRequestMessage(HttpMethod.Post, addSubscription.Headers.Location)
            {
                Content = content
            };
            SetCustomHeaders(postEventRequest, eventName, messageId, signature);

            // Act
            var response = await _client.SendAsync(postEventRequest);
            var streamMessage = (await _streamStore.ReadAllForwards(Position.Start, 10, true))
                .Messages.Where(m => m.Type == eventName).SingleOrDefault();

            // Assert
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await streamMessage.GetJsonData()).ShouldBe(json);
            streamMessage.MessageId.ShouldBe(messageId);
        }

        [Fact]
        public async Task Should_have_no_subscriptions()
        {
            var response = await _client.GetAsync("hooks");
            var webHooks = await response.Content.ReadAs<WebHookSubscription[]>();

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            webHooks.ShouldNotBeNull();
            webHooks.Length.ShouldBe(0);
        }

        [Fact]
        public async Task When_add_subscription_then_should_be_in_collection()
        {
            var request = new AddSubscriptionRequest
            {
                Name = "foo"
            };
            await _client.PostAsJson(request, "hooks");
            var response = await _client.GetAsync("hooks");

            var subscriptions = await response.Content.ReadAs<WebHookSubscription[]>();

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            subscriptions.Length.ShouldBe(1);
            subscriptions[0].Name.ShouldBe(request.Name);
            subscriptions[0].PayloadTargetRelativeUri.ShouldNotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task When_receive_same_event_twice_then_should_record_it_once()
        {
            // Arrange
            var request = new AddSubscriptionRequest
            {
                Name = "foo"
            };
            var addSubscription = await _client.PostAsJson(request, "hooks");
            var addSubscriptionResponse = await addSubscription.Content.ReadAs<AddSubscriptionResponse>();
            var eventName = "foo";
            var messageId = Guid.NewGuid();
            var json = "{ \"id\": 1 }";
            var signature = PayloadSignature.CreateSignature(json, addSubscriptionResponse.Secret);
            var postEventRequest = new HttpRequestMessage(HttpMethod.Post, addSubscription.Headers.Location)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            SetCustomHeaders(postEventRequest, eventName, messageId, signature);
            await _client.SendAsync(postEventRequest);

            // Act
            var secondPostEventRequest = new HttpRequestMessage(HttpMethod.Post, addSubscription.Headers.Location)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            SetCustomHeaders(postEventRequest, eventName, messageId, signature);
            await _client.SendAsync(secondPostEventRequest);

            (await _streamStore.ReadAllForwards(Position.Start, 10, true))
                .Messages
                .Where(m => m.Type == eventName && m.MessageId == messageId)
                .SingleOrDefault() // Idempotent!
                .ShouldNotBeNull();
        }
    }
}