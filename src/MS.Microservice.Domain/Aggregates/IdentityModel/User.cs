using MS.Microservice.Core;
using MS.Microservice.Core.Domain;
using MS.Microservice.Core.Domain.Entity;
using MS.Microservice.Core.Extension;
using MS.Microservice.Core.Security.Cryptology;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace MS.Microservice.Domain.Aggregates.IdentityModel
{
    public class User : EntityBase<int>, IFullAuditTracker<int>, IAggregateRoot
    {
        private bool _isDisabled;
        private string _telephone;
        private DateTime? _deletedAt;
        private int _creatorId;
        private int _updatorId;
        private string _email;
        private string _name;
        private string _password;
        private string _account;
        private string _salt;
        private string _fzAccount;
        private string _fzId;

        private User() { 
            Roles = new List<Role>();
        }
        [JsonConstructor]
        public User(string account, string password, string salt, bool isDisabled, string telephone, int creatorId, int updatorId, string email, string name, string fzAccount, string fzId) : this()
        {
            _password = password;
            _salt = salt;
            _isDisabled = isDisabled;
            _telephone = telephone;
            _creatorId = creatorId;
            _updatorId = updatorId;
            _email = email;
            _name = name;
            _account = account;
            _fzAccount = fzAccount;
            _fzId = fzId;
        }

        internal void ChangePassword()
        {
            Password = CryptologyHelper.HmacSha256(_password + _salt);
        }

        public string Account { get => _account; private set => _account = value; }
        public string Name { get => _name; private set => _name = value; }
        public string Password { get => _password; private set => _password = value; }
        public string Salt { get => _salt; private set => _salt = value; }
        public string Telephone { get => _telephone; private set => _telephone = value; }
        public string Email { get => _email; private set => _email = value; }
        public bool IsDisabled { get => _isDisabled; private set => _isDisabled = value; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get => _deletedAt; private set => _deletedAt = value; }

        public int CreatorId { get => _creatorId; private set => _creatorId = value; }

        public int UpdatorId { get => _updatorId; private set => _updatorId = value; }

        public ICollection<Role> Roles { get; private set; }
        public string FzAccount { get => _fzAccount; private set => _fzAccount = value; }
        public string FzId { get => _fzId; private set => _fzId = value; }

        public override bool IsTransient()
        {
            return Id == 0;
        }

        public void AddRole(Role role)
        {
            if (Roles.Any(r => r.Id == role.Id))
            {
                return;
            }
            Roles.Add(role);
        }

        public void AddRoles(ICollection<Role> roles)
        {
            foreach (var role in roles)
            {
                AddRole(role);
            }
        }

        public void Delete() => _deletedAt = DateTime.Now;

        /// <summary>
        /// 修改用户信息
        /// </summary>
        /// <param name="name"> 名称 空 不更新</param>
        /// <param name="telephone">电话 空 不更新</param>
        /// <param name="email">邮箱 空 不更新</param>
        /// <param name="password">密码 空 不更新</param>
        /// <param name="salt">密码盐 空 不更新</param>
        internal void Update(string name, string telephone, string email,string password,string salt)
        {
            if (name.IsNullOrEmpty() == false)
                _name = name;
            if (telephone.IsNullOrEmpty() == false)
                _telephone = telephone;
            if (email.IsNullOrEmpty() == false)
                _email = email;

            if (password.IsNullOrEmpty() == false && salt.IsNullOrEmpty() == false)
            {
                _password = password;
                _salt = salt;
            }

        }
    }
}
