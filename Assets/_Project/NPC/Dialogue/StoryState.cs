using System;
using System.Collections.Generic;

namespace DialogueSystem
{
    public abstract class StoryKey
    {
        public string Id { get; }
        protected StoryKey(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A fact needs an ID.", nameof(id));
            Id = id;
        }
    }

    public sealed class StoryKey<T> : StoryKey
    {
        public T InitialValue { get; }
        public StoryKey(string id, T initialValue) : base(id) => InitialValue = initialValue;
    }

    public sealed class StoryState
    {
        private Dictionary<string, StoryKey> keys = new Dictionary<string, StoryKey>();
        private Dictionary<string, object> values = new Dictionary<string, object>();

        public void Register(StoryKey key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (keys.TryGetValue(key.Id, out StoryKey existing) && !ReferenceEquals(existing, key))
                throw new InvalidOperationException($"Duplicate story fact '{key.Id}'. Reuse its key instance.");
            keys[key.Id] = key;
        }

        public T Get<T>(StoryKey<T> key)
        {
            Register(key);
            return values.TryGetValue(key.Id, out object value) ? (T)value : key.InitialValue;
        }

        public void Set<T>(StoryKey<T> key, T value)
        {
            Register(key);
            values[key.Id] = value;
        }

        internal void ApplyReceipt(FactChange[] changes)
        {
            if (changes.Length == 0) return;
            var staged = new StoryState
            {
                keys = new Dictionary<string, StoryKey>(keys),
                values = new Dictionary<string, object>(values)
            };
            foreach (FactChange change in changes) change.Apply(staged);
            keys = staged.keys;
            values = staged.values;
        }
    }
}
