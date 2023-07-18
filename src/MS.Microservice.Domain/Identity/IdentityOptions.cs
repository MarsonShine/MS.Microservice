namespace MS.Microservice.Domain.Identity
{
    public class IdentityOptions
    {
        public const string Name = "IdentityOptions";
        public AuthenticationOption? AuthenticationOption { get; set; }
        public ActivationJwtBearerOption? JwtBearerOption { get; set; }
    }

    public class AuthenticationOption
    {
        /// <summary>
        /// 受用者（谁可以使用令牌经过认证）
        /// </summary>
        public string[]? Audiences { get; set; }
        /// <summary>
        /// 授权者（一般是认证服务中心）
        /// </summary>
        public string[]? Issuers { get; set; }
    }

    public class ActivationJwtBearerOption
    {
        /// <summary>
        /// 受用者（谁可以使用令牌经过认证）
        /// </summary>
        public string[]? Audiences { get; set; }
        /// <summary>
        /// 授权者（一般是认证服务中心）
        /// </summary>
        public string[]? Issuers { get; set; }
        /// <summary>
        /// 安全密钥
        /// </summary>
        public string[]? SecurityKeys { get; set; }
        /// <summary>
        /// 有效期，单位秒
        /// </summary>
        public int Expires { get; set; }
    }
}
