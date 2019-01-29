using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MS.Microservice.Web
{
    public class SwaggerConsts
    {
        public const string API_NAME = "orderingapi";

        public static readonly string ENDPOINT_URL = $"/swagger/{API_NAME}/swagger.json";
        public const string ENDPOINT_NAME = "Ordering.API V1";

        public const string OAUTH_CLIENTID = "orderingswaggerui";
        public const string OAUTH_APPNAME = "Ordering Swagger UI";

        public const string DOC_TITLE = "Ordering Services";
        public const string DOC_VERSION = "v1";

    }
}
