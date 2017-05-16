namespace WebHooks.Subscriber.Api
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Http;
    using SqlStreamStore;
    using SqlStreamStore.Streams;
    using WebHooks.Subscriber.Domain;

    [RoutePrefix("hooks")]
    internal class SubscriptionController : ApiController
    {
        private readonly IStreamStore _streamStore;
        private readonly SubscriptionsRepository _repository;
        private readonly ShouldReturnErrorOnReceive _shouldReturnErrorOnReceive;
        private readonly WebHookHeaders _webHookHeaders;

        public SubscriptionController(IStreamStore streamStore, SubscriptionsRepository repository,
            ShouldReturnErrorOnReceive shouldReturnErrorOnReceive, WebHookHeaders webHookHeaders)
        {
            _streamStore = streamStore;
            _repository = repository;
            _shouldReturnErrorOnReceive = shouldReturnErrorOnReceive;
            _webHookHeaders = webHookHeaders;
        }

        [HttpGet]
        [Route("")]
        public async Task<WebHookSubscription[]> ListSubscriptions(CancellationToken cancellationToken)
        {
            var subscriptions = await _repository.Load(cancellationToken);

            return subscriptions
                .Items
                .Select(i => new WebHookSubscription
                {
                    Name = i.Name,
                    CreatedUtc = i.CreatedUtc,
                    PayloadTargetRelativeUri = $"hooks/{i.Id}"
                }).ToArray();
        }

        [HttpPost]
        [Route("")]
        public async Task<IHttpActionResult> AddSubscription(
            [FromBody] AddSubscriptionRequest request,
            CancellationToken cancellationToken)
        {
            var subscriptions = await _repository.Load(cancellationToken);
            var subscription = subscriptions.Add(request.Name);
            await _repository.Save(subscriptions, cancellationToken);

            var response = new AddSubscriptionResponse
            {
                Id = subscription.Id,
                Name = subscription.Name,
                Secret = subscription.Secret,
                PayloadTargetRelativeUri = $"hooks/{subscription.Id}"
            };

            return Created(response.PayloadTargetRelativeUri, response);
        }

        [HttpGet]
        [NullFilter]
        [Route("{id}")]
        public async Task<WebHookSubscription> GetSubscription(
            [FromUri] Guid id,
            CancellationToken cancellationToken)
        {
            var subscriptions = await _repository.Load(cancellationToken);
            var subscription = subscriptions.Get(id);

            return new WebHookSubscription
            {
                Name = subscription.Name,
                CreatedUtc = subscription.CreatedUtc,
                PayloadTargetRelativeUri = $"hooks/{subscription.Id}"
            };
        }

        [HttpDelete]
        [Route("{id}")]
        public async Task<IHttpActionResult> DeleteSubscription(
            [FromUri] Guid id,
            CancellationToken cancellationToken)
        {
            var subscriptions = await _repository.Load(cancellationToken);
            var subscription = subscriptions.Get(id);
            var deleted = subscriptions.Delete(id);
            if (!deleted)
            {
                return NotFound();
            }
            await _repository.Save(subscriptions, cancellationToken);

            // delete the inbox
            var streamId = subscription.GetInboxStreamId();
            await _streamStore.DeleteStream(streamId, cancellationToken: cancellationToken);

            return Ok();
        }

        [HttpPost]
        [Route("{id}")]
        public async Task<IHttpActionResult> ReceiveEvent(
            [FromUri] Guid id,
            CancellationToken cancellationToken)
        {
            var subscriptions = await _repository.Load(cancellationToken);
            var subscription = subscriptions.Get(id);

            if (subscription == null)
            {
                return NotFound();
            }

            if (_shouldReturnErrorOnReceive())
            {
                throw new Exception("Receive Error");
            }

            IEnumerable<string> variables;
            if (!Request.Headers.TryGetValues(_webHookHeaders.EventNameHeader, out variables))
            {
                return BadRequest("");
            }
            var eventName = variables.Single();

            if (!Request.Headers.TryGetValues(_webHookHeaders.MessageIdHeader, out variables))
            {
                return BadRequest("");
            }
            var messageId = Guid.Parse(variables.Single());

            if (!Request.Headers.TryGetValues(_webHookHeaders.SequenceHeader, out variables))
            {
                return BadRequest("");
            }
            // Sequence usage _may_ be used to detect lost / skipped messages. 
            // That's for you to implement...

            if (!Request.Headers.TryGetValues(_webHookHeaders.SignatureHeader, out var values))
            {
                return BadRequest("");
            }
            var signature = values.Single();
            var body = await Request.Content.ReadAsStringAsync();

            var expectedSignature = PayloadSignature.CreateSignature(body, subscription.Secret);
            if (!signature.Equals(expectedSignature))
            {
                return BadRequest("");
            }

            var newStreamMessage = new NewStreamMessage(messageId, eventName, body); // using same message id allows idempotency
            await _streamStore.AppendToStream(subscription.GetInboxStreamId(), ExpectedVersion.Any,
                newStreamMessage, cancellationToken);

            return Ok();
        }
    }
}