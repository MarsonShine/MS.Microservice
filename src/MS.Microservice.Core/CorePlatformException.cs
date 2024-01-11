using System;
using System.Runtime.Serialization;

namespace MS.Microservice.Core
{
    public class CorePlatformException : Exception
    {
        public CorePlatformException() { }

        protected CorePlatformException(string message) : base(message)
        {
        }

        public CorePlatformException(int code,string message) : base(message)
        {
            Code = code;
        }

        public CorePlatformException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public int Code { get; set; }

    }
}
