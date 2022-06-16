using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DDDExample.Domain.SwitchContext
{
    public interface ISocialContext
    {
        interface IContact
        {
            void Make(Friendship friend);
            void Break(Friendship friend);
        }

        IContact AsContact(User user);
    }
}
