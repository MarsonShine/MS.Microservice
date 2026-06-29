using MS.Microservice.Core.Ceching;
using MS.Microservice.Core.Domain.Entity;
using MS.Microservice.Core.Domain.Extension;
using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Specification;
using System.Linq.Expressions;
using Xunit;

namespace MS.Microservice.Core.Tests.Coverage
{
    /// <summary>
    /// Tests for DTOs, specifications, and other 0%-coverage classes.
    /// </summary>
    public class DtoAndSpecificationTests
    {
        // ===== ResultDto =====
        [Fact] public void ResultDto_Default() { var d = new ResultDto("msg", 400); Assert.False(d.Success); Assert.Equal("msg", d.Message); Assert.Equal(400, d.Code); }
        [Fact] public void ResultDto_Full() { var d = new ResultDto(true, "ok", 200); Assert.True(d.Success); Assert.Equal("ok", d.Message); Assert.Equal(200, d.Code); }
        [Fact] public void ResultDto_Properties() { var d = new ResultDto(false, "err", 500) { Success = true, Message = "fixed", Code = 200 }; Assert.True(d.Success); }

        // ===== ResultDto<T> =====
        [Fact] public void ResultDtoOfT_DataOnly() { var d = new ResultDto<int>(42); Assert.Equal(42, d.Data); Assert.True(d.Success); Assert.Equal(200, d.Code); }
        [Fact] public void ResultDtoOfT_Full() { var d = new ResultDto<string>("val", false, "bad", 400); Assert.Equal("val", d.Data); Assert.False(d.Success); Assert.Equal("bad", d.Message); Assert.Equal(400, d.Code); }
        [Fact] public void ResultDtoOfT_Inherits() { var d = new ResultDto<int>(1, false, "msg", 404); var rd = (ResultDto)d; Assert.Equal("msg", rd.Message); }

        // ===== PagedRequestDto =====
        [Fact] public void PagedRequestDto_Default() { var d = new PagedRequestDto(); Assert.Equal(1, d.PageIndex); Assert.Equal(10, d.PageSize); }
        [Fact] public void PagedRequestDto_Custom() { var d = new PagedRequestDto(2, 20) { PageIndex = 3, PageSize = 50 }; Assert.Equal(3, d.PageIndex); Assert.Equal(50, d.PageSize); }

        // ===== PagedAndSortedRequestDto =====
        [Fact] public void PagedAndSortedRequestDto_Default() { var d = new PagedAndSortedRequestDto(); Assert.Equal(1, d.PageIndex); Assert.Null(d.Sorting); }
        [Fact] public void PagedAndSortedRequestDto_Sorted() { var d = new PagedAndSortedRequestDto() { Sorting = "Name" }; Assert.Equal("Name", d.Sorting); }

        // ===== PagedResultDto<T> =====
        [Fact] public void PagedResultDto_Default() { var d = new PagedResultDto<int>(); Assert.Equal(0, d.TotalCount); Assert.Empty(d.Items); }
        [Fact] public void PagedResultDto_WithData() { var d = new PagedResultDto<string>(5, new System.Collections.Generic.List<string> { "a", "b" }); Assert.Equal(5, d.TotalCount); Assert.Equal(2, d.Items.Count); }

        // ===== CacheOptions =====
        [Fact] public void CacheOptions_Default() { var o = new CacheOptions(); Assert.Equal("Fz.Activation.", o.KeyPrefix); Assert.Equal(7200, o.SlidingExpirationSecond); Assert.Null(o.AbsoluteExpirationSecond); }
        [Fact] public void CacheOptions_Custom() { var o = new CacheOptions { KeyPrefix = "test.", AbsoluteExpirationSecond = 300, SlidingExpirationSecond = 600 }; Assert.Equal("test.", o.KeyPrefix); Assert.Equal(300, o.AbsoluteExpirationSecond); }

