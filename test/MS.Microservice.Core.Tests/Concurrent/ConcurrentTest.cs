using MS.Microservice.Core.Concurrent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Tests.Concurrent
{
	public class ConcurrentTest
	{
		[Fact]
		public async Task SingleFlightTest()
		{
			SingleflightManager singleflightManager = new();

			string result = await singleflightManager.ExecuteOnceAsync("one_call", async () =>
			{
				Console.WriteLine("calling...");
				await Task.Delay(1000);
				return "result";
			});

			Assert.Equal("result", result);

			Task[] tasks = new Task[100];
			for (int i = 0; i < 100; i++)
			{
				tasks[i] = singleflightManager.ExecuteOnceAsync("one_call", async () =>
				{
					Console.WriteLine("calling...");
					await Task.Delay(1000);
					return "result";
				});
			}
			await Task.WhenAll(tasks);
		}
	}
}
