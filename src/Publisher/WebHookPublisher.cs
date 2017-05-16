namespace WebHooks.Publisher
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Http;
    using System.Web.Http.Dispatcher;
    using LightInject;
    using Microsoft.Owin.Builder;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;
    using Owin;
    using SqlStreamStore;
    using SqlStreamStore.Streams;
    using WebHooks.Publisher.Api;
    using WebHooks.Publisher.Domain;

    public class WebHookPublisher : IDisposable
    {
        private readonly WebHookPublisherSettings _settings;
        private readonly IStreamStore _streamStore;
        public const int DefaultMaxWebHookCount = 10;
        public static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
#if DEBUG
            Formatting = Formatting.Indented //indentation bloats the message size, only use in debug mode.
#endif
        };

        private readonly WebHooksRepository _webHooksRepository;
        private readonly CancellationTokenSource _disposed = new CancellationTokenSource();
        private readonly HttpClient _httpClient;
        private readonly WebHookHeaders _webHookHeaders;

        public WebHookPublisher(WebHookPublisherSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _webHooksRepository = new WebHooksRepository(settings.StreamStore, "webhooks", settings.GetUtcNow, settings.MaxWebHookCount);
            _httpClient = new HttpClient(settings.HttpMessageHandler);
            _streamStore = settings.StreamStore;
            _webHookHeaders = new WebHookHeaders(settings.Vendor);

            var config = CreateHttpConfiguration();
            var appBuilder = new AppBuilder();
            appBuilder.UseWebApi(config);
            AppFunc = appBuilder.Build();
        }

        public Func<IDictionary<string, object>, Task> AppFunc { get; }

        public async Task QueueEvent(Guid messageId, string eventName,
            string jsonData, CancellationToken cancellationToken = default(CancellationToken))
        {
            var webHooks = await _webHooksRepository.Load(cancellationToken);
            var subscribers = webHooks
                .Items
                .Where(w => w.SubscriptionChoice == SubscriptionChoice.Everything ||
                             w.SubscribeToEvents.Contains(eventName) && w.Enabled)
                .ToArray();

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposed.Token, cancellationToken))
            {
                foreach (var subscriber in subscribers)
                {
                    var newStreamMessage = new NewStreamMessage(messageId, eventName, jsonData);
                    var streamId = subscriber.StreamIds.OutStreamId;
                    await _streamStore.AppendToStream(streamId, ExpectedVersion.Any, newStreamMessage, cts.Token);
                }
            }
        }

        public async Task DeliverNow(CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposed.Token, cancellationToken))
            {
                bool messageSent;
                do
                {
                    messageSent = false;
                    var webHooksToProcess = (await _webHooksRepository.Load(cancellationToken))
                        .Items.Where(w => w.Enabled).ToList();

                    foreach (var webHook in webHooksToProcess)
                    {
                        if (cts.Token.IsCancellationRequested)
                        {
                            return;
                        }
                        var streamIds = webHook.StreamIds;

                        var nextMessageToDeliver = await GetNextMessageToDeliver(streamIds.OutStreamId, cts.Token);
                        if (!nextMessageToDeliver.Found)
                        {
                            continue;
                        }

                        var streamMessageToDeliver = nextMessageToDeliver.StreamMessage;
                        var previousDeliveryInfo = await GetPreviousDeliveryInfo(streamIds.DeliveriesStreamId, cancellationToken);

                        if (!previousDeliveryInfo.Success && previousDeliveryInfo.EventId == streamMessageToDeliver.MessageId)
                        {
                            // Should we attempt to deliver?
                            var timeSinceLastAttempt = _settings.GetUtcNow() - previousDeliveryInfo.DeliveryDateUtc;
                            if (timeSinceLastAttempt > _settings.MaxDeliveryAttemptDuration)
                            {
                                var webHooks = await _webHooksRepository.Load(cancellationToken);
                                webHooks.Disable(webHook.Id);
                            }

                            var delay = CalculateDelay(previousDeliveryInfo.DeliveryAttemptCount, _settings.MaxRetryDelay);
                            if (_settings.GetUtcNow() - previousDeliveryInfo.DeliveryDateUtc < delay)
                            {
                                // Too soon to attempt retry;
                                continue;
                            }
                        }

                        // Forward the message to the subscriber.
                        var jsonData = await streamMessageToDeliver.GetJsonData(cts.Token);
                        var request = CreateSubscriberRequest(webHook, jsonData, streamMessageToDeliver, webHook.Secret);

                        var response = await _httpClient.SendAsync(request, cts.Token); //TODO try-catch
                        var deliveryMetadata = new DeliveryMetadata
                        {
                            AttemptCount = previousDeliveryInfo.DeliveryAttemptCount + 1,
                            DeliverySuccess = true,
                            EventId = streamMessageToDeliver.MessageId,
                            Sequence = streamMessageToDeliver.StreamVersion,
                        };
                        if ((int) response.StatusCode > 299)
                        {
                            // Delivery failed, write error delivery message
                            deliveryMetadata.DeliverySuccess = false;
                            deliveryMetadata.ErrorMessage = $"Subscriber returned status code {response.StatusCode}";
                            await AppendDeliveryMessage(deliveryMetadata, streamIds.DeliveriesStreamId,
                                streamMessageToDeliver.Type, jsonData, cancellationToken);
                        }
                        else
                        {
                            await AppendDeliveryMessage(deliveryMetadata, streamIds.DeliveriesStreamId,
                                streamMessageToDeliver.Type, jsonData, cancellationToken);
                            // Remove the sent messsage from the out stream
                            await _streamStore.DeleteMessage(streamIds.OutStreamId, streamMessageToDeliver.MessageId, cts.Token);
                            messageSent = true;
                        }
                    }

                } while (messageSent);
            }
        }

        private static TimeSpan CalculateDelay(int attemptCount, TimeSpan maxDelay)
        {
            var delay = Math.Pow(2d, attemptCount);
            return TimeSpan.FromSeconds(Math.Min(delay, maxDelay.TotalSeconds));
        }

        private HttpRequestMessage CreateSubscriberRequest( WebHooks.WebHook webHook, string jsonData,
            StreamMessage streamMessageToDeliver, string webhookSecret)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, webHook.PayloadTargetUri)
            {
                Content = new StringContent(jsonData, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation(_webHookHeaders.EventNameHeader, streamMessageToDeliver.Type);
            request.Headers.TryAddWithoutValidation(_webHookHeaders.MessageIdHeader,
                streamMessageToDeliver.MessageId.ToString("d"));
            request.Headers.TryAddWithoutValidation(_webHookHeaders.SequenceHeader,
                streamMessageToDeliver.StreamVersion.ToString());
            if (!string.IsNullOrWhiteSpace(webhookSecret))
            {
                var signature = PayloadSignature.CreateSignature(jsonData, webhookSecret);
                request.Headers.TryAddWithoutValidation(_webHookHeaders.SignatureHeader, signature);
            }
            return request;
        }

        private async Task AppendDeliveryMessage(DeliveryMetadata deliveryMetadata, string deliveryStreamId,
            string messageType, string eventDataJson, CancellationToken cancellationToken)
        {
            var jsonMeta = JsonConvert.SerializeObject(deliveryMetadata, SerializerSettings);
            var newStreamMessage = new NewStreamMessage(Guid.NewGuid(), messageType, eventDataJson, jsonMeta);
            await _streamStore.AppendToStream(deliveryStreamId, ExpectedVersion.Any, newStreamMessage,
                cancellationToken);
        }

        private async Task<(bool Found, StreamMessage StreamMessage)> GetNextMessageToDeliver(
            string streamId, CancellationToken cancellationToken)
        {
            var page = await _streamStore.ReadStreamForwards(streamId, StreamVersion.Start, 1, cancellationToken);
            if (page.Status == PageReadStatus.StreamNotFound || page.Messages.Length == 0)
            {
                return (false, default(StreamMessage));
            }
            return (true, page.Messages[0]);
        }

        private async Task<PreviousDeliveryInfo> GetPreviousDeliveryInfo(string deliveryStreamId, CancellationToken cancellationToken)
        {
            var page = await _streamStore.ReadStreamBackwards(deliveryStreamId, StreamVersion.End, 1, cancellationToken);
            if (page.Status == PageReadStatus.StreamNotFound || page.Messages.Length == 0)
            {
                return new PreviousDeliveryInfo(true, DateTime.MinValue, 0, Guid.Empty);
            }
            var streamMessage = page.Messages[0];
            var deliveryMetadata = JsonConvert.DeserializeObject<DeliveryMetadata>(streamMessage.JsonMetadata, SerializerSettings);
            return new PreviousDeliveryInfo(deliveryMetadata.DeliverySuccess, streamMessage.CreatedUtc, deliveryMetadata.AttemptCount, deliveryMetadata.EventId);
        }

        private HttpConfiguration CreateHttpConfiguration()
        {
            var config = new HttpConfiguration();
            var container = new ServiceContainer();
            container.RegisterInstance(_streamStore);
            container.RegisterInstance(_webHooksRepository);
            var controllerTypeResolver = new ControllerTypeResolver();
            config.Services.Replace(typeof(IHttpControllerTypeResolver), controllerTypeResolver);
            foreach (var controllerType in controllerTypeResolver.GetControllerTypes(null))
            {
                container.Register(controllerType, new PerRequestLifeTime());
            }
            container.EnableWebApi(config);
            config.MapHttpAttributeRoutes();
            config.Formatters.JsonFormatter.SerializerSettings = SerializerSettings; 
            return config;
        }

        public void Dispose()
        {
            _disposed.Cancel();
            _httpClient.Dispose();
        }

        private class ControllerTypeResolver : IHttpControllerTypeResolver
        {
            // We want to be very explicit which controllers we want to use.
            // Also we want our controllers internal.

            public ICollection<Type> GetControllerTypes(IAssembliesResolver _)
            {
                return new[] { typeof(PublisherController) };
            }
        }

        private class PreviousDeliveryInfo
        {
            /// <summary>
            ///     Represents information about a delivered event (or attempted delivery).
            /// </summary>
            /// <param name="success">True if last delivery was successful</param>
            /// <param name="deliveryDateUtc">The date and time the delivery occured (or was attemted)</param>
            /// <param name="deliveryAttemptCount">The attempt count of the delivery. 0 if delivery was successful first time.</param>
            /// <param name="eventId">The event ID that the delivery is related to.</param>
            public PreviousDeliveryInfo(bool success, DateTime deliveryDateUtc, int deliveryAttemptCount, Guid eventId)
            {
                Success = success;
                DeliveryDateUtc = deliveryDateUtc;
                DeliveryAttemptCount = deliveryAttemptCount;
                EventId = eventId;
            }

            public bool Success { get; }

            public DateTime DeliveryDateUtc { get; }

            public int DeliveryAttemptCount { get; }

            public Guid EventId { get; }
        }
    }
}