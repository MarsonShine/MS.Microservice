using MongoDB.Driver;
using MS.Microservice.Domain;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace MS.MicroService.MongoDb
{
    public class MongoDbContextModelSource : IMongoDbContextModelSource
    {
        protected readonly ConcurrentDictionary<Type, MongoDbContextModel> ModelCache = new ConcurrentDictionary<Type, MongoDbContextModel>();

        public MongoDbContextModel GetModel(MongoDbContext dbContext)
        {
            return ModelCache.GetOrAdd(
                dbContext.GetType(),
                _ => CreateModel(dbContext)
            );
        }

        protected virtual MongoDbContextModel CreateModel(MongoDbContext dbContext)
        {
            var entityModels = BuildMongoEntityModelCollection(dbContext.GetType());
            return new MongoDbContextModel(entityModels);
        }

        private IReadOnlyDictionary<Type, IMongoEntityModel> BuildMongoEntityModelCollection(Type dbContextType)
        {
            var collectionProperties =
                from property in dbContextType.GetTypeInfo().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                where IsAssignableToGenericType(property.PropertyType, typeof(IMongoCollection<>)) &&
                 typeof(BaseEntity).IsAssignableFrom(property.PropertyType.GenericTypeArguments[0])
                select property;

            var models = new Dictionary<Type, IMongoEntityModel>();

            foreach (var collectionProperty in collectionProperties)
            {
                var entityType = collectionProperty.PropertyType.GenericTypeArguments[0];
                var collectionAttribute = collectionProperty.GetCustomAttributes().OfType<MongoCollectionAttribute>().FirstOrDefault();

                models.Add(entityType, new MongoEntityModel(entityType, collectionAttribute?.CollectionName));
            }

            return new ReadOnlyDictionary<Type, IMongoEntityModel>(models);
        }

        // 待重构到一个单独的类中
        public bool IsAssignableToGenericType(Type givenType, Type genericType)
        {
            var givenTypeInfo = givenType.GetTypeInfo();

            if (givenTypeInfo.IsGenericType && givenType.GetGenericTypeDefinition() == genericType)
            {
                return true;
            }

            foreach (var interfaceType in givenTypeInfo.GetInterfaces())
            {
                if (interfaceType.GetTypeInfo().IsGenericType && interfaceType.GetGenericTypeDefinition() == genericType)
                {
                    return true;
                }
            }

            if (givenTypeInfo.BaseType == null)
            {
                return false;
            }

            return IsAssignableToGenericType(givenTypeInfo.BaseType, genericType);
        }
    }
}