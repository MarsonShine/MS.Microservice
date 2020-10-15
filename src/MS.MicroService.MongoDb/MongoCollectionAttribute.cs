using System;

namespace MS.MicroService.MongoDb
{
    public class MongoCollectionAttribute : Attribute
    {
        public string? CollectionName { get; set; }

        public MongoCollectionAttribute()
        {

        }

        public MongoCollectionAttribute(string collectionName)
        {
            CollectionName = collectionName;
        }
    }
}
