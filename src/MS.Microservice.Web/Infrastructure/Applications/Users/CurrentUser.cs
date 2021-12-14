namespace MS.Microservice.Web.Infrastructure.Applications.Users
{
    public class CurrentUser
    {
        public CurrentUser(int id, string userName, string email, string phone, int[] roleIds)
        {
            Id = id;
            UserName = userName;
            Email = email;
            Phone = phone;
            RoleIds = roleIds;
        }

        public int Id { get; }
        public string UserName { get; }
        public string Email { get; }
        public string Phone { get; }
        public int[] RoleIds { get; }
    }
}
