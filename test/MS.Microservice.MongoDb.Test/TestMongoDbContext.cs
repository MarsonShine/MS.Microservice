using MongoDB.Driver;
using MS.Microservice.Core.Data;
using MS.Microservice.MongoDb.Test.Entity;
using MS.MicroService.MongoDb;

namespace MS.Microservice.MongoDb.Test
{
    [DatabaseNameString("TestApp")]
    public class TestMongoDbContext : MongoDbContext,ITestMongoDbContext
    {
        [MongoCollection("Persons")]
        public IMongoCollection<Person> People => Collection<Person>();
        [MongoCollection("Cities")]
        public IMongoCollection<City> Cities => Collection<City>();
    }
}