using MS.Microservice.Core;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MS.Microservice.Domain.Aggregates.IdentityModel
{
    public class Role : EntityBase<int>
    {
        protected Role() {
            Users = new List<User>();
            Actions = new List<Action>();
        }
        public Role(string name, string description) : this(0, name, description) { }
        [JsonConstructor]
        public Role(int id, string name, string description, List<Action>? actions = null) : this()
        {
            Id = id;
            Name = name;
            Description = description;
            if (actions != null)
            {
                Actions = new List<Action>();
            }
        }

        public string? Name { get; private set; }
        public string? Description { get; }
        public List<Action> Actions { get; private set; }
        [JsonIgnore]
        public List<User> Users { get; private set; }

        public void AddAction(string name, string path)
        {
            Actions.Add(new Action(name, path));
        }

    }

    public class RoleComparer : IEqualityComparer<Role>
    {
        public bool Equals(Role? x, Role? y)
        {
            if (x is null || y is null)
                return false;
            return x!.Id == y!.Id;
        }

        public int GetHashCode([DisallowNull] Role obj)
        {
            return obj.Name!.GetHashCode();
        }
    }

    public class UserRole
    {
        private UserRole()
        {
        }

        public UserRole(int userId, int roleId)
        {
            UserId = userId;
            RoleId = roleId;
        }

        public int UserId { get; private set; }
        public User? User { get; private set; }
        public int RoleId { get; private set; }
        public Role? Role { get; private set; }
    }

    public class RoleAction
    {
        private RoleAction() { }
        public RoleAction(int roleId, int actionId)
        {
            RoleId = roleId;
            ActionId = actionId;
        }

        public int RoleId { get; private set; }
        public Role? Role { get; private set; }
        public int ActionId { get; private set; }
        public Action? Action { get; private set; }
    }
}
