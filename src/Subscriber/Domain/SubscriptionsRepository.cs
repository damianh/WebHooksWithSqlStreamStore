namespace WebHooks.Subscriber.Domain
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using SqlStreamStore;
    using SqlStreamStore.Streams;
    using WebHooks.Subscriber;

    internal class SubscriptionsRepository
    {
        private readonly IStreamStore _streamStore;
        private readonly string _name;
        private readonly GetUtcNow _getUtcNow;
        private readonly int _maxSubscriptionCount;

        public SubscriptionsRepository(IStreamStore streamStore, string name,
            GetUtcNow getUtcNow, int maxSubscriptionCount)
        {
            _streamStore = streamStore;
            _name = name;
            _getUtcNow = getUtcNow;
            _maxSubscriptionCount = maxSubscriptionCount;
        }

        public async Task<Subscriptions> Load(CancellationToken cancellationToken)
        {
            var page = await _streamStore.ReadStreamBackwards(_name, StreamVersion.End, 1, cancellationToken);
            if (page.Status == PageReadStatus.StreamNotFound)
            {
                await _streamStore.SetStreamMetadata(_name, maxCount: 1, cancellationToken: cancellationToken);
                return new Subscriptions(_getUtcNow, _maxSubscriptionCount);
            }

            var json = await page.Messages[0].GetJsonData(cancellationToken);
            var memento = JsonConvert.DeserializeObject<SubscriptionsMemento>(json);

            return new Subscriptions(memento, _getUtcNow, _maxSubscriptionCount);
        }

        public async Task Save(Subscriptions subscriptions, CancellationToken cancellationToken)
        {
            var memento = subscriptions.ToMemento();
            var json = JsonConvert.SerializeObject(memento, WebHookSubscriber.SerializerSettings);
            var newStreamMessage = new NewStreamMessage(Guid.NewGuid(), "WebHookSubsriptionsMemento", json);
            
            // TODO: are we interested in concurrency handling here? Should be low traffic...
            await _streamStore.AppendToStream(_name, ExpectedVersion.Any, newStreamMessage, cancellationToken);
        }
    }
}