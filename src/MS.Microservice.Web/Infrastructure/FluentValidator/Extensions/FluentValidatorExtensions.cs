using MS.Microservice.Web.Infrastructure.FluentValidator.Validators;

namespace FluentValidation
{
    public static class FluentValidatorExtensions
    {
        public static IRuleBuilderOptions<T, string> Password<T>(this IRuleBuilder<T, string> ruleBuilder, int minLength, int? maxLength = null)
        {
            return ruleBuilder.SetValidator(new PasswordValidator<T>(minLength, maxLength));
        }

        public static IRuleBuilderOptions<T, string> Telephone<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            return ruleBuilder.Matches(@"^1(3|4|5|6|7|8|9)\d{9}$");
        }
    }
}
