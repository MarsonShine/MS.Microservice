using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MS.Microservice.Domain.Aggregates.IdentityModel
{
    public class Action : EntityBase<int>
    {
        protected Action() { }
        public Action(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public string? Name { get; set; }
        public string? Path { get; set; }
        [JsonIgnore]
        public ICollection<Role>? Roles { get; private set; }
    }
}
