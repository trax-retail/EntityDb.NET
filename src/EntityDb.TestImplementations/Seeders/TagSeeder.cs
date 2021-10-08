using EntityDb.Abstractions.Tags;
using EntityDb.Common.Tags;
using System.Collections.Immutable;

namespace EntityDb.TestImplementations.Seeders
{
    public static class TagSeeder
    {
        public static ImmutableArray<ITag> Create()
        {
            var tag = new Tag("Foo", "Bar");

            return ImmutableArray.Create<ITag>(tag);
        }
    }
}
