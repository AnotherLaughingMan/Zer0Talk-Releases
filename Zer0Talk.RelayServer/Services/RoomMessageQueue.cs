using System;
using System.Collections.Generic;

namespace Zer0Talk.RelayServer.Services;

/// <summary>
/// Thread-safe ring-buffer that retains the most recent N messages for a room.
/// Used to deliver history to late-arriving members when the admin is offline.
/// </summary>
internal sealed class RoomMessageQueue
{
    private readonly int _capacity;
    private readonly Queue<QueuedMessage> _queue = new();
    private readonly object _lock = new();

    public RoomMessageQueue(int capacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    /// <summary>Enqueue an incoming offline message, dropping the oldest if at capacity.</summary>
    public void Enqueue(string senderUid, string ciphertextHex)
    {
        lock (_lock)
        {
            if (_queue.Count >= _capacity)
                _queue.Dequeue();

            _queue.Enqueue(new QueuedMessage(senderUid, ciphertextHex, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>Return a snapshot of all buffered messages, oldest first.</summary>
    public IReadOnlyList<QueuedMessage> GetRecent()
    {
        lock (_lock)
        {
            return [.. _queue];
        }
    }

    /// <summary>Remove all buffered messages (called when admin comes back online).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _queue.Clear();
        }
    }

    public int Count
    {
        get { lock (_lock) { return _queue.Count; } }
    }
}

internal sealed record QueuedMessage(string SenderUid, string CiphertextHex, DateTimeOffset Timestamp);
