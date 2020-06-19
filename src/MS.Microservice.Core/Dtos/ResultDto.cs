namespace MS.Microservice.Core.Dtos
{
    public class ResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int Code { get; set; }
    }

    public class ResultDto<T> : ResultDto
    {
        public ResultDto(T data) : this(true, "")
        {
            Data = data;
        }
        public ResultDto(bool success, string error)
        {
            Success = success;
            Message = error;
        }

        public T Data { get; set; }
    }
}
