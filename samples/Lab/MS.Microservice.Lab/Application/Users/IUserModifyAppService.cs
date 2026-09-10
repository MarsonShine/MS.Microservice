using MS.Microservice.Core.Functional;
using MS.Microservice.Lab.Application.Commands;

namespace MS.Microservice.Lab.Application.Users
{
    public interface IUserModifyAppService
    {
        Task<Either<Error, bool>> ModifyAsync(UserModifyCommand request, CancellationToken cancellationToken = default);
    }
}
