using System;
using System.Collections.Generic;

namespace MS.Microservice.Web.Application.Models.Caching
{
    [Serializable]
    public class UserCacheItem
    {
        public UserCacheItem()
        {
        }

        public int Id { get; set; }
        public string Account { get; set; }
        public string Name { get; set; }
        public string Password { get; set; }
        public string Salt { get; set; }
        public string Telephone { get; set; }
        public string Email { get; set; }
        public string FzAccount { get; set; }
        public string FzId { get; set; }
        public ICollection<RoleCacheItem> Roles { get; set; }
    }
    [Serializable]
    public class RoleCacheItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public ICollection<ActionCacheItem> Actions { get; set; }
    }
    [Serializable]
    public class ActionCacheItem
    {
        public string Path { get; set; }
    }
}