        // ===== Specification<T> =====
        [Fact] public void Specification_Where() { var s = new TestSpec(); s.TestWhere(x => x.Name == "a"); Assert.NotNull(s.Criteria); }
        [Fact] public void Specification_Where_Twice_Ands() { var s = new TestSpec(); s.TestWhere(x => x.Name == "a"); s.TestWhere(x => x.Age > 10); Assert.NotNull(s.Criteria); }
        [Fact] public void Specification_OrWhere() { var s = new TestSpec(); s.TestOrWhere(x => x.Name == "a"); s.TestOrWhere(x => x.Age > 10); Assert.NotNull(s.Criteria); }
        [Fact] public void Specification_Where_Then_OrWhere() { var s = new TestSpec(); s.TestWhere(x => x.Name == "a"); s.TestOrWhere(x => x.Age > 10); Assert.NotNull(s.Criteria); }
        [Fact] public void Specification_Includes() { var s = new TestSpec(); s.TestInclude(x => x.Name); Assert.Single(s.Includes); }
        [Fact] public void Specification_IncludeList() { var s = new TestSpec(); s.TestIncludeList(x => x.Tags); Assert.Single(s.Includes); }
        [Fact] public void Specification_IncludeCollection() { var s = new TestSpec(); s.TestIncludeCollection(x => x.TagsAsCollection); Assert.Single(s.Includes); }
        [Fact] public void Specification_OrderBy() { var s = new TestSpec(); s.TestOrderBy(x => x.Name); Assert.Single(s.OrderExpressions); }
        [Fact] public void Specification_OrderByDescending() { var s = new TestSpec(); s.TestOrderByDescending(x => x.Name); Assert.Single(s.OrderExpressions); }
        [Fact] public void Specification_ThenBy() { var s = new TestSpec(); s.TestThenBy(x => x.Name); Assert.Single(s.OrderExpressions); }
        [Fact] public void Specification_ThenByDescending() { var s = new TestSpec(); s.TestThenByDescending(x => x.Name); Assert.Single(s.OrderExpressions); }
        [Fact] public void Specification_Paging() { var s = new TestSpec(); s.TestPaging(10, 20); Assert.Equal(10, s.Skip); Assert.Equal(20, s.Take); Assert.True(s.IsPagingEnabled); }
        [Fact] public void Specification_IgnoreQueryFilters() { var s = new TestSpec(); s.TestIgnoreFilters(); Assert.True(s.IgnoreQueryFilters); }

        // ===== Specification<T,TResult> =====
        [Fact] public void SpecificationWithResult_Select() { var s = new TestResultSpec(); s.TestSelect(x => x.Name); Assert.NotNull(s.Selector); }

        // ===== EntityExtensions =====
        [Fact] public void EntityExtensions_IsNull_Null() { IEntity? e = null; Assert.True(e!.IsNull()); }
        [Fact] public void EntityExtensions_IsNull_NotNull() { var e = new TestEntity(); Assert.False(e.IsNull()); }

        private class TestEntity : IEntity
        {
            public string Name { get; set; } = "";
            public int Age { get; set; }
            public System.Collections.Generic.List<string> Tags { get; set; } = new();
            public System.Collections.Generic.ICollection<string> TagsAsCollection { get; set; } = new System.Collections.Generic.List<string>();
            public object[] GetKeys() => new object[] { Name };
        }
    }

    public class TestSpec : Specification<TestEntity>
    {
        public void TestWhere(Expression<Func<TestEntity, bool>> expr) => Where(expr);
        public void TestOrWhere(Expression<Func<TestEntity, bool>> expr) => OrWhere(expr);
        public void TestInclude<TProperty>(Expression<Func<TestEntity, TProperty>> expr) => Include(expr);
        public void TestIncludeList<TProperty>(Expression<Func<TestEntity, System.Collections.Generic.List<TProperty>>> expr) => IncludeList(expr);
        public void TestIncludeCollection<TProperty>(Expression<Func<TestEntity, System.Collections.Generic.ICollection<TProperty>>> expr) => IncludeCollection(expr);
        public void TestOrderBy(Expression<Func<TestEntity, object>> expr) => OrderBy(expr);
        public void TestOrderByDescending(Expression<Func<TestEntity, object>> expr) => OrderByDescending(expr);
        public void TestThenBy(Expression<Func<TestEntity, object>> expr) => ThenBy(expr);
        public void TestThenByDescending(Expression<Func<TestEntity, object>> expr) => ThenByDescending(expr);
        public void TestPaging(int skip, int take) => ApplyPaging(skip, take);
        public void TestIgnoreFilters(bool ignore = true) => IgnoreGlobalQueryFilters(ignore);
    }

    public class TestEntity
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public System.Collections.Generic.List<string> Tags { get; set; } = new();
        public System.Collections.Generic.ICollection<string> TagsAsCollection { get; set; } = new System.Collections.Generic.List<string>();
    }

    public class TestResultSpec : Specification<TestEntity, string>
    {
        public void TestSelect(Expression<Func<TestEntity, string>> expr) => Select(expr);
    }
}
