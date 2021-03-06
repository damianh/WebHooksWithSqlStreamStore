﻿using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SqlStreamStore;
using WebHooks.Publisher.Api;
using WebHooks.Publisher.Domain;
using WebHooks.Subscriber;
using WebHooks.Subscriber.Api;
using Xunit;

namespace WebHooks.Publisher
{
    public class PublisherTests : IDisposable
    {
        public PublisherTests()
        {
            // Setup subscriber
            _subscriberStreamStore = new InMemoryStreamStore(() => _utcNow);
            _subscriberSettings = new WebHookSubscriberSettings(_subscriberStreamStore);
            var subscriberWebHostBuilder = new WebHostBuilder()
                .UseStartup<WebHookSubscriberStartup>()
                .ConfigureServices(services => services.AddSingleton(_subscriberSettings));
            var subscriberTestServer = new TestServer(subscriberWebHostBuilder);
            _subscriberClient = new HttpClient(subscriberTestServer.CreateHandler())
            {
                BaseAddress = new Uri("http://subscriber.example.com")
            };

            // Setup publisher
            _publisherStreamStore = new InMemoryStreamStore(() => _utcNow);
            var publisherSettings = new WebHookPublisherSettings(_publisherStreamStore)
            {
                HttpMessageHandler = subscriberTestServer.CreateHandler(),
                GetUtcNow = () => _utcNow
            };
            _publisher = new WebHookPublisher(publisherSettings);
            var publisherWebHostBuilder = new WebHostBuilder()
                .UseStartup<WebHookPublisherStartup>()
                .ConfigureServices(services => services.AddSingleton(publisherSettings));
            var publisherTestServer = new TestServer(publisherWebHostBuilder);
            _publisherClient = new HttpClient(publisherTestServer.CreateHandler())
            {
                BaseAddress = new Uri("http://publisher.example.com")
            };
            _publisherClient.DefaultRequestHeaders.Accept.Add(
                MediaTypeWithQualityHeaderValue.Parse("application/json"));
        }


        public void Dispose()
        {
            _publisherClient.Dispose();
            _publisher.Dispose();
            _publisherStreamStore.Dispose();
            _subscriberClient.Dispose();
            _subscriberStreamStore.Dispose();
        }

        private readonly InMemoryStreamStore _subscriberStreamStore;
        private readonly InMemoryStreamStore _publisherStreamStore;
        private readonly HttpClient _publisherClient;
        private readonly HttpClient _subscriberClient;
        private readonly WebHookPublisher _publisher;
        private DateTime _utcNow = new DateTime(2017, 1, 1);
        private readonly WebHookSubscriberSettings _subscriberSettings;

        private static AddWebHookRequest CreateAddWebHookRequest(
            string payloadTargetUri = "http://subscriber.example.com/sink",
            string secret = "secret")
        {
            var addWebHook = new AddWebHookRequest
            {
                PayloadTargetUri = new Uri(payloadTargetUri),
                Enabled = true,
                SubscribeToEvents = new[] {"foo", "bar"},
                SubscriptionChoice = SubscriptionChoice.SelectedEvents,
                Secret = secret
            };
            return addWebHook;
        }

        private async Task<Uri> CreateWebHook(AddWebHookRequest request)
        {
            return (await _publisherClient.PostAsJson(request, "hooks")).Headers.Location;
        }

        [Fact]
        public async Task Can_add_webhook()
        {
            var addWebHook = CreateAddWebHookRequest();

            var response = await _publisherClient.PostAsJson(addWebHook, "hooks");

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            response.Headers.Location.ShouldNotBe(null);
        }

