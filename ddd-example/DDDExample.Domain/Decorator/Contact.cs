using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DDDExample.Domain.Decorator
{
    public class Contact
    {
        private readonly User user;
        private List<Friendship>? friendships;
        private List<Moments>? moments;
        public Contact(User user)
        {
            this.user = user;
        }

        // 社交上下文
        public void Make(Friendship friend) { }
        public void Break(Friendship friend) { }
    }
}
