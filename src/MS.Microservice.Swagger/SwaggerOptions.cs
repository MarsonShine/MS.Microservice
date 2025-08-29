namespace MS.Microservice.Swagger.Swagger
{
    public class SwaggerOptions
    {
        public bool IsEnabled { get; set; }
        public bool EnabledSecurity { get; set; }
        public string? SwaggerXmlFile { get; set; }
        public string? RoutePrefix { get; set; }
        public bool IsAuth { get; set; }
        public string? Name { get; set; }
    }
}
