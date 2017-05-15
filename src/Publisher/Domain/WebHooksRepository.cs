namespace WebHooks.Publisher.Domain
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using SqlStreamStore;
    using SqlStreamStore.Streams;

    internal class WebHooksRepository
    {
        private readonly IStreamStore _streamStore;
        private readonly string _name;
        private readonly GetUtcNow _getUtcNow;
        private readonly int _maxWebHookCount;

        public WebHooksRepository(IStreamStore streamStore, string name, GetUtcNow getUtcNow, int maxWebHookCount)
        {
            _streamStore = streamStore;
            _name = name;
            _getUtcNow = getUtcNow;
            _maxWebHookCount = maxWebHookCount;
        }

        public async Task<WebHooks> Load(CancellationToken cancellationToken)
        {
            var page = await _streamStore.ReadStreamBackwards(_name, StreamVersion.End, 1, cancellationToken);
            if (page.Status == PageReadStatus.StreamNotFound)
            {
                await _streamStore.SetStreamMetadata(_name, maxCount: 1, cancellationToken: cancellationToken);
                return new WebHooks(_getUtcNow, _maxWebHookCount);
            }

            var json = await page.Messages[0].GetJsonData(cancellationToken);
            var memento = JsonConvert.DeserializeObject<WebHooksMemento>(json);

            return new WebHooks(memento, _getUtcNow, _maxWebHookCount);
        }

        public async Task Save(WebHooks webHooks, CancellationToken cancellationToken)
        {
            var memento = webHooks.ToMemento();
            var json = JsonConvert.SerializeObject(memento, WebHookPublisher.SerializerSettings);
            var newStreamMessage = new NewStreamMessage(Guid.NewGuid(), "WebHooksMemento", json);
            
            // TODO: are we interested in concurrency handling here? Should be low traffic...
            await _streamStore.AppendToStream(_name, ExpectedVersion.Any, newStreamMessage, cancellationToken);
        }
    }
}