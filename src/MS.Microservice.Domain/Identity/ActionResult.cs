using MS.Microservice.Domain.Aggregates.IdentityModel;
using System.Collections.Generic;
using System.Linq;

namespace MS.Microservice.Domain.Identity
{
    public class ActionResult
    {
        public int[] Roles { get; }
        public string[] Actions { get; }

        public ActionResult(User user)
        {
            Roles = user.Roles
            .Select(r => r.Id)
            .ToArray();

            List<string> acts = new List<string>();
            foreach(var r in user.Roles)
            {
                acts.AddRange(r.Actions.Select(a => a.Path));
            }
            Actions = acts.Distinct().ToArray();
        }
    }
}
