namespace WebHooks.Publisher.Api
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Http;
    using Newtonsoft.Json;
    using SqlStreamStore;
    using SqlStreamStore.Streams;
    using WebHooks.Publisher.Domain;

    [RoutePrefix("hooks")]
    internal class PublisherController : ApiController
    {
        private readonly IStreamStore _streamStore;
        private readonly WebHooksRepository _webHooksRepository;
        private const int PageSize = 50;

        public PublisherController(IStreamStore streamStore, WebHooksRepository webHooksRepository)
        {
            _streamStore = streamStore;
            _webHooksRepository = webHooksRepository;
        }

        [HttpGet]
        [Route("")]
        public async Task<WebHook[]> ListWebHooks(CancellationToken cancellationToken)
        {
            var webHooks = await _webHooksRepository.Load(cancellationToken);
            return webHooks.Items.Select(w => new WebHook
            {
                Id = w.Id.ToString(),
                PayloadTargetUri = w.PayloadTargetUri,
                SubscribeToEvents = w.SubscribeToEvents.ToArray(),
                SubscriptionChoice = (int) w.SubscriptionChoice,
                Enabled = w.Enabled,
                UpdatedUtc = w.UpdatedUtc,
                CreatedUtc = w.CreatedUtc,
                HasSecret = !string.IsNullOrWhiteSpace(w.Secret)
            }).ToArray();
        }

        [HttpPost]
        [Route("")]
        public async Task<HttpResponseMessage> AddWebHook([FromBody]AddWebHookRequest request, CancellationToken cancellationToken)
        {
            var webHooks = await _webHooksRepository.Load(cancellationToken);
            var result = webHooks.Add(request.PayloadTargetUri, request.Enabled,
                request.SubscribeToEvents, request.SubscriptionChoice, request.Secret);

            if (result.LimitReached)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden); //Consider API problem detail instead.
            }

            await _webHooksRepository.Save(webHooks, cancellationToken);

            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.Location = new Uri(result.WebHook.Id.ToString(), UriKind.Relative);
            return response;
        }

        [HttpGet]
        [NullFilter]
        [Route("{id}")]
        public async Task<WebHook> GetWebHook([FromUri]Guid id, CancellationToken cancellationToken)
        {
            var webHooks = await _webHooksRepository.Load(cancellationToken);
            if (webHooks.TryGet(id, out var w))
            {
                return new WebHook
                {
                    Id = w.Id.ToString(),
                    PayloadTargetUri = w.PayloadTargetUri,
                    SubscribeToEvents = w.SubscribeToEvents.ToArray(),
                    SubscriptionChoice = (int) w.SubscriptionChoice,
                    Enabled = w.Enabled,
                    UpdatedUtc = w.UpdatedUtc,
                    CreatedUtc = w.CreatedUtc,
                    HasSecret = !string.IsNullOrWhiteSpace(w.Secret)
                };
            }
            return null;
        }

        [HttpDelete]
        [Route("{id}")]
        public async Task<IHttpActionResult> DeleteWebHook([FromUri]Guid id, CancellationToken cancellationToken)
        {
            var webHooks = await _webHooksRepository.Load(cancellationToken);
            if (!webHooks.Delete(id))
            {
                return NotFound();
            }
            await _webHooksRepository.Save(webHooks, cancellationToken);
            return Ok();
        }

        [HttpPost]
        [Route("{id}")]
        public async Task<IHttpActionResult> UpdateWebHook(Guid id,
            [FromBody] UpdateWebHookRequest request, CancellationToken cancellationToken)
        {
            var webHooks = await _webHooksRepository.Load(cancellationToken);
            if (!webHooks.Update(id, request.PayloadTargetUri, request.SubscribeToEvents,
                request.Enabled, request.SubscriptionChoice, request.Secret).Exists)
            {
                return NotFound();
            }
            await _webHooksRepository.Save(webHooks, cancellationToken);
            return Ok();
        }


        [HttpGet]
        [NullFilter]
        [Route("{id}/out")]
        public async Task<OutEventsPage> GetOutStreamPage(Guid id, int? start = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var fromVersionInclusive = start ?? StreamVersion.Start;

            var webHooks = await _webHooksRepository.Load(cancellationToken);
            if (!webHooks.TryGet(id, out var w))
            {
                return null;
            }
            var streamId = w.StreamIds.OutStreamId;
            var page = await _streamStore.ReadStreamForwards(streamId, fromVersionInclusive, PageSize, true, cancellationToken);
            if (page.Status == PageReadStatus.StreamNotFound)
            {
                return new OutEventsPage
                {
                    Next = StreamVersion.Start
                };
            }
            var items = page.Messages.Select(m => new OutEventItem
            {
                MessageId = m.MessageId,
                CreatedUtc = m.CreatedUtc,
                EventName = m.Type,
                Sequence = m.StreamVersion
            }).ToArray();
            return new OutEventsPage
            {
                Items = items,
                Next = items.Last().Sequence
            };
        }

        [HttpGet]
        [NullFilter]
        [Route("{id}/deliveries")]
        public async Task<DeliveryEventsPage> GetDeliveriesPage(Guid id, int? start = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var fromVersionInclusive = start ?? StreamVersion.End;

            var webHooks = await _webHooksRepository.Load(cancellationToken);
            if (!webHooks.TryGet(id, out var w))
            {
                return null;
            }
            var streamId = w.StreamIds.DeliveriesStreamId;
            var page = await _streamStore.ReadStreamBackwards(streamId, fromVersionInclusive, PageSize, true, cancellationToken);
            if (page.Status == PageReadStatus.StreamNotFound)
            {
                return new DeliveryEventsPage
                {
                    Next = StreamVersion.End
                };
            }
            var items = page.Messages.Select(m =>
            {
                var deliveryMetadata = JsonConvert.DeserializeObject<DeliveryMetadata>(m.JsonMetadata, WebHookPublisher.SerializerSettings);

                return new DeliveryEventItem
                {
                    MessageId = m.MessageId,
                    EventMessageId = deliveryMetadata.EventId,
                    EventSequence = deliveryMetadata.Sequence,
                    Success = deliveryMetadata.DeliverySuccess,
                    CreatedUtc = m.CreatedUtc,
                    EventName = m.Type,
                    Sequence = m.StreamVersion,
                    ErrorMessage = deliveryMetadata.ErrorMessage,
                };
            }).ToArray();
            return new DeliveryEventsPage
            {
                Items = items,
                Next = items.Last().Sequence
            };
        }
    }
}