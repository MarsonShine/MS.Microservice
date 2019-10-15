namespace MS.Infrastructure.Util.Mail
{
    using MimeKit;
    using System;
    using System.Collections.Generic;
    using System.Text;

    public class MailMessage
    {
        private readonly TextPart _messageBody;
        public MailMessage(string text)
        {
            _messageBody = new TextPart("plain")
            {
                Text = text
            };
        }

        public TextPart Body => _messageBody;
    }
}
