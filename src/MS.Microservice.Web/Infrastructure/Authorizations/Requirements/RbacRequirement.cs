using Microsoft.AspNetCore.Authorization;
using System;

namespace MS.Microservice.Web.Infrastructure.Authorizations.Requirements
{
    public class RbacRequirement : IAuthorizationRequirement
    {
        public string[] Issuers { get; }
        public string Path { get; }
        public string ClaimType { internal get; set; }

        public RbacRequirement(string[] issuers, string claimType, string path)
        {
            Issuers = issuers ?? throw new ArgumentNullException(nameof(issuers));
            ClaimType = claimType ?? throw new ArgumentNullException(claimType);
            Path = path ?? throw new ArgumentNullException(path);
        }
    }
}
