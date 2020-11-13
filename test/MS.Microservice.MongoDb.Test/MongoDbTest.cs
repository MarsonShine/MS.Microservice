using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MS.Microservice.MongoDb.Test.Entity;
using MS.Microservice.MongoDb.Test.Repositoies;
using MS.MicroService.MongoDb.Log;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace MS.Microservice.MongoDb.Test
{
    public class MongoDbTest : MongoDbTestBase
    {
        private readonly ICityRepository _cityRepository;
        protected virtual ILogger<MongoDbTest> Logger { get; set; }
        public MongoDbTest() : base()
        {
            _cityRepository = RootServiceProvider.GetService<ICityRepository>()!;
            Logger = NullLogger<MongoDbTest>.Instance;
        }

        [Fact]
        public void Constructor_Successfully()
        {
            var b = true;
            Assert.True(b);
        }

        [Fact]
        public async Task Person_Add_Successfully()
        {
            var city = new City
            {
                Name = "深圳市"
            };
            var cityInfo = await _cityRepository.CreateAsync(city);

            Assert.NotNull(cityInfo);
            Assert.True(cityInfo.Name == "深圳市");
        }
        [Theory]
        [InlineData("深圳市")]
        [InlineData("岳阳市")]
        public async Task Person_Find_Successfully(string cityName)
        {
            var city = await _cityRepository.FindByNameAsync(cityName);
            Assert.NotNull(city);
            Assert.Equal(cityName, city.Name);
        }
        [Theory]
        [InlineData("深圳市")]
        public async Task Person_Update_Successfully(string cityName)
        {
            var city = await _cityRepository.FindByNameAsync(cityName);
            Assert.NotNull(city);
            Assert.Equal(cityName, city.Name);

            // update
            city.SetId(100000);
            city.Name = "岳阳市";
            var cityUpdated = await _cityRepository.UpdateAsync(city);
            Assert.NotNull(cityUpdated);
            Assert.Equal(city.Name, cityUpdated.Name);
            Assert.Equal(city.Id, cityUpdated.Id);
        }
        [Theory]
        [InlineData("深圳市")]
        public async Task Person_Delete_Successfully(string cityName)
        {
            var city = await _cityRepository.FindByNameAsync(cityName);
            if (city != null)
            {
                var idDeleting = city.Id;
                await _cityRepository.DeleteAsync(city);
                var cityDeleted = await _cityRepository.FindByNameAsync(city.Name);

                if (cityDeleted != null)
                {
                    Assert.NotEqual(idDeleting, cityDeleted.Id);
                }
            }
            Assert.True(true);
        }
        [Fact]
        public async Task Person_Search_Successfully()
        {
            var cities = await _cityRepository.GetAllAsync();
            Assert.NotNull(cities);
        }
        [Fact]
        public async Task Person_Clear_Successsfully()
        {
            var cities = await _cityRepository.GetAllAsync();
            foreach (var city in cities)
            {
                await _cityRepository.DeleteAsync(city);
            }
            var cs = await _cityRepository.GetAllAsync();
            Assert.True(cs == null || cs.Count == 0);
        }
        [Fact]
        public void MongoDb_Log_Init_Successfully()
        {
            var logEntity = new MongoDbLogEntity
            {
                Content = "测试添加日志内容",
                IP = "localhost",
                SourceFrom = "api地址",
                UserId = 1,
                UserName = "marsonshine",
                LogDateTime = DateTime.Now
            };

            Logger.Log(LogLevel.Information, new EventId(), logEntity, new Exception(), _FormatMongoDbLogException);

            static string _FormatMongoDbLogException(MongoDbLogEntity logEntity, Exception exception)
            {
                return $"添加日志发生错误, 日志源：{System.Text.Json.JsonSerializer.Serialize(logEntity)} 错误信息：{exception.Message + Environment.NewLine + exception.StackTrace}";
            }
        }
    }
}
