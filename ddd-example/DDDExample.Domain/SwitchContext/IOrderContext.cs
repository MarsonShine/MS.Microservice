namespace DDDExample.Domain.SwitchContext
{
    public interface IOrderContext
    {
        interface IBuyer
        {
            void PlaceOrder(Column column);
        }

        IBuyer AsBuyer(User user);
    }
}
