﻿using EntityDb.Abstractions.Snapshots;
using StackExchange.Redis;

namespace EntityDb.Redis.Sessions;

/// <summary>
///     Configuration options for the Redis implementation of <see cref="ISnapshotRepository{TSnapshot}"/>.
/// </summary>
public class RedisSnapshotSessionOptions<TSnapshot>
{
    /// <summary>
    ///     A connection string that is compatible with <see cref="ConfigurationOptions.Parse(string)"/>
    /// </summary>
    public string ConnectionString { get; set; } = default!;

    /// <summary>
    ///     Choose a key namspace for snapshots. Snapshots are stored with keys in the following format:
    ///     <c>{KeyNamespace}#{SnapshotId}@{SnapshotVersionNumber}</c>
    /// </summary>
    public string KeyNamespace { get; set; } = default!;

    /// <summary>
    ///     If <c>true</c>, indicates the agent only intends to execute queries.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    ///     If <c>true</c>, indicates the agent can tolerate replication lag for queries.
    /// </summary>
    public bool SecondaryPreferred { get; set; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{nameof(RedisSnapshotSessionOptions<TSnapshot>)}<{typeof(TSnapshot).Name}";
    }
}
