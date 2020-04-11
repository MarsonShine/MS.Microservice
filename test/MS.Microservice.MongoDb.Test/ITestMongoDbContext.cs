using MongoDB.Driver;
using MS.Microservice.Core.Data;
using MS.Microservice.MongoDb.Test.Entity;
using MS.MicroService.MongoDb;

namespace MS.Microservice.MongoDb.Test
{
    [DatabaseNameString("TestApp")]
    public interface ITestMongoDbContext: IMongoDbContext
    {
        IMongoCollection<Person> People { get; }

        IMongoCollection<City> Cities { get; }
    }
}