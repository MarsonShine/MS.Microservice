namespace MS.Microservice.Core.Ceching
{
    public class CacheOptions
    {
        public CacheOptions()
        {
            KeyPrefix = "Fz.Activation.";
            //AbsoluteExpiration = DateTime.Now.AddMinutes(5);
            SlidingExpirationSecond = 2 * 60 * 60;
        }

        public string KeyPrefix { get; set; }
        public int? AbsoluteExpirationSecond { get; set; }
        public int SlidingExpirationSecond { get; set; }
    }
}
