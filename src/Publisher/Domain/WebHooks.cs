namespace WebHooks.Publisher.Domain
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    internal class WebHooks
    {
        private readonly GetUtcNow _getUtcNow;
        private readonly int _maxCount;
        private readonly Dictionary<Guid, WebHook> _webHooks = new Dictionary<Guid, WebHook>();

        public WebHooks(GetUtcNow getUtcNow, int maxCount)
        {
            _getUtcNow = getUtcNow;
            _maxCount = maxCount;
        }

        public WebHooks(WebHooksMemento webHooksMemento, GetUtcNow getUtcNow, int maxCount)
            : this(getUtcNow, maxCount)
        {
            _webHooks = webHooksMemento
                .Items
                .Select(w => new WebHook(w))
                .ToDictionary(w => w.Id, w => w);
        }

        public ICollection<WebHook> Items => _webHooks.Values;

        public (bool LimitReached, WebHook WebHook) Add(
            Uri payloadTargetUri,
            bool enabled,
            string[] subscribedEvents,
            SubscriptionChoice subscriptionChoice,
            string secret)
        {
            if (_webHooks.Count >= _maxCount)
            {
                // Always limit the number of webhooks. Should probably throw a custom exception too.
                return (true, null);
            }
            subscribedEvents = subscribedEvents ?? Array.Empty<string>();
            var now = _getUtcNow();
            var item = new WebHookMemento
            {
                Id = Guid.NewGuid(),
                PayloadTargetUri = payloadTargetUri,
                CreatedUtc = now,
                SubscribeToEvents = new HashSet<string>(subscribedEvents),
                SubscriptionChoice = subscriptionChoice,
                Enabled = enabled,
                UpdatedUtc = now,
                Secret = secret
            };
            var webHook = new WebHook(item);
            _webHooks.Add(webHook.Id, webHook);
            return (false, webHook);
        }

        public (bool Exists, WebHook WebHook) Update(
            Guid webhookId,
            Uri payloadTargetUri,
            string[] subscribedEvents,
            bool enabled,
            SubscriptionChoice subscriptionChoice,
            string secret)
        {
            if (!_webHooks.ContainsKey(webhookId))
            {
                return (false, null);
            }
            var webHook = _webHooks[webhookId];
            webHook.PayloadTargetUri = payloadTargetUri;
            webHook.SubscribeToEvents = new HashSet<string>(subscribedEvents);
            webHook.Enabled = enabled;
            webHook.SubscriptionChoice = subscriptionChoice;
            if (secret != null)
            {
                webHook.Secret = secret;
            }
            return (true, webHook);
        }

        public (bool Exists, WebHook WebHook) Disable(Guid webhookId)
        {
            if (!_webHooks.ContainsKey(webhookId))
            {
                return (false, null);
            }
            var webHook = _webHooks[webhookId];
            webHook.Enabled = false;
            return (true, webHook);
        }

        public WebHooksMemento ToMemento() => new WebHooksMemento
        {
            Items = _webHooks.Values.Select(w => w.GetMemento()).ToArray()
        };

        public bool TryGet(Guid id, out WebHook webHook)
        {
            return _webHooks.TryGetValue(id, out webHook);
        }

        public bool Delete(Guid id)
        {
            if (!_webHooks.ContainsKey(id))
            {
                return false;
            }
            _webHooks.Remove(id);
            return true;
        }

        internal class WebHook
        {
            private readonly WebHookMemento _memento;

            internal WebHook(WebHookMemento memento)
            {
                _memento = memento;
            }

            public Guid Id => _memento.Id;

            public Uri PayloadTargetUri
            {
                get => _memento.PayloadTargetUri;
                set => _memento.PayloadTargetUri = value;
            }

            public bool Enabled
            {
                get => _memento.Enabled;
                set => _memento.Enabled = value;
            }

            public SubscriptionChoice SubscriptionChoice
            {
                get => _memento.SubscriptionChoice;
                set => _memento.SubscriptionChoice = value;
            }

            public HashSet<string> SubscribeToEvents
            {
                get => _memento.SubscribeToEvents;
                set => _memento.SubscribeToEvents = value;
            }

            public DateTime CreatedUtc => _memento.CreatedUtc;

            public DateTime UpdatedUtc => _memento.UpdatedUtc;

            public string Secret
            {
                get => _memento.Secret;
                set => _memento.Secret = value;
            }

            public (string OutStreamId, string DeliveriesStreamId) StreamIds => ($"webhooks/{Id}/out", $"webhooks/{Id}/deliveries");

            public WebHookMemento GetMemento()
            {
                return _memento;
            }
        }
    }
}