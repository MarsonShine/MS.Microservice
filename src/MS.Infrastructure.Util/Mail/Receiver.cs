namespace MS.Infrastructure.Util.Mail
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    public class Receiver
    {
        public Receiver(string name, string address)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrEmpty(address))
                throw new ArgumentNullException(nameof(address));
            MailValidator.ThrowIfInvalid(address);
            Name = name;
            EmailAddress = address;
        }
        public string Name { get; set; }
        public string EmailAddress { get; set; }

        public static ReceiverCollection AsList(params Receiver[] toers)
        {
            return new ReceiverCollection(toers);
        }
    }
}
