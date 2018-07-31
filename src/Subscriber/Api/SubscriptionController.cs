using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SqlStreamStore;
using SqlStreamStore.Streams;
using WebHooks.Subscriber.Domain;

namespace WebHooks.Subscriber.Api
{
    [Route("hooks")]
    internal class SubscriptionController : ControllerBase
    {
        private readonly SubscriptionsRepository _repository;
        private readonly ShouldReturnErrorOnReceive _shouldReturnErrorOnReceive;
        private readonly IStreamStore _streamStore;
        private readonly WebHookHeaders _webHookHeaders;

        public SubscriptionController(
            IStreamStore streamStore,
            SubscriptionsRepository repository,
            ShouldReturnErrorOnReceive shouldReturnErrorOnReceive,
            WebHookHeaders webHookHeaders)
        {
            _streamStore = streamStore;
            _repository = repository;
            _shouldReturnErrorOnReceive = shouldReturnErrorOnReceive;
            _webHookHeaders = webHookHeaders;
        }

        [HttpGet("")]
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

        [HttpPost("")]
        public async Task<IActionResult> AddSubscription(
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

        [HttpGet("{id}")]
        public async Task<WebHookSubscription> GetSubscription(
            Guid id,
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

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSubscription(
            Guid id,
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

        [HttpPost("{id}")]
        public async Task<IActionResult> ReceiveEvent(
            Guid id,
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
                return StatusCode(500);
            }

            if (!Request.Headers.TryGetValue(_webHookHeaders.EventNameHeader, out var variables))
            {
                return BadRequest("");
            }

            var eventName = variables.Single();

            if (!Request.Headers.TryGetValue(_webHookHeaders.MessageIdHeader, out variables))
            {
                return BadRequest("");
            }

            var messageId = Guid.Parse(variables.Single());

            if (!Request.Headers.TryGetValue(_webHookHeaders.SequenceHeader, out variables))
            {
                return BadRequest("");
            }
            // Sequence usage _may_ be used to detect lost / skipped messages. 
            // That's for you to implement...

            if (!Request.Headers.TryGetValue(_webHookHeaders.SignatureHeader, out var values))
            {
                return BadRequest("");
            }

            var signature = values.Single();

            string body;
            using (var streamReader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                body = await streamReader.ReadToEndAsync();
            }
            var expectedSignature = PayloadSignature.CreateSignature(body, subscription.Secret);
            if (!signature.Equals(expectedSignature))
            {
                return BadRequest("");
            }

            var newStreamMessage =
                new NewStreamMessage(messageId, eventName, body); // using same message id allows idempotency
            await _streamStore.AppendToStream(subscription.GetInboxStreamId(), ExpectedVersion.Any,
                newStreamMessage, cancellationToken);

            return Ok();
        }
    }
}