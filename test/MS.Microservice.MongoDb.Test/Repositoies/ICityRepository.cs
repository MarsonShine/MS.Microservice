using MS.Microservice.MongoDb.Test.Entity;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.MongoDb.Test.Repositoies
{
    public interface ICityRepository
    {
        Task<City> CreateAsync(City city);
        Task<City> FindByNameAsync(string name);
        Task<City> UpdateAsync(City city);
        Task<bool> DeleteAsync(City city);
        Task<List<City>> GetAllAsync();
    }
}
