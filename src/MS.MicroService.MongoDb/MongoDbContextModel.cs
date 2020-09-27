using System;
using System.Collections.Generic;

namespace MS.MicroService.MongoDb
{
    public class MongoDbContextModel
    {
        public IReadOnlyDictionary<Type, IMongoEntityModel> Entities { get; }

        public MongoDbContextModel(IReadOnlyDictionary<Type, IMongoEntityModel> entities)
        {
            Entities = entities;
        }
    }

    public interface IMongoEntityModel
    {
        Type EntityType { get; }
        string? CollectionName { get; set; }
    }

    public class MongoEntityModel<TEntity> : IMongoEntityModel
    {
        public Type EntityType { get; }

        public string? CollectionName { get; set; }

        public MongoEntityModel()
        {
            EntityType = typeof(TEntity);
        }
    }

    public class MongoEntityModel : IMongoEntityModel
    {
        public Type EntityType { get; }

        public string? CollectionName { get; set; }

        public MongoEntityModel(Type entityType, string? collectionName)
        {
            EntityType = entityType;
            CollectionName = collectionName;
        }
    }
}