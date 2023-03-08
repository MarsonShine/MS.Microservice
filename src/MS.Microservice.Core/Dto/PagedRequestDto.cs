namespace MS.Microservice.Core.Dto
{
    public class PagedRequestDto
    {
        public PagedRequestDto() : this(1, 10)
        {

        }

        public PagedRequestDto(int pageIndex, int pageSize)
        {
            PageIndex = pageIndex;
            PageSize = pageSize;
        }

        /// <summary>
        /// 当前页
        /// </summary>
        public int PageIndex { get; set; }

        /// <summary>
        /// 每页总条数
        /// </summary>
        public int PageSize { get; set; }
    }
}