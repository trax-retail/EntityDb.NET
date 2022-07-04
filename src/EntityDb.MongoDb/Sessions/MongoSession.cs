﻿using EntityDb.Common.Disposables;
using EntityDb.Common.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace EntityDb.MongoDb.Sessions;

internal record MongoSession
(
    ILogger<MongoSession> Logger,
    IMongoDatabase MongoDatabase,
    IClientSessionHandle ClientSessionHandle,
    MongoDbTransactionSessionOptions Options
) : DisposableResourceBaseRecord, IMongoSession
{
    private static readonly WriteConcern WriteConcern = WriteConcern.WMajority;

    public async Task Insert<TDocument>(string collectionName, TDocument[] bsonDocuments,
        CancellationToken cancellationToken)
    {
        AssertNotReadOnly();

        var serverSessionId = ClientSessionHandle.ServerSession.Id.ToString();

        Logger
            .LogInformation
            (
                "Started Running MongoDb Insert on `{DatabaseNamespace}.{CollectionName}`\n\nServer Session Id: {ServerSessionId}\n\nDocuments Inserted: {DocumentsInserted}",
                MongoDatabase.DatabaseNamespace,
                collectionName,
                serverSessionId,
                bsonDocuments.Length
            );

        await MongoDatabase
            .GetCollection<TDocument>(collectionName)
            .InsertManyAsync
            (
                ClientSessionHandle,
                bsonDocuments,
                cancellationToken: cancellationToken
            );

        Logger
            .LogInformation
            (
                "Finished Running MongoDb Insert on `{DatabaseNamespace}.{CollectionName}`\n\nServer Session Id: {ServerSessionId}\n\nDocuments Inserted: {DocumentsInserted}",
                MongoDatabase.DatabaseNamespace,
                collectionName,
                serverSessionId,
                bsonDocuments.Length
            );
    }

    public async Task<List<TDocument>> Find<TDocument>
    (
        string collectionName,
        FilterDefinition<BsonDocument> filter,
        ProjectionDefinition<BsonDocument, TDocument> projection,
        SortDefinition<BsonDocument>? sort,
        int? skip,
        int? limit,
        CancellationToken cancellationToken
    )
    {
        var find = MongoDatabase
            .GetCollection<BsonDocument>(collectionName)
            .WithReadPreference(GetReadPreference())
            .WithReadConcern(GetReadConcern())
            .Find(ClientSessionHandle, filter, new FindOptions { MaxTime = Options.ReadTimeout })
            .Project(projection);

        if (sort is not null)
        {
            find = find.Sort(sort);
        }

        if (skip is not null)
        {
            find = find.Skip(skip);
        }

        if (limit is not null)
        {
            find = find.Limit(limit);
        }

        var query = find.ToString();
        var serverSessionId = ClientSessionHandle.ServerSession.Id.ToString();

        Logger
            .LogInformation
            (
                "Started Running MongoDb Query on `{DatabaseNamespace}.{CollectionName}`\n\nServer Session Id: {ServerSessionId}\n\nQuery: {Query}",
                MongoDatabase.DatabaseNamespace,
                collectionName,
                serverSessionId,
                query
            );

        var documents = await find.ToListAsync(cancellationToken);

        Logger
            .LogInformation
            (
                "Finished Running MongoDb Query on `{DatabaseNamespace}.{CollectionName}`\n\nServer Session Id: {ServerSessionId}\n\nQuery: {Query}\n\nDocuments Returned: {DocumentsReturned}",
                MongoDatabase.DatabaseNamespace,
                collectionName,
                serverSessionId,
                query,
                documents.Count
            );

        return documents;
    }

    public async Task Delete<TDocument>(string collectionName,
        FilterDefinition<TDocument> filterDefinition, CancellationToken cancellationToken)
    {
        AssertNotReadOnly();

        var serverSessionId = ClientSessionHandle.ServerSession.Id.ToString();
        var command =
            MongoDatabase.GetCollection<TDocument>(collectionName).Find(filterDefinition).ToString()!.Replace("find",
                "deleteMany");

        Logger
            .LogInformation
            (
                "Started Running MongoDb Delete on `{DatabaseNamespace}.{CollectionName}`\n\nServer SessionId: {ServerSessionId}\n\nCommand: {Command}",
                MongoDatabase.DatabaseNamespace,
                collectionName,
                serverSessionId,
                command
            );

        var deleteResult = await MongoDatabase
            .GetCollection<TDocument>(collectionName)
            .DeleteManyAsync
            (
                ClientSessionHandle,
                filterDefinition,
                cancellationToken: cancellationToken
            );

        Logger
            .LogInformation(
                "Finished Running MongoDb Delete on `{DatabaseNamespace}.{CollectionName}`\n\nServer SessionId: {ServerSessionId}\n\nCommand: {Command}\n\nDocuments Deleted: {DocumentsDeleted}",
                MongoDatabase.DatabaseNamespace,
                collectionName,
                serverSessionId,
                command,
                deleteResult.IsAcknowledged ? "(Not Available)" : deleteResult.DeletedCount
            );
    }

    public IMongoSession WithTransactionSessionOptions(MongoDbTransactionSessionOptions options)
    {
        return this with { Options = options };
    }

    public void StartTransaction()
    {
        AssertNotReadOnly();

        ClientSessionHandle.StartTransaction(new TransactionOptions
        (
            writeConcern: WriteConcern,
            maxCommitTime: Options.WriteTimeout
        ));
    }

    [ExcludeFromCodeCoverage(Justification =
        "Tests should run with the Debug configuration, and should not execute this method.")]
    public async Task CommitTransaction(CancellationToken cancellationToken)
    {
        AssertNotReadOnly();

        await ClientSessionHandle.CommitTransactionAsync(cancellationToken);
    }

    public async Task AbortTransaction()
    {
        AssertNotReadOnly();

        await ClientSessionHandle.AbortTransactionAsync();
    }

    public override ValueTask DisposeAsync()
    {
        ClientSessionHandle.Dispose();

        return ValueTask.CompletedTask;
    }

    private ReadPreference GetReadPreference()
    {
        if (!Options.ReadOnly)
        {
            return ReadPreference.Primary;
        }

        return Options.SecondaryPreferred
            ? ReadPreference.SecondaryPreferred
            : ReadPreference.PrimaryPreferred;
    }

    [ExcludeFromCodeCoverage(Justification = "Tests should always run in a transaction.")]
    private ReadConcern GetReadConcern()
    {
        return ClientSessionHandle.IsInTransaction
            ? ReadConcern.Snapshot
            : ReadConcern.Majority;
    }

    private void AssertNotReadOnly()
    {
        if (Options.ReadOnly)
        {
            throw new CannotWriteInReadOnlyModeException();
        }
    }

    public static IMongoSession Create
    (
        IServiceProvider serviceProvider,
        IMongoDatabase mongoDatabase,
        IClientSessionHandle clientSessionHandle,
        MongoDbTransactionSessionOptions options
    )
    {
        return ActivatorUtilities.CreateInstance<MongoSession>(serviceProvider, mongoDatabase, clientSessionHandle,
            options);
    }
}
