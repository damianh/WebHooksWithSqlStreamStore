using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using SqlStreamStore;
using SqlStreamStore.Streams;
using WebHooks.Publisher.Domain;

namespace WebHooks.Publisher.Api
{
    [Route("hooks")]
    internal class PublisherController : ControllerBase
    {
        private const int PageSize = 50;
        private readonly IStreamStore _streamStore;
        private readonly WebHooksRepository _webHooksRepository;

        public PublisherController(IStreamStore streamStore, WebHooksRepository webHooksRepository)
        {
            _streamStore = streamStore;
            _webHooksRepository = webHooksRepository;
        }

        [HttpGet]
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
        public async Task<IActionResult> AddWebHook(
            [FromBody]AddWebHookRequest request,
            CancellationToken cancellationToken)
        {
            var webHooks = await _webHooksRepository.Load(cancellationToken);
            var result = webHooks.Add(
                request.PayloadTargetUri,
                request.Enabled,
                request.SubscribeToEvents,
                request.SubscriptionChoice,
                request.Secret);

            if (result.LimitReached)
            {
                return BadRequest("Webhook limit reached.");
            }

            await _webHooksRepository.Save(webHooks, cancellationToken);

            return Created(new Uri(result.WebHook.Id.ToString(), UriKind.Relative), null);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetWebHook(Guid id, CancellationToken cancellationToken)
        {
            var webHooks = await _webHooksRepository.Load(cancellationToken);
            if (webHooks.TryGet(id, out var w))
            {
                var webHook = new WebHook
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
                return new ObjectResult(webHook);
            }

            return NotFound();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteWebHook(Guid id, CancellationToken cancellationToken)
        {
            var webHooks = await _webHooksRepository.Load(cancellationToken);
            if (!webHooks.Delete(id))
            {
                return NotFound();
            }
            await _webHooksRepository.Save(webHooks, cancellationToken);
            return Ok();
        }

        [HttpPost("{id}")]
        public async Task<IActionResult> UpdateWebHook(
            Guid id,
            [FromBody] UpdateWebHookRequest request,
            CancellationToken cancellationToken)
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


        [HttpGet("{id}/out")]
        public async Task<IActionResult> GetOutStreamPage(
            Guid id,
            int? start = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var fromVersionInclusive = start ?? StreamVersion.Start;

            var webHooks = await _webHooksRepository.Load(cancellationToken);
            if (!webHooks.TryGet(id, out var w))
            {
                return NotFound();
            }
            var streamId = w.StreamIds.OutStreamId;
            var page = await _streamStore
                .ReadStreamForwards(streamId, fromVersionInclusive, PageSize, true, cancellationToken);
            if (page.Status == PageReadStatus.StreamNotFound)
            {
                return new ObjectResult(new OutEventsPage
                {
                    Next = StreamVersion.Start
                });
            }
            var items = page.Messages.Select(m => new OutEventItem
            {
                MessageId = m.MessageId,
                CreatedUtc = m.CreatedUtc,
                EventName = m.Type,
                Sequence = m.StreamVersion
            }).ToArray();
            return new ObjectResult(new OutEventsPage
            {
                Items = items,
                Next = items.Last().Sequence
            });
        }

        [HttpGet("{id}/deliveries")]
        public async Task<IActionResult> GetDeliveriesPage(Guid id, int? start = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var fromVersionInclusive = start ?? StreamVersion.End;

            var webHooks = await _webHooksRepository.Load(cancellationToken);
            if (!webHooks.TryGet(id, out var w))
            {
                return NotFound();
            }
            var streamId = w.StreamIds.DeliveriesStreamId;
            var page = await _streamStore.ReadStreamBackwards(streamId, fromVersionInclusive, PageSize, true,
                cancellationToken);
            if (page.Status == PageReadStatus.StreamNotFound)
            {
                return new ObjectResult(new DeliveryEventsPage
                {
                    Next = StreamVersion.End
                });
            }

            var items = page.Messages.Select(m =>
            {
                var deliveryMetadata =
                    JsonConvert.DeserializeObject<DeliveryMetadata>(m.JsonMetadata,
                        WebHookPublisher.SerializerSettings);

                return new DeliveryEventItem
                {
                    MessageId = m.MessageId,
                    EventMessageId = deliveryMetadata.EventId,
                    EventSequence = deliveryMetadata.Sequence,
                    Success = deliveryMetadata.DeliverySuccess,
                    CreatedUtc = m.CreatedUtc,
                    EventName = m.Type,
                    Sequence = m.StreamVersion,
                    ErrorMessage = deliveryMetadata.ErrorMessage
                };
            }).ToArray();
            return new ObjectResult(new DeliveryEventsPage
            {
                Items = items,
                Next = items.Last().Sequence
            });
        }
    }
}