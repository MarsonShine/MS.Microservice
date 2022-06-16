namespace DDDExample.Domain.SwitchContext
{
    public interface ISubscriptionContext
    {
        interface IReader
        {
            bool CanView(Content content);
        }

        IReader AsReader(User user);
    }
}
