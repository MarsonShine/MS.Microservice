using System.Collections.Generic;
using System.Linq;
using MS.Microservice.Core.Extension;

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

			// 使用 IntersectBy 查找名称相同的人和员工
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
