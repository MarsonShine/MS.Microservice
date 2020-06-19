namespace MS.Microservice.Core.Dtos
{
    using System;
    using System.Collections.Generic;

    [Serializable]
    public class PagedResultDto<TResult>
    {
        public PagedResultDto()
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
