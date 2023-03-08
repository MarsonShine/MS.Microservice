using MS.Microservice.Domain.Aggregates.IdentityModel.Repository;

namespace MS.Microservice.Domain.Managers
{
    public class UserManager
    {
        private readonly IUserRepository _userRepository;

        public UserManager(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }
    }
}
