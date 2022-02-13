using EntityDb.Abstractions.Commands;
using EntityDb.Abstractions.Tags;
using EntityDb.Common.Tags;
using EntityDb.Common.Tests.Implementations.Entities;
using System.Collections.Generic;

namespace EntityDb.Common.Tests.Implementations.Commands
{
    public record AddTag(string TagLabel, string TagValue) : ICommand<TransactionEntity>
    {
        public TransactionEntity Reduce(TransactionEntity entity)
        {
            var tags = new List<ITag>();

            if (entity.Tags != null)
            {
                tags.AddRange(entity.Tags);
            }

            tags.Add(new Tag(TagLabel, TagValue));

            return entity with { VersionNumber = entity.VersionNumber + 1, Tags = tags.ToArray() };
        }
    }
}