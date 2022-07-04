﻿using EntityDb.Abstractions.ValueObjects;
using System.Collections.Generic;

namespace EntityDb.Common.Transactions.Subscribers.Processors;

internal class SnapshotTransactionStepProcessorCache<TSnapshot>
{
    private readonly Dictionary<Pointer, TSnapshot> _cache = new();

    public void PutSnapshot(Pointer snapshotPointer, TSnapshot snapshot)
    {
        _cache[snapshotPointer] = snapshot;
    }

    public TSnapshot? GetSnapshotOrDefault(Pointer snapshotPointer)
    {
        return _cache.GetValueOrDefault(snapshotPointer);
    }
}
