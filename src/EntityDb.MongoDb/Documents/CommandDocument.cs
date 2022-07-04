﻿using EntityDb.Abstractions.Queries;
using EntityDb.Abstractions.Transactions;
using EntityDb.Abstractions.Transactions.Steps;
using EntityDb.Abstractions.ValueObjects;
using EntityDb.Common.Envelopes;
using EntityDb.Common.Queries;
using EntityDb.MongoDb.Commands;
using EntityDb.MongoDb.Extensions;
using EntityDb.MongoDb.Queries;
using EntityDb.MongoDb.Queries.FilterBuilders;
using EntityDb.MongoDb.Queries.SortBuilders;
using EntityDb.MongoDb.Sessions;
using MongoDB.Bson;
using System.Threading;
using System.Threading.Tasks;

namespace EntityDb.MongoDb.Documents;

internal sealed record CommandDocument : DocumentBase, IEntityDocument
{
    public const string CollectionName = "Commands";

    private static readonly CommandFilterBuilder FilterBuilder = new();

    private static readonly CommandSortBuilder SortBuilder = new();

    public Id EntityId { get; init; }
    public VersionNumber EntityVersionNumber { get; init; }

    public static InsertDocumentsCommand<CommandDocument> GetInsertCommand
    (
        IEnvelopeService<BsonDocument> envelopeService,
        ITransaction transaction,
        IAppendCommandTransactionStep appendCommandTransactionStep
    )
    {
        var documents = new[]
        {
            new CommandDocument
            {
                TransactionTimeStamp = transaction.TimeStamp,
                TransactionId = transaction.Id,
                EntityId = appendCommandTransactionStep.EntityId,
                EntityVersionNumber = appendCommandTransactionStep.EntityVersionNumber,
                Data = envelopeService.Deconstruct(appendCommandTransactionStep.Command)
            }
        };

        return new InsertDocumentsCommand<CommandDocument>
        (
            CollectionName,
            documents
        );
    }

    public static DocumentQuery<CommandDocument> GetQuery
    (
        ICommandQuery commandQuery
    )
    {
        return new DocumentQuery<CommandDocument>
        (
            CollectionName,
            commandQuery.GetFilter(FilterBuilder),
            commandQuery.GetSort(SortBuilder),
            commandQuery.Skip,
            commandQuery.Take
        );
    }

    public static Task<VersionNumber> GetLastEntityVersionNumber
    (
        IMongoSession mongoSession,
        Id entityId,
        CancellationToken cancellationToken
    )
    {
        var commandQuery = new GetLastEntityCommandQuery(entityId);

        var documentQuery = GetQuery(commandQuery);

        return documentQuery.GetEntityVersionNumber(mongoSession, cancellationToken);
    }
}
