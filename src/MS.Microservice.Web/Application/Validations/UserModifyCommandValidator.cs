using FluentValidation;
using MS.Microservice.Web.Application.Commands;

namespace MS.Microservice.Web.Application.Validations
{
    public class UserModifyCommandValidator : AbstractValidator<UserModifyCommand>
    {
        public UserModifyCommandValidator()
        {
            RuleFor(p => p.Passowrd).Password(minLength: 8,maxLength:20).When(p => p.Passowrd?.Length > 0).WithMessage("密码格式不正确");
            RuleFor(p => p.Telephone).Telephone().When(p => p.Telephone?.Length > 0).WithMessage("手机号码格式不正确");
            RuleFor(p => p.Email).EmailAddress().When(p => p.Email?.Length > 0).WithMessage("邮件格式不正确");
        }
    }
}
