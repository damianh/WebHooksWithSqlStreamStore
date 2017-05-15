namespace WebHooks.Subscriber.Domain
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    internal class Subscriptions
    {
        private readonly int _maxSubscriptionCount;
        private readonly GetUtcNow _getUtcNow;
        private readonly Dictionary<Guid, Subscription> _items = new Dictionary<Guid, Subscription>();

        public Subscriptions(GetUtcNow getUtcNow, int maxSubscriptionCount)
        {
            _getUtcNow = getUtcNow;
            _maxSubscriptionCount = maxSubscriptionCount;
        }

        public Subscriptions(SubscriptionsMemento memento, GetUtcNow getUtcNow, int maxSubscriptionCount)
            : this(getUtcNow, maxSubscriptionCount)
        {
            _items = memento.Items.ToDictionary(i => i.Id, i => new Subscription(i));
        }

        public IReadOnlyCollection<Subscription> Items => _items.Values;

        public SubscriptionsMemento ToMemento()
        {
            return new SubscriptionsMemento
            {
                Items = _items.Values.Select(i => i.GetMemento()).ToArray()
            };
        }

        public Subscription Add(string name)
        {
            var memento = new SubscriptionMemento
            {
                Id = Guid.NewGuid(),
                Name = name,
                CreatedUtc = _getUtcNow(),
                Secret = Guid.NewGuid().ToString("d")
            };
            var subscription = new Subscription(memento);
            _items.Add(subscription.Id, subscription);
            return subscription;
        }

        public Subscription Get(Guid id) => !_items.ContainsKey(id) ? null : _items[id];

        public bool Delete(Guid id)
        {
            if (!_items.ContainsKey(id))
            {
                return false;
            }
            _items.Remove(id);
            return true;
        }

        internal class Subscription
        {
            private readonly SubscriptionMemento _memento;

            internal Subscription(SubscriptionMemento memento)
            {
                _memento = memento;
            }

            internal string Name => _memento.Name;

            internal Guid Id => _memento.Id;

            internal string Secret => _memento.Secret;

            internal DateTime CreatedUtc => _memento.CreatedUtc;

            public SubscriptionMemento GetMemento()
            {
                return _memento;
            }

            internal string GetInboxStreamId()
            {
                return $"webhooks/subscriptions/{Id}/inbox";
            }
        }
    }
}