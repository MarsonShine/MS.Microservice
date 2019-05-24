namespace MS.Infrastructure.Util.Mail
{
    using MimeKit;
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Sender 默认的无参构造函数默认为越海系统邮箱发送者
    /// 带参构造函数指定发送者
    /// </summary>
    public class Sender
    {
        public Sender() : this("yhsystem", "mail@yhglobal.com") { }
        public Sender(string name, string address)
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

        public MailboxAddress ToFrom()
        {
            return new MailboxAddress(Name, EmailAddress);
        }
    }
}
