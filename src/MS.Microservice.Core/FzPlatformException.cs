using System;
using System.Runtime.Serialization;

namespace MS.Microservice.Core
{
    public class MsPlatformException : Exception
    {
        public MsPlatformException() { }

        protected MsPlatformException(string message) : base(message)
        {
        }

        public MsPlatformException(int code,string message) : base(message)
        {
            Code = code;
        }

        public MsPlatformException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected MsPlatformException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public int Code { get; set; }

    }
}
