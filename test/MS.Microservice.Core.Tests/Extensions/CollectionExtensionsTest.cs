using System.Collections.Generic;
using System.Linq;
using MS.Microservice.Core.Extension;
using Xunit;

namespace MS.Microservice.Core.Tests.Extensions
{
    public class CollectionExtensionsTest
    {
        [Fact]
        public void TestIntersectBy()
        {
            List<Person> people =
            [
                new Person { Name = "Alice", Age = 30 },
                new Person { Name = "Bob", Age = 25 },
                new Person { Name = "Charlie", Age = 35 }
            ];

            List<Employee> employees =
            [
                new Employee { FirstName = "Alice", EmployeeId = 1 },
                new Employee { FirstName = "David", EmployeeId = 2 }
            ];

            var commonNames = people.IntersectBy(employees, person => person.Name, employee => employee.FirstName);
            Assert.NotEmpty(commonNames);
            Assert.Equal("Alice", commonNames.First().Name);
        }

        [Fact]
        public void TestDistinctDictionary()
        {
            List<Person> people =
            [
                new Person { Name = "Alice", Age = 30 },
                new Person { Name = "Bob", Age = 25 },
                new Person { Name = "Alice", Age = 35 }
            ];

            var peopleDictionary = people.ToDistinctDictionary(person => person.Name!, person => person.Age);
            Assert.NotEmpty(peopleDictionary);
            Assert.Equal(2, peopleDictionary.Count);
        }

        [Fact]
        public void TestOrderByReference()
        {
            List<int> collectionA = [1, 2, 3, 4, 5];
            List<int> collectionB = [5, 3, 1];

            var orderedCollectionB = collectionB.OrderByReference(collectionA)
                .ToList();

            Assert.NotEmpty(orderedCollectionB);
            Assert.Equal(1, orderedCollectionB[0]);
            Assert.Equal(3, orderedCollectionB[1]);
            Assert.Equal(5, orderedCollectionB[2]);
        }

        [Fact]
        public void TestOrderByReference2()
        {
            List<Person> people = [
                new Person { Name = "Alice" },
                new Person { Name = "Bob" },
                new Person { Name = "Charlie" }
            ];

            List<Order> orders =
            [
                new Order { CustomerName = "Charlie" },
                new Order { CustomerName = "Alice" }
            ];

            var sortedOrders = orders.OrderByReference(people, person => person.Name!, order => order.CustomerName)
                .ToList();

            Assert.NotEmpty(sortedOrders);
            Assert.Equal(2, sortedOrders.Count);
            Assert.Equal("Alice", sortedOrders[0].CustomerName);
            Assert.Equal("Charlie", sortedOrders[1].CustomerName);
        }

        [Fact]
        public void TestSafeOrderByReference_Basic_Small()
        {
            List<int> reference = [1, 2, 3, 4, 5];
            List<int> target = [5, 3, 1];

            var result = target.SafeOrderByReference(reference);

            Assert.NotEmpty(result);
            Assert.Equal(new List<int> { 1, 3, 5 }, result);
        }

        [Fact]
        public void TestSafeOrderByReference_WithUnmatched_Small()
        {
            List<int> reference = [1, 2, 3, 4, 5];
            List<int> target = [7, 5, 3, 1, 9];

            var result = target.SafeOrderByReference(reference);

            Assert.Equal(new List<int> { 1, 3, 5, 7, 9 }, result);
        }

        [Fact]
        public void TestSafeOrderByReference_WithDuplicates_Small()
        {
            List<int> reference = [3, 1];
            List<int> target = [1, 3, 3, 2, 1, 5];

            var result = target.SafeOrderByReference(reference);

            Assert.Equal(new List<int> { 3, 1, 3, 2, 1, 5 }, result);
        }

        [Fact]
        public void TestSafeOrderByReference_LargeDataset()
        {
            var reference = Enumerable.Range(0, 200).ToList();
            var target = Enumerable.Range(100, 100).Concat(Enumerable.Range(0, 100)).ToList();

            var result = target.SafeOrderByReference(reference);

            Assert.Equal(200, result.Count);
            for (int i = 0; i < 200; i++)
            {
                Assert.Equal(i, result[i]);
            }
        }

        [Fact]
        public void TestSafeOrderByReference2_Basic()
        {
            List<Person> people = [
                new Person { Name = "Alice" },
                new Person { Name = "Bob" },
                new Person { Name = "Charlie" }
            ];

            List<Order> orders =
            [
                new Order { CustomerName = "Charlie" },
                new Order { CustomerName = "Alice" }
            ];

            var sortedOrders = orders
                .SafeOrderByReference(people, p => p.Name!, o => o.CustomerName)
                .ToList();

            Assert.Equal(2, sortedOrders.Count);
            Assert.Equal("Alice", sortedOrders[0].CustomerName);
            Assert.Equal("Charlie", sortedOrders[1].CustomerName);
        }

        [Fact]
        public void TestSafeOrderByReference2_WithDuplicatesAndUnmatched()
        {
            List<Person> people = [
                new Person { Name = "Charlie" },
                new Person { Name = "Alice" }
            ];

            List<Order> orders =
            [
                new Order { CustomerName = "Alice" },
                new Order { CustomerName = "Eve" },
                new Order { CustomerName = "Alice" },
                new Order { CustomerName = "Charlie" }
            ];

            var sortedOrders = orders
                .SafeOrderByReference(people, p => p.Name!, o => o.CustomerName)
                .ToList();

            Assert.Equal(4, sortedOrders.Count);
            Assert.Equal("Charlie", sortedOrders[0].CustomerName);
            Assert.Equal("Alice", sortedOrders[1].CustomerName);
            Assert.Equal("Eve", sortedOrders[2].CustomerName);
            Assert.Equal("Alice", sortedOrders[3].CustomerName);
        }

        [Fact]
        public void TestOrderByReference_WithUnmatched_ItemsStayAtEnd()
        {
            List<int> reference = [1, 3];
            List<int> target = [5, 3, 1, 9];

            var result = target.OrderByReference(reference).ToList();

            Assert.Equal(new List<int> { 1, 3, 5, 9 }, result);
        }

        [Fact]
        public void TestValidatedShuffle_ShouldCreateDerangement_ForUniqueItems()
        {
            List<int> list = [1, 2, 3, 4, 5];
            var original = list.ToList();

            list.ValidatedShuffle();

            Assert.Equal(original.OrderBy(x => x), list.OrderBy(x => x));
            Assert.All(list.Zip(original), pair => Assert.NotEqual(pair.Second, pair.First));
        }

        [Fact]
        public void TestValidatedShuffle_SingleItem_ShouldRemainUnchanged()
        {
            List<int> list = [1];

            list.ValidatedShuffle();

            Assert.Equal(new List<int> { 1 }, list);
        }

        class Person
        {
            public string? Name { get; set; }
            public int Age { get; set; }
        }

        class Employee
        {
            public string? FirstName { get; set; }
            public int EmployeeId { get; set; }
        }

        class Order
        {
            public string CustomerName { get; set; } = "";
        }
    }
}
