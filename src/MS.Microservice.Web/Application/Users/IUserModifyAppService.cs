using MS.Microservice.Core.Functional;
using MS.Microservice.Web.Application.Commands;

namespace MS.Microservice.Web.Application.Users
{
    public interface IUserModifyAppService
    {
        Task<Either<Error, bool>> ModifyAsync(UserModifyCommand request, CancellationToken cancellationToken = default);
    }
}
