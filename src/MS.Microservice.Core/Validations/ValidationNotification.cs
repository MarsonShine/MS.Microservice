using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Validations
{
    /// <summary>
    /// https://martinfowler.com/articles/replaceThrowWithNotification.html
    /// </summary>
    public class ValidationNotification
    {
        private List<Error> errors = new List<Error>();
        public void AddError(string message)
        {
            AddError(message, new Exception(message));
        }

        public void AddError(string message, Exception exception)
        {
            errors.Add(new Error(message, exception));
        }

        public string ErrorMessage => string.Join(',', errors.Select(p => p.Message).ToArray());

        private class Error
        {
            internal Error(string message, Exception exception)
            {
                Message = message;
                Exception = exception;
            }
            public string Message { get;}
            public Exception Exception { get;}
        }
    }
}
