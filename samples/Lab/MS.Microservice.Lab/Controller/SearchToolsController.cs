using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;
using MS.Microservice.Search;

namespace MS.Microservice.Lab.Controller;

public sealed class DemoSearchQuery
{
    /// <summary>搜索内容，例如 报告、/finance/ 或 revenue；不可为空。</summary>
    [Required, StringLength(256)] public string Query { get; set; } = "";
    /// <summary>0=文件名，1=逻辑路径，2=正文。</summary>
    [DefaultValue(SearchTarget.FileName)] public SearchTarget Target { get; set; }
    /// <summary>文件名/路径默认 Contains，正文默认 AllTerms；正文支持 AllTerms/Phrase，元数据支持 Contains/Prefix。</summary>
    public SearchMatch? Match { get; set; }
    /// <summary>0=Fast 默认不计算总数；1=Exact 返回精确 totalHits，常见词查询会更慢。</summary>
    [DefaultValue(SearchCountMode.Fast)] public SearchCountMode CountMode { get; set; }
    /// <summary>默认前 50 条，最多 200 条。</summary>
    [Range(1, 200), DefaultValue(50)] public int Limit { get; set; } = 50;
    /// <summary>默认按 Path 排序以优先响应；正文可显式选择 Relevance 按相关度排序。</summary>
    public SearchSort? Sort { get; set; }
    /// <summary>可选扩展名过滤，例如 .docx。</summary>
    public string? Extension { get; set; }
    /// <summary>可选逻辑目录前缀过滤，例如 /finance/。</summary>
    public string? DirectoryPrefix { get; set; }
    /// <summary>正文摘要字符数，默认 0 不提取，最大 1000；摘要是纯文本。</summary>
    [Range(0, 1000), DefaultValue(0)] public int SnippetLength { get; set; }
    /// <summary>上一页 nextCursor；索引更新后返回 409，此时从第一页重新搜索。</summary>
    public string? Cursor { get; set; }
}

[LabOnly, Authorize]
[Route("api/lab/tools/search")]
[Tags("工具示例：文件名、路径与全文检索")]
public sealed class SearchToolsController(ToolDemoFiles files, DocumentSearchIndex index) : ToolDemoControllerBase(files)
{
    /// <summary>搜索当前用户已建立索引的文件名、逻辑路径或正文。</summary>
    /// <remarks>先上传文件并通过 /tasks/documents/index 建立索引，任务成功后再查询。默认快速返回前 50 条，totalHits 为 null，hasMore 仍准确；countMode=Exact 按需计算精确总数。查询只读取索引，不运行 PDF/DOCX 提取或 OCR。正文采用词项/短语检索，文件名和路径采用大小写归一后的精确子串/前缀匹配。仅索引中已发布的数据可见，结果按当前登录用户隔离。</remarks>
    [HttpGet, Produces("application/json")]
    [ProducesResponseType<SearchResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public IActionResult Search([FromQuery] DemoSearchQuery request)
    {
        try
        {
            return Ok(index.Search(new(TaskCenterDemo.Scope(Owner), request.Query, request.Target, request.Match,
                request.CountMode, request.Limit, request.Sort, request.Extension, request.DirectoryPrefix,
                SnippetLength: request.SnippetLength, Cursor: request.Cursor), Token));
        }
        catch (SearchCursorExpiredException error) { return Conflict(new ProblemDetails { Status = 409, Detail = error.Message }); }
    }

    /// <summary>以更高版本删除当前用户文档的索引。</summary>
    /// <remarks>version 必须高于该文档已索引的版本，首次 version=1 的文档可用 version=2 删除；保留删除版本可阻止旧任务重新写回。只删除索引，原始上传文件可另用 /files 的 DELETE 清理。低版本返回 stale，不会删除较新的索引。</remarks>
    [HttpDelete("{documentId}"), Produces("application/json")]
    [ProducesResponseType<SearchBatchResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public IActionResult Delete(string documentId, [Range(typeof(long), "1", "9223372036854775807")] long version)
    {
        try
        {
            var result = index.ApplyBatch([SearchChange.Delete(TaskCenterDemo.Scope(Owner), documentId, version)], Token);
            index.Publish(Token);
            return Ok(result);
        }
        catch (SearchVersionConflictException error) { return Conflict(new ProblemDetails { Status = 409, Detail = error.Message }); }
    }
}
