using DDDExample.Domain;
using DDDExample.Domain.Decorator;
using DDDExample.Domain.Repository;
using DDDExample.Infrastructure.DbContext;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DDDExample.Infrastructure.Repository
{
    public class UserRepository : IUserRepository
    {
        private readonly MyDbContext myDbContext;
        public UserRepository(MyDbContext myDbContext)
        {
            this.myDbContext = myDbContext;
        }

        public Buyer AsBuyer(User user)
        {
            return new Buyer(user); // 可以传递需要的领域实体信息
        }

        public Contact AsContact(User user)
        {
            return new Contact(user);
        }

        public Reader AsReader(User user)
        {
            return new Reader(user);
        }

        public User FindById(long id)
        {
            return myDbContext.Find<User>(id);
        }
    }
}
