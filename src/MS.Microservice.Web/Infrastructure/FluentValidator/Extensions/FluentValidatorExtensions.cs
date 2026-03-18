using MS.Microservice.Web.Infrastructure.FluentValidator.Validators;

namespace FluentValidation
{
    public static partial class FluentValidatorExtensions
    {
        extension<T>(IRuleBuilder<T, string> ruleBuilder)
        {
            public IRuleBuilderOptions<T, string> Password(int minLength, int? maxLength = null)
                => ruleBuilder.SetValidator(new PasswordValidator<T>(minLength, maxLength));

            public IRuleBuilderOptions<T, string> Telephone()
                => ruleBuilder.Matches(@"^1(3|4|5|6|7|8|9)\d{9}$");
        }
    }
}
