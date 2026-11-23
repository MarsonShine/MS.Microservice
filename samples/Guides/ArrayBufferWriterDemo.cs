#:property PublishAot=false

using System.Buffers;
using System.Text;
using System.Text.Json;

ShowAppend();
ShowReuse();
await ShowShortReadAsync();
ShowJson();
ShowAllocations();

static void ShowAppend()
{
    var buffer = new ArrayBufferWriter<byte>(initialCapacity: 4);
    Span<byte> destination = buffer.GetSpan(2);
    "AB"u8.CopyTo(destination);
    Console.WriteLine($"append/before: count={buffer.WrittenCount}, text='{Encoding.UTF8.GetString(buffer.WrittenSpan)}'");

    buffer.Advance(2);
    Console.WriteLine($"append/after: count={buffer.WrittenCount}, text='{Encoding.UTF8.GetString(buffer.WrittenSpan)}'");

    "CDE"u8.CopyTo(buffer.GetSpan(3));
    buffer.Advance(3);
    Console.WriteLine($"append/grown: count={buffer.WrittenCount}, text='{Encoding.UTF8.GetString(buffer.WrittenSpan)}'");
}

static void ShowReuse()
{
    var buffer = new ArrayBufferWriter<byte>(initialCapacity: 8);
    buffer.Write("ABCDE"u8);
    ReadOnlyMemory<byte> view = buffer.WrittenMemory;
    byte[] snapshot = buffer.WrittenSpan.ToArray();

    buffer.ResetWrittenCount();
    Console.WriteLine($"reuse/reset: count={buffer.WrittenCount}");
    buffer.Write("XYZ"u8);
    // 容量足以复用原数组；特意查看旧视图，展示保留它的后果。
    Console.WriteLine($"reuse/overwritten: current={Encoding.UTF8.GetString(buffer.WrittenSpan)}, oldView={Encoding.UTF8.GetString(view.Span)}, copy={Encoding.UTF8.GetString(snapshot)}");

    buffer.Clear();
    Console.WriteLine($"reuse/clear: count={buffer.WrittenCount}, oldViewHex={Convert.ToHexString(view.Span)}");
}

static async Task ShowShortReadAsync()
{
    const string original = "猫A";
    byte[] source = Encoding.UTF8.GetBytes(original);
    using var input = new MemoryStream(source);
    var buffer = new ArrayBufferWriter<byte>();
    const int requested = 8;
    Memory<byte> destination = buffer.GetMemory(requested)[..requested];
    int read = await input.ReadAsync(destination);
    buffer.Advance(read);
    Console.WriteLine($"read/source: text={original}, utf16Length={original.Length}, hex={Convert.ToHexString(source)}");
    Console.WriteLine($"read/short: requested={requested}, read={read}, count={buffer.WrittenCount}, text={Encoding.UTF8.GetString(buffer.WrittenSpan)}, bytesEqual={buffer.WrittenSpan.SequenceEqual(source)}");
}

static void ShowJson()
{
    var notice = new { Id = 7, Read = true };
    byte[] independent = JsonSerializer.SerializeToUtf8Bytes(notice);
    var buffer = new ArrayBufferWriter<byte>();
    using (var json = new Utf8JsonWriter(buffer))
    {
        json.WriteStartObject();
        json.WriteNumber("Id", notice.Id);
        json.WriteBoolean("Read", notice.Read);
        json.WriteEndObject();
        json.Flush();
    }

    Console.WriteLine($"json/serializer: {Encoding.UTF8.GetString(independent)}");
    Console.WriteLine($"json/writer: {Encoding.UTF8.GetString(buffer.WrittenSpan)}");
    Console.WriteLine($"json/compare: bytes={buffer.WrittenCount}, equal={buffer.WrittenSpan.SequenceEqual(independent)}");
}

static void ShowAllocations()
{
    const int iterations = 1024;
    byte[] source = new byte[4096];
    Array.Fill(source, (byte)0x5A);
    var buffer = new ArrayBufferWriter<byte>(source.Length);

    Measure(copy: false, count: 16);
    Measure(copy: true, count: 16);
    long viewBytes = Measure(copy: false, count: iterations);
    long snapshotBytes = Measure(copy: true, count: iterations);
    Console.WriteLine($"alloc/input: payload={source.Length} B, iterations={iterations}");
    Console.WriteLine($"alloc/view: {viewBytes} B total, {viewBytes / iterations} B/op");
    Console.WriteLine($"alloc/snapshot: {snapshotBytes} B total, {snapshotBytes / iterations} B/op");

    long Measure(bool copy, int count)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++)
        {
            buffer.ResetWrittenCount();
            buffer.Write(source);
            ReadOnlySpan<byte> output = copy ? buffer.WrittenSpan.ToArray() : buffer.WrittenSpan;
            if (!output.SequenceEqual(source))
            {
                throw new InvalidDataException("Unexpected output bytes.");
            }
        }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
