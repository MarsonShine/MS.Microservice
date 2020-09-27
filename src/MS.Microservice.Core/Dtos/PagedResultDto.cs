namespace MS.Microservice.Core.Dtos
{
    using System;
    using System.Collections.Generic;

    [Serializable]
    public class PagedResultDto<TResult>
    {
        public PagedResultDto() : this(0, new List<TResult>())
        {

        }

        public PagedResultDto(long totalCount, List<TResult> items)
        {
            TotalCount = totalCount;
            Items = items;
        }

        public long TotalCount { get; set; }
        public List<TResult> Items { get; set; }
    }
}
