﻿using EntityDb.Abstractions.Tags;
using EntityDb.Abstractions.ValueObjects;
using EntityDb.Common.Transactions.Builders;
using System.Collections.Generic;

namespace EntityDb.Common.Commands;

/// <summary>
///     If a transaction needs to add any instances of <see cref="ITag" />, and the properties of the tags
///     are contained in the command and/or entity, a direct call to
///     <see cref="TransactionBuilder{TEntity}.Add(Id, ITag[])" />
///     can be avoided by implementing this interface!
/// </summary>
/// <typeparam name="TEntity">The type of the entity</typeparam>
internal interface IAddTagsCommand<in TEntity>
{
    /// <summary>
    ///     Returns the tags that need to be added.
    /// </summary>
    /// <param name="previousEntity">The entity before this command was applied.</param>
    /// <param name="nextEntity">The entity after this command was applied.</param>
    /// <returns>The tags that need to be added.</returns>
    IEnumerable<ITag> GetTags(TEntity previousEntity, TEntity nextEntity);
}
