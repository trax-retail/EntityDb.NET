﻿using EntityDb.Abstractions.Queries;
using EntityDb.Abstractions.Queries.FilterBuilders;
using EntityDb.Abstractions.Queries.Filters;
using EntityDb.Abstractions.Queries.SortBuilders;

namespace EntityDb.Common.Queries.Filtered
{
    internal sealed record FilteredLeaseQuery
        (ILeaseQuery LeaseQuery, ILeaseFilter LeaseFilter) : FilteredQueryBase(LeaseQuery), ILeaseQuery
    {
        public TFilter GetFilter<TFilter>(ILeaseFilterBuilder<TFilter> builder)
        {
            return builder.And
            (
                LeaseQuery.GetFilter(builder),
                LeaseFilter.GetFilter(builder)
            );
        }

        public TSort? GetSort<TSort>(ILeaseSortBuilder<TSort> builder)
        {
            return LeaseQuery.GetSort(builder);
        }
    }
}
