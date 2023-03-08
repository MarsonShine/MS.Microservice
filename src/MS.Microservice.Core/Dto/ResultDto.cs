namespace MS.Microservice.Core.Dto
{
    public class ResultDto
    {
        public ResultDto(string message, int code) : this(false, message, code) { }
        public ResultDto(bool success, string message, int code)
        {
            Success = success;
            Message = message;
            Code = code;
        }

        public bool Success { get; set; }
        public string Message { get; set; }
        public int Code { get; set; }
    }

    public class ResultDto<T> : ResultDto
    {
        public ResultDto(T data) : this(data, true, "", 200)
        {

        }
        public ResultDto(T data, bool success, string message, int code) : base(success, message, code)
        {
            Success = success;
            Message = message;
            Code = code;
            Data = data;
        }

        public T Data { get; set; }
    }
}
