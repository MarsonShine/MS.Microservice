using FluentValidation;
using MS.Microservice.Web.Application.Commands;

namespace MS.Microservice.Web.Application.Validations
{
    public class UserCreatedCommandValidator : AbstractValidator<UserCreatedCommand>
    {
        public UserCreatedCommandValidator()
        {
            RuleFor(p => p.UserName).NotEmpty().WithMessage("姓名不能为空");
            RuleFor(p => p.Passowrd).Password(minLength: 8).WithMessage("密码格式不正确");
            RuleFor(p => p.Telephone).Telephone().WithMessage("手机号码格式不正确");
            RuleFor(p => p.Email).EmailAddress().WithMessage("邮件格式不正确");
        }
    }
}
