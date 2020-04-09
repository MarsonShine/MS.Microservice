namespace MS.MicroService.MongoDb
{
    public interface IMongoDbContextModelSource
    {
        MongoDbContextModel GetModel(MongoDbContext dbContext);
    }
}
