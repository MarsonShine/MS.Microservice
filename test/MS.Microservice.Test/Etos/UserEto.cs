using MS.Microservice.Domain;

namespace MS.Microservice.Test.Etos
{
    public class UserEto : EventBase
    {
        public UserEto() { }
        public UserEto(string userName, int age, bool enabled)
        {
            UserName = userName;
            Age = age;
            Enabled = enabled;
        }
        public string UserName { get; } = null!;
        public int Age { get; }
        public bool Enabled { get; set; }
    }
}
