using MS.Microservice.Excel.Aot;

namespace MS.Microservice.Lab.AotExamples.Static.Excel;

public sealed class GeneratedBook
{
    [ExcelColumn("编号")]
    public int Id { get; set; }
    [ExcelColumn("名称")]
    public string? Name { get; set; }
}

[ExcelSerializable(typeof(GeneratedBook))]
public static partial class GeneratedBooks;
