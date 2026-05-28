using static MS.Microservice.Core.Functional.PhoneNumber;

namespace MS.Microservice.Core.Functional
{
    using static F;
    public readonly record struct CountryCode
    {
        private readonly string _value;

        public CountryCode(string value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static implicit operator string(CountryCode cc) => cc._value;
        public static implicit operator CountryCode(string s) => new(s);

        public override string ToString() => _value;
    }

    public record PhoneNumber(NumberType Type, CountryCode CountryCode, Number Nr)
    {
        public static Func<NumberType, CountryCode, Number, PhoneNumber> Create
            = (type, countryCode, nr) => new PhoneNumber(type, countryCode, nr);
        public override string ToString()
            => $"{Type}: +{CountryCode} {Nr}";

        public enum NumberType { Mobile, Home, Office }

        public struct Number
        {

        }

        public static Func<CountryCode, Validation<CountryCode>> ValidCountryCode = countryCode =>
            countryCode.ToString() switch
            {
                "uk" => Valid(countryCode),
                "us" => Valid(countryCode),
                _ => Invalid(new Error("0", $"Unsupported country code: {countryCode}"))
            };

        public static Func<Number, Validation<Number>> ValidNumber = number =>
            number.ToString() switch
            {
                _ => Valid(number)
            };

        public static Func<NumberType, Validation<NumberType>> ValidNumberType = numberType =>
            numberType switch
            {
                NumberType.Mobile => Valid(numberType),
                NumberType.Home => Valid(numberType),
                NumberType.Office => Valid(numberType),
                _ => Invalid(new Error("0", $"Unsupported number type: {numberType}"))
            };

        public static Validation<PhoneNumber> CreatePhoneNumber(NumberType type, CountryCode countryCode, Number number) => Valid(Create)
            .Apply(ValidNumberType(type))
            .Apply(ValidCountryCode(countryCode))
            .Apply(ValidNumber(number));
    }

    public static class Demo
    {
        public static void Run()
        {
            // 1c
            Func<int, int> mod5 = F.ApplyR<int, int, int>(F.Remainder, 5);
            Console.WriteLine(mod5(13));   // 3
            Console.WriteLine(mod5(-13));  // 2

            // 2b / 2c
            Func<NumberType, Number, CountryCode, PhoneNumber> createPhoneNumber
                = (type, number, countryCode) => new PhoneNumber(type, countryCode, number);

            var createUk = F.ApplyR<NumberType, Number, CountryCode, PhoneNumber>(createPhoneNumber, new CountryCode("uk"));
            var createUkMobile = F.ApplyR(createUk, new Number());
            var p = createUkMobile(NumberType.Mobile);
            Console.WriteLine(p);
        }
    }
}
