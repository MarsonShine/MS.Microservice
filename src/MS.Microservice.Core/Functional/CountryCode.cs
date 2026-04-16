namespace MS.Microservice.Core.Functional
{
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

    public record PhoneNumber(string Type, CountryCode CountryCode, string Number)
    {
        public override string ToString()
            => $"{Type}: +{CountryCode} {Number}";
    }

    public static class Demo
    {
        public static void Run()
        {
            // 1c
            Func<int, int> mod5 = F.ApplyR<int,int,int>(F.Remainder, 5);
            Console.WriteLine(mod5(13));   // 3
            Console.WriteLine(mod5(-13));  // 2

            // 2b / 2c
            Func<string, CountryCode, string, PhoneNumber> createPhoneNumber
                = (type, countryCode, number) => new PhoneNumber(type, countryCode, number);

            var createUk = F.ApplyR<string, CountryCode, string, PhoneNumber>(createPhoneNumber, (CountryCode)"uk");
            var createUkMobile = F.ApplyR(createUk, "mobile");

            var p = createUkMobile("07123456789");
            Console.WriteLine(p);
        }
    }
}
