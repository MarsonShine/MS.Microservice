using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using MongoDB.Driver;
using MS.Microservice.Core.Reflection;
using MS.Microservice.Domain;

namespace MS.MicroService.MongoDb {
    public class MongoDbContextModelSource : IMongoDbContextModelSource {
        protected readonly ConcurrentDictionary<Type, MongoDbContextModel> ModelCache = new ConcurrentDictionary<Type, MongoDbContextModel>();

        public MongoDbContextModel GetModel(MongoDbContext dbContext) {
            return ModelCache.GetOrAdd(
                dbContext.GetType(),
                _ => CreateModel(dbContext)
            );
        }

        protected virtual MongoDbContextModel CreateModel(MongoDbContext dbContext) {
            var entityModels = BuildMongoEntityModelCollection(dbContext.GetType());
            return new MongoDbContextModel(entityModels);
        }

        private IReadOnlyDictionary<Type, IMongoEntityModel> BuildMongoEntityModelCollection(Type dbContextType) {
            var collectionProperties =
                from property in dbContextType.GetTypeInfo().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            where ReflectionHelper.IsAssignableToGenericType(property.PropertyType, typeof(IMongoCollection<>)) &&
                typeof(BaseEntity).IsAssignableFrom(property.PropertyType.GenericTypeArguments[0])
            select property;

            var models = new Dictionary<Type, IMongoEntityModel>();

            foreach (var collectionProperty in collectionProperties) {
                var entityType = collectionProperty.PropertyType.GenericTypeArguments[0];
                var collectionAttribute = collectionProperty.GetCustomAttributes().OfType<MongoCollectionAttribute>().FirstOrDefault();

                models.Add(entityType, new MongoEntityModel(entityType, collectionAttribute?.CollectionName));
            }

            return new ReadOnlyDictionary<Type, IMongoEntityModel>(models);
        }
    }
}