        [Fact]
        public async Task Can_delete_webhook()
        {
            var request = CreateAddWebHookRequest();
            var response = await _publisherClient.PostAsJson(request, "hooks");
            var webHookUri = $"hooks/{response.Headers.Location}";

            response = await _publisherClient.DeleteAsync(webHookUri);
            var getResponse = await _publisherClient.GetAsync(webHookUri);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Can_update_webhook()
        {
            var request = CreateAddWebHookRequest();
            var response = await _publisherClient.PostAsJson(request, "hooks");
            var webHookUri = $"hooks/{response.Headers.Location}";

            var addWebHook = new UpdateWebHookRequest
            {
                PayloadTargetUri = new Uri("http://example.com/sink2"),
                Enabled = false,
                SubscribeToEvents = new[] {"baz"},
                SubscriptionChoice = SubscriptionChoice.Everything,
                Secret = ""
            };

            await _publisherClient.PostAsJson(addWebHook, webHookUri);

            response = await _publisherClient.GetAsync(webHookUri);

            var webHook = await response.Content.ReadAs<WebHook>();

            webHook.Enabled.ShouldBe(addWebHook.Enabled);
            webHook.HasSecret.ShouldBeFalse();
            webHook.PayloadTargetUri.ShouldBe(addWebHook.PayloadTargetUri);
            webHook.SubscribeToEvents.ShouldBe(addWebHook.SubscribeToEvents);
            webHook.CreatedUtc.ShouldBeGreaterThan(DateTime.MinValue);
            webHook.UpdatedUtc.ShouldBeGreaterThan(DateTime.MinValue);
            webHook.SubscriptionChoice.ShouldBe((int) addWebHook.SubscriptionChoice);
        }

        [Fact]
        public async Task Cannot_add_webhooks_that_exceed_max_count()
        {
            var request = CreateAddWebHookRequest();
            for (var i = 0; i < WebHookPublisher.DefaultMaxWebHookCount; i++)
            {
                await _publisherClient.PostAsJson(request, "hooks");
            }

            var response = await _publisherClient.PostAsJson(request, "hooks");

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Should_have_no_webhooks()
        {
            var response = await _publisherClient.GetAsync("hooks");

            var webHooks = await response.Content.ReadAs<WebHook[]>();

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            webHooks.ShouldNotBeNull();
            webHooks.Length.ShouldBe(0);
        }

        [Fact]
        public async Task When_add_webhook_then_can_get_webhook()
        {
            var request = CreateAddWebHookRequest();
            var response = await _publisherClient.PostAsJson(request, "hooks");
            var webHookUri = $"hooks/{response.Headers.Location}";
            response = await _publisherClient.GetAsync(webHookUri);

            var webHook = await response.Content.ReadAs<WebHook>();

            webHook.Enabled.ShouldBe(request.Enabled);
            webHook.HasSecret.ShouldBeTrue();
            webHook.PayloadTargetUri.ShouldBe(request.PayloadTargetUri);
            webHook.SubscribeToEvents.ShouldBe(request.SubscribeToEvents);
            webHook.CreatedUtc.ShouldBeGreaterThan(DateTime.MinValue);
            webHook.UpdatedUtc.ShouldBeGreaterThan(DateTime.MinValue);
            webHook.SubscriptionChoice.ShouldBe((int) request.SubscriptionChoice);
        }

        [Fact]
        public async Task When_add_webhook_then_should_be_in_webhooks_collection()
        {
            var request = CreateAddWebHookRequest();
            await _publisherClient.PostAsJson(request, "hooks");
            var response = await _publisherClient.GetAsync("hooks");

            var webHooks = await response.Content.ReadAs<WebHook[]>();

            webHooks.Length.ShouldBe(1);
            webHooks[0].Enabled.ShouldBe(request.Enabled);
            webHooks[0].HasSecret.ShouldBeTrue();
            webHooks[0].PayloadTargetUri.ShouldBe(request.PayloadTargetUri);
            webHooks[0].SubscribeToEvents.ShouldBe(request.SubscribeToEvents);
            webHooks[0].CreatedUtc.ShouldBeGreaterThan(DateTime.MinValue);
            webHooks[0].UpdatedUtc.ShouldBeGreaterThan(DateTime.MinValue);
            webHooks[0].SubscriptionChoice.ShouldBe((int) request.SubscriptionChoice);
        }

        [Fact]
        public async Task When_delete_non_existent_webhook_then_should_get_not_found()
        {
            var webHookUri = $"hooks/{Guid.NewGuid()}";

            var response = await _publisherClient.DeleteAsync(webHookUri);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task When_event_delivered_should_be_in_delivery_stream()
        {
            var request = new AddSubscriptionRequest
            {
                Name = "foo"
            };
            var addSubscription = await _subscriberClient.PostAsJson(request, "hooks");
            var addSubscriptionResponse = await addSubscription.Content.ReadAs<AddSubscriptionResponse>();
            var hookUri = new Uri(_subscriberClient.BaseAddress, "hooks");
            var payloadTargetUri = new Uri(hookUri, addSubscription.Headers.Location);

            var addWebHookRequest =
                CreateAddWebHookRequest(payloadTargetUri.ToString(), addSubscriptionResponse.Secret);
            var webhookLocation = await CreateWebHook(addWebHookRequest);
            var eventName = "foo";
            var json = "bar";
            await _publisher.QueueEvent(eventName, json);

            await _publisher.DeliverNow();
            var response = await _publisherClient.GetAsync($"hooks/{webhookLocation}/deliveries");
            var deliveryEventsPage = await response.Content.ReadAs<DeliveryEventsPage>();

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            deliveryEventsPage.Items.Length.ShouldBe(1);
            deliveryEventsPage.Items[0].EventName.ShouldBe(eventName);
            deliveryEventsPage.Items[0].EventMessageId.ShouldNotBe(Guid.Empty);
            deliveryEventsPage.Items[0].Success.ShouldBeTrue();
            deliveryEventsPage.Items[0].EventSequence.ShouldBe(0);
        }

        [Fact]
        public async Task When_get_delivery_page_for_non_existant_webhook_should_404()
        {
            var response = await _publisherClient.GetAsync($"hooks/{Guid.NewGuid()}/deliveries");

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task When_get_delivery_page_for_webhook_with_no_delivered_events_should_return_no_items()
        {
            var webhookLocation = await CreateWebHook(CreateAddWebHookRequest());

            var response = await _publisherClient.GetAsync($"hooks/{webhookLocation}/deliveries");
            var deliveryEventsPage = await response.Content.ReadAs<DeliveryEventsPage>();

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            deliveryEventsPage.Next.ShouldBe(-1);
            deliveryEventsPage.Items.ShouldBeNull();
        }


        [Fact]
        public async Task When_get_out_event_page_for_non_existant_webhook_should_404()
        {
            var response = await _publisherClient.GetAsync($"hooks/{Guid.NewGuid()}/out");

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task When_get_out_event_page_for_webhook_with_no_queued_events_should_return_no_items()
        {
            var webhookLocation = await CreateWebHook(CreateAddWebHookRequest());

            var response = await _publisherClient.GetAsync($"hooks/{webhookLocation}/out");
            var deliveryEventsPage = await response.Content.ReadAs<OutEventsPage>();

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            deliveryEventsPage.Next.ShouldBe(0);
            deliveryEventsPage.Items.ShouldBeNull();
        }

        [Fact]
        public async Task When_publish_event_then_should_be_in_out_stream()
        {
            var webhookLocation = await CreateWebHook(CreateAddWebHookRequest());
            var eventName = "foo";
            var json = "bar";
            await _publisher.QueueEvent(eventName, json);

            var response = await _publisherClient.GetAsync($"hooks/{webhookLocation}/out");
            var outBoxSummaryItems = await response.Content.ReadAs<OutEventsPage>();

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            outBoxSummaryItems.Items.Length.ShouldBe(1);
            outBoxSummaryItems.Items[0].MessageId.ShouldNotBe(Guid.Empty);
            outBoxSummaryItems.Items[0].EventName.ShouldBe(eventName);
        }

        [Fact]
        public async Task When_subscriber_returns_error_then_should_retry()
        {
            var request = new AddSubscriptionRequest
            {
                Name = "foo"
            };
            var addSubscription = await _subscriberClient.PostAsJson(request, "hooks");
            var addSubscriptionResponse = await addSubscription.Content.ReadAs<AddSubscriptionResponse>();
            var hookUri = new Uri(_subscriberClient.BaseAddress, "hooks");
            var payloadTargetUri = new Uri(hookUri, addSubscription.Headers.Location);
            var addWebHookRequest =
                CreateAddWebHookRequest(payloadTargetUri.ToString(), addSubscriptionResponse.Secret);
            var webhookLocation = await CreateWebHook(addWebHookRequest);
            var eventName = "foo";
            var json = "bar";
            await _publisher.QueueEvent(eventName, json);


            _subscriberSettings.ReturnErrorOnReceive = true;
            await _publisher.DeliverNow();
            _utcNow = _utcNow.AddSeconds(10);
            _subscriberSettings.ReturnErrorOnReceive = false;
            await _publisher.DeliverNow();

            var response = await _publisherClient.GetAsync($"hooks/{webhookLocation}/deliveries");
            var deliveryEventsPage = await response.Content.ReadAs<DeliveryEventsPage>();

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            deliveryEventsPage.Items.Length.ShouldBe(2);
            deliveryEventsPage.Items[0].EventName.ShouldBe(eventName);
            deliveryEventsPage.Items[0].EventMessageId.ShouldNotBe(Guid.Empty);
            deliveryEventsPage.Items[0].Success.ShouldBeTrue();
            deliveryEventsPage.Items[0].EventSequence.ShouldBe(0);
            deliveryEventsPage.Items[0].ErrorMessage.ShouldBeNullOrWhiteSpace();

            deliveryEventsPage.Items[1].EventName.ShouldBe(eventName);
            deliveryEventsPage.Items[1].EventMessageId.ShouldNotBe(Guid.Empty);
            deliveryEventsPage.Items[1].Success.ShouldBeFalse();
            deliveryEventsPage.Items[1].EventSequence.ShouldBe(0);
            deliveryEventsPage.Items[1].ErrorMessage.ShouldNotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task When_update_non_existent_webhook_then_should_get_not_found()
        {
            var webHookUri = $"hooks/{Guid.NewGuid()}";

            var addWebHook = new UpdateWebHookRequest
            {
                PayloadTargetUri = new Uri("http://example.com/sink2"),
                Enabled = false,
                SubscribeToEvents = new[] {"baz"},
                SubscriptionChoice = SubscriptionChoice.Everything,
                Secret = ""
            };
            var response = await _publisherClient.PostAsJson(addWebHook, webHookUri);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }
}