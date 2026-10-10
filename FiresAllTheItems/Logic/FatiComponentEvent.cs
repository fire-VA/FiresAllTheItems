using System;
using System.Collections.Generic;
using UnityEngine;

namespace FiresAllTheItems.Logic
{
    // Port of VNEI Logic/ComponentEvent.cs — an event whose listeners are keyed by a Unity
    // Component so pooled UI cells can re-subscribe per SetItem and dead components are
    // skipped/pruned automatically (Unity fake-null safe).
    public class FatiComponentEvent
    {
        private readonly Dictionary<Component, Action> _listeners = new Dictionary<Component, Action>();
        private static readonly List<Component> _deadScratch = new List<Component>();

        public void AddListener(Component owner, Action action)
        {
            if (owner == null)
                return;
            _listeners[owner] = action;
        }

        public void RemoveListener(Component owner)
        {
            if (owner is null)
                return;
            _listeners.Remove(owner);
        }

        public void Invoke()
        {
            _deadScratch.Clear();
            foreach (KeyValuePair<Component, Action> pair in _listeners)
            {
                if (pair.Key == null)
                {
                    _deadScratch.Add(pair.Key);
                    continue;
                }
                try { pair.Value?.Invoke(); }
                catch (Exception ex) { Ascend.Log?.LogWarning($"FatiComponentEvent listener threw: {ex.Message}"); }
            }
            foreach (Component dead in _deadScratch)
                _listeners.Remove(dead);
        }
    }
}
