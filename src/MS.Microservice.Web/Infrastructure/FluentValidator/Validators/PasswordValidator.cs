using FluentValidation;
using FluentValidation.Results;
using FluentValidation.Validators;
using MS.Microservice.Core.Extension;
using System.Text.RegularExpressions;

namespace MS.Microservice.Web.Infrastructure.FluentValidator.Validators
{
    public class PasswordValidator<T> : PropertyValidator<T, string>
    {
        private const string PropertyName = "密码";
        private readonly int _minLength;
        private readonly int? _maxLength;
        private readonly bool _isLetter;
        private readonly bool _isNumber;

        private readonly Regex ruler = new Regex("^[A-Za-z0-9]+$");
        public override string Name => "PasswordValidator";

        public PasswordValidator(int minLength, int? maxLength, bool isLetter = true, bool isNumber = true)
        {
            _minLength = minLength;
            _maxLength = maxLength;
            _isLetter = isLetter;
            _isNumber = isNumber;
        }

        protected override string GetDefaultMessageTemplate(string errorCode) => "密码强度过低";

        public override bool IsValid(ValidationContext<T> context, string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                context.AddFailure(new ValidationFailure(PropertyName, "不能为空"));
                return false;
            }
            if (value.Length < _minLength)
            {
                context.AddFailure(new ValidationFailure(PropertyName, $"长度必须大于{_minLength}"));
                return false;
            }
            if (_maxLength.HasValue && value.Length > _maxLength)
            {
                context.AddFailure(new ValidationFailure(PropertyName, $"长度必须小于{_maxLength}"));
                return false;
            }
            if (_isLetter && _isNumber && !ruler.IsMatch(value))
            {
                context.AddFailure(new ValidationFailure(PropertyName, "密码必须由至少字母+数字组成"));
                return false;
            }
            return true;
        }

    }
}
