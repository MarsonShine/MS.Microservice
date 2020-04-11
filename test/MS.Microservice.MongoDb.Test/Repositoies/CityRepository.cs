using MongoDB.Driver;
using MS.Microservice.MongoDb.Test.Entity;
using MS.MicroService.MongoDb;
using MS.MicroService.MongoDb.Repository;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.MongoDb.Test.Repositoies
{
    public class CityRepository : MongoDbRepository<ITestMongoDbContext, City>, ICityRepository
    {
        public CityRepository(IMongoDbContextProvider<ITestMongoDbContext> dbContextProvider) : base(dbContextProvider)
        {

        }

        public async Task<City> CreateAsync(City city)
        {

            var cityInfo = await InsertAsync(city);
            return cityInfo;
        }

        public async Task<City> FindByNameAsync(string name)
        {
            return await FindAsync(p => p.Name == name);
        }

        public async Task<City> UpdateAsync(City city)
        {
            return await UpdateAsync(city, default);
        }

        public async Task<bool> DeleteAsync(City city)
        {
            return await DeleteAsync(city, default);
        }

        public async Task<List<City>> GetAllAsync()
        {
            var list = await GetMongoQueryable().ToListAsync();
            return list;
        }
    }
}
