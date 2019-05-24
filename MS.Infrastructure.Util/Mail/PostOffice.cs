namespace MS.Infrastructure.Util.Mail
{
    using MailKit.Net.Smtp;
    using MimeKit;
    using System;
    using System.Collections.Generic;
    /// <summary>
    /// 邮局，构造函数初始化越海系统邮件发送者
    /// 非线程安全，推荐用链式调用
    /// <para>因为发送邮件通常内容固定话，可以用模版设计模式abstract出来一个CommonTempleteMail</para>
    /// </summary>
    public class PostOffice
    {
        private readonly ReceiverCollection _receivers;
        private Sender sender;
        private MailMessage message;
        private string subject;
        public PostOffice(List<Receiver> receivers)
        {
            sender = new Sender();
            _receivers = new ReceiverCollection(receivers);
        }

        public PostOffice WriteMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("the message sent can not be empty");
            message = new MailMessage(text);
            return this;
        }

        public PostOffice SetSender(string name, string mailAddress)
        {
            sender = new Sender(name, mailAddress);
            return this;
        }
        public PostOffice SetSubject(string subject)
        {
            this.subject = subject;
            return this;
        }
        public PostOffice AddReceiver(string name, string mailAddress)
        {
            _receivers.Add(new Receiver(name, mailAddress));
            return this;
        }

        public void Send()
        {
            if (_receivers.Count == 0)
                throw new InvalidOperationException("empty receiver");
            //create message
            var mimeMessage = new MimeMessage();
            mimeMessage.From.Add(sender.ToFrom());
            mimeMessage.To.AddRange(_receivers.ToMailAddresses());
            mimeMessage.Subject = subject;
            mimeMessage.Body = message.Body;
            //简历链接，发送
            using (var client = new SmtpClient())
            {
                client.ServerCertificateValidationCallback = (s, c, h, e) => true;
                client.Connect(Smtp.MailHost, Smtp.Port, useSsl: true);
                client.Authenticate(Smtp.MailUserName, Smtp.MailPassWord);
                client.Send(mimeMessage);
                client.Disconnect(true);
            }
        }
    }
}
