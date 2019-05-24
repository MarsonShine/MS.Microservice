namespace MS.Infrastructure.Util.Mail
{
    using MimeKit;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    public class ReceiverCollection : ICollection<Receiver>
    {
        private readonly List<Receiver> _receivers = new List<Receiver>();
        public ReceiverCollection(IEnumerable<Receiver> receivers)
        {
            if (receivers.Any())
                _receivers = receivers.ToList();

        }

        public int Count => _receivers.Count;

        public bool IsReadOnly => true;
        /// <summary>
        /// 添加抄送人，接收人
        /// </summary>
        /// <param name="item"></param>
        public void Add(Receiver item)
        {
            _receivers.Add(item);
        }

        public void Clear()
        {
            _receivers.Clear();
        }

        public bool Contains(Receiver item)
        {
            return _receivers.Contains(item);
        }

        public void CopyTo(Receiver[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<Receiver> GetEnumerator()
        {
            return _receivers.GetEnumerator();
        }

        public bool Remove(Receiver item)
        {
            return _receivers.Remove(item);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public List<MailboxAddress> ToMailAddresses()
        {
            if (_receivers.Count == 0) throw new InvalidCastException("elements are empty");
            return _receivers.ConvertAll(r => new MailboxAddress(r.Name, r.EmailAddress));
        }
    }
}
