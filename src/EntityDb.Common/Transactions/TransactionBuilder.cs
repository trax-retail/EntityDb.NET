﻿using EntityDb.Abstractions.Agents;
using EntityDb.Abstractions.Commands;
using EntityDb.Abstractions.Entities;
using EntityDb.Abstractions.Leases;
using EntityDb.Abstractions.Strategies;
using EntityDb.Abstractions.Tags;
using EntityDb.Abstractions.Transactions;
using EntityDb.Common.Exceptions;
using EntityDb.Common.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace EntityDb.Common.Transactions
{
    /// <summary>
    ///     Provides a way to construct an <see cref="ITransaction{TEntity}" />. Note that no operations are permanent until
    ///     you call <see cref="Build(Guid, DateTime?, object?)" /> and pass the result to a transaction repository.
    /// </summary>
    /// <typeparam name="TEntity">The type of the entity in the transaction.</typeparam>
    public sealed class TransactionBuilder<TEntity>
    {
        private readonly Dictionary<Guid, TEntity> _knownEntities = new();
        private readonly List<TransactionStep<TEntity>> _transactionSteps = new();

        private readonly IAgentAccessor _agentAccessor;
        private readonly IConstructingStrategy<TEntity> _constructingStrategy;
        private readonly IVersioningStrategy<TEntity> _versioningStrategy;
        private readonly IAuthorizingStrategy<TEntity>? _authorizingStrategy;
        private readonly ILeasingStrategy<TEntity>? _leasingStrategy;
        private readonly ITaggingStrategy<TEntity>? _taggingStrategy;

        /// <summary>
        ///     Initializes a new instance of <see cref="TransactionBuilder{TEntity}" />.
        /// </summary>
        public TransactionBuilder
        (
            IAgentAccessor agentAccessor,
            IConstructingStrategy<TEntity> constructingStrategy,
            IVersioningStrategy<TEntity> versioningStrategy,
            IAuthorizingStrategy<TEntity>? authorizingStrategy = null,
            ILeasingStrategy<TEntity>? leasingStrategy = null,
            ITaggingStrategy<TEntity>? taggingStrategy = null
        )
        {
            _agentAccessor = agentAccessor;
            _constructingStrategy = constructingStrategy;
            _versioningStrategy = versioningStrategy;
            _authorizingStrategy = authorizingStrategy;
            _leasingStrategy = leasingStrategy;
            _taggingStrategy = taggingStrategy;
        }

        private static ITransactionMetaData<TMetaData> GetTransactionMetaData<TMetaData>(TEntity previousEntity,
            TEntity nextEntity, Func<TEntity, TMetaData[]> metaDataMapper)
        {
            var previousMetaData = metaDataMapper.Invoke(previousEntity);
            var nextMetaData = metaDataMapper.Invoke(nextEntity);

            return new TransactionMetaData<TMetaData>
            {
                Delete = previousMetaData.Except(nextMetaData).ToImmutableArray(),
                Insert = nextMetaData.Except(previousMetaData).ToImmutableArray()
            };
        }

        private bool IsAuthorized(TEntity entity, ICommand<TEntity> command)
        {
            return _authorizingStrategy?.IsAuthorized(entity, command) ?? true;
        }

        private ILease[] GetLeases(TEntity entity)
        {
            return _leasingStrategy?.GetLeases(entity) ?? Array.Empty<ILease>();
        }

        private ITag[] GetTags(TEntity entity)
        {
            return _taggingStrategy?.GetTags(entity) ?? Array.Empty<ITag>();
        }

        private void AddTransactionStep(Guid entityId, ICommand<TEntity> command)
        {
            var previousEntity = _knownEntities[entityId];
            var previousEntityVersionNumber = _versioningStrategy.GetVersionNumber(previousEntity);

            CommandNotAuthorizedException.ThrowIfFalse(IsAuthorized(previousEntity, command));

            var nextEntity = previousEntity.Reduce(command);
            var nextEntityVersionNumber = _versioningStrategy.GetVersionNumber(nextEntity);

            _transactionSteps.Add(new TransactionStep<TEntity>
            {
                PreviousEntitySnapshot = previousEntity,
                PreviousEntityVersionNumber = previousEntityVersionNumber,
                NextEntitySnapshot = nextEntity,
                NextEntityVersionNumber = nextEntityVersionNumber,
                EntityId = entityId,
                Command = command,
                Leases = GetTransactionMetaData(previousEntity, nextEntity, GetLeases),
                Tags = GetTransactionMetaData(previousEntity, nextEntity, GetTags)
            });

            _knownEntities[entityId] = nextEntity;
        }

        /// <summary>
        ///     Loads an already-existing entity into the builder.
        /// </summary>
        /// <param name="entityId">The id of the entity to load.</param>
        /// <param name="entityRepository">The repository which encapsulates transactions and snapshots.</param>
        /// <returns>A new task that loads an already-existing entity into the builder.</returns>
        /// <remarks>
        ///     Call this method to load an entity that already exists before calling
        ///     <see cref="Append(Guid, ICommand{TEntity})" />.
        /// </remarks>
        public async Task Load(Guid entityId, IEntityRepository<TEntity> entityRepository)
        {
            if (_knownEntities.ContainsKey(entityId))
            {
                throw new EntityAlreadyLoadedException();
            }

            var entity = await entityRepository.GetCurrent(entityId);

            _knownEntities.Add(entityId, entity);
        }

        /// <summary>
        ///     Adds a transaction step that creates a new entity.
        /// </summary>
        /// <param name="entityId">A new id for the new entity.</param>
        /// <param name="command">The very first command for the new entity.</param>
        /// <returns>The transaction builder.</returns>
        /// <remarks>
        ///     Do not call this method for an entity that already exists.
        /// </remarks>
        public TransactionBuilder<TEntity> Create(Guid entityId, ICommand<TEntity> command)
        {
            if (_knownEntities.ContainsKey(entityId))
            {
                throw new EntityAlreadyCreatedException();
            }

            var entity = _constructingStrategy.Construct(entityId);

            _knownEntities.Add(entityId, entity);

            AddTransactionStep(entityId, command);

            return this;
        }

        /// <summary>
        ///     Adds a transaction step that appends to an that has already been created.
        /// </summary>
        /// <param name="entityId">The id for the existing entity.</param>
        /// <param name="command">A new command for the existing entity.</param>
        /// <returns>The transaction builder.</returns>
        public TransactionBuilder<TEntity> Append(Guid entityId, ICommand<TEntity> command)
        {
            if (!_knownEntities.ContainsKey(entityId))
            {
                throw new EntityNotLoadedException();
            }

            AddTransactionStep(entityId, command);

            return this;
        }

        /// <summary>
        ///     Returns a new instance of <see cref="ITransaction{TEntity}" />.
        /// </summary>
        /// <param name="transactionId">A new id for the new transaction.</param>
        /// <param name="timeStampOverride">
        ///     An optional override for the transaction timestamp. The default is
        ///     <see cref="DateTime.UtcNow" />.
        /// </param>
        /// <param name="agentSignatureOverride">
        ///     An optional override for the agent signature. The default is
        ///     from <see cref="IAgent.GetSignature()"/>
        /// </param>
        /// <returns>A new instance of <see cref="ITransaction{TEntity}" />.</returns>
        public ITransaction<TEntity> Build(Guid transactionId, DateTime? timeStampOverride = null, object? agentSignatureOverride = null)
        {
            var timeStamp = DateTime.UtcNow;

            if (timeStampOverride.HasValue)
            {
                timeStamp = timeStampOverride.Value.ToUniversalTime();
            }

            var agentSignature = _agentAccessor.GetAgent().GetSignature();

            if (agentSignatureOverride != null)
            {
                agentSignature = agentSignatureOverride;
            }

            var transaction = new Transaction<TEntity>
            {
                Id = transactionId,
                TimeStamp = timeStamp,
                AgentSignature = agentSignature,
                Steps = _transactionSteps.ToImmutableArray<ITransactionStep<TEntity>>()
            };

            _transactionSteps.Clear();

            return transaction;
        }
    }
}
