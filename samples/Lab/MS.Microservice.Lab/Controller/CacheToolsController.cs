using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Caching;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;

namespace MS.Microservice.Lab.Controller;

public sealed class CacheDemoWriteRequest
{
    /// <summary>新的示例源数据，最多 256 字符，默认“已更新”。</summary>
    [Required, StringLength(256), DefaultValue("已更新")] public string Value { get; set; } = "已更新";
}
public sealed record CacheDemoResult(string Value, long SourceReads, int SharedTtlSeconds, int LocalTtlSeconds);

[LabOnly, Authorize, Route("api/lab/tools/cache"), Tags("工具示例：缓存治理")]
public sealed class CacheToolsController(ToolDemoFiles files) : ToolDemoControllerBase(files)
{
    private IGovernedCache Cache => HttpContext.RequestServices.GetRequiredService<IGovernedCache>();
    private CacheDemo Source => HttpContext.RequestServices.GetRequiredService<CacheDemo>();
    private string Scope => TaskCenterDemo.Scope(Owner);
    private CacheKey<string> Key => Cache.CreateKey<string>("lab.cache.v1", Scope, "sample");

    /// <summary>读取本人示例数据，重复调用观察缓存减少源读取。</summary>
    /// <remarks>默认本地 30 秒、共享 300 秒。sourceReads 是本进程所有示例用户的累计源读取数；连续读取同一用户通常保持不变。示例源在内存中，重启后重置；当前用户 Scope 隔离缓存键。</remarks>
    [HttpGet, Produces("application/json"), ProducesResponseType<CacheDemoResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get()
    {
        var key = Key;
        var value = await Cache.GetOrCreateAsync(key, (Source, Scope), static (state, token) => state.Source.Read(state.Scope, token), Token);
        return Ok(new CacheDemoResult(value, Source.Reads, (int)key.Policy.Expiration.TotalSeconds, (int)key.Policy.LocalExpiration.TotalSeconds));
    }
    /// <summary>修改本人源数据并使对应缓存失效。</summary>
    /// <remarks>先修改源，再清除缓存；随后 GET 会重新加载。真实数据库业务应在成功提交后失效。该示例不承诺其他实例的本地缓存立即失效。</remarks>
    [HttpPut, ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Put(CacheDemoWriteRequest request)
    { Source.Write(Scope, request.Value); await Cache.RemoveAsync(Key, Token); return NoContent(); }
    /// <summary>主动清除本人示例缓存，保留源数据。</summary>
    /// <remarks>清除后再次 GET 会增加一次源读取，便于观察缓存失效和重建。</remarks>
    [HttpDelete, ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete() { await Cache.RemoveAsync(Key, Token); return NoContent(); }
}
