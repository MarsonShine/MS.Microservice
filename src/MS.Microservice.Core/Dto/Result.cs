using MS.Microservice.Core.Functional;
using System;
using System.Diagnostics.CodeAnalysis;

namespace MS.Microservice.Core.Dto
{
	/// <summary>
	/// 表示一个操作的结果，该结果要么成功并包含值 <typeparamref name="T"/>，
	/// 要么失败并包含一个 <see cref="Exception"/>。
	/// </summary>
	/// <typeparam name="T">成功时包含的值类型。</typeparam>
	/// <remarks>
	/// <para>
	/// Result 是函数式编程中"Railway Oriented Programming"模式的核心类型：
	/// 每个操作返回 <see cref="Result{T}"/>，调用方通过 <see cref="Match{R}"/>、
	/// <see cref="Map{R}"/> 或 <see cref="Bind{R}"/> 来处理结果，
	/// 而不是用 try/catch 控制流。
	/// </para>
	/// <para>
	/// 参考：<see href="https://andrewlock.net/working-with-the-result-pattern-part-1-replacing-exceptions-as-control-flow/#making-the-result-pattern-safer"/>
	/// </para>
	/// </remarks>
	public class Result<T>
	{
		private readonly T? _value;
		private readonly Exception? _error;

		private Result(T value)
		{
			IsSuccess = true;
			_value = value;
			_error = null;
		}

		private Result(Exception error)
		{
			IsSuccess = false;
			_value = default;
			_error = error;
		}

		/// <summary>
		/// 当结果为成功时为 <c>true</c>，此时 <c>_value</c> 非 null；
		/// 失败时为 <c>false</c>，此时 <c>_error</c> 非 null。
		/// </summary>
		// https://github.com/dotnet/csharplang/blob/main/proposals/TypeUnions.md
		[MemberNotNullWhen(true, nameof(_value))]
		[MemberNotNullWhen(false, nameof(_error))]
		public bool IsSuccess { get; private set; }

		/// <summary>
		/// 指示操作是否失败。
		/// </summary>
		public bool IsFailure => !IsSuccess;

		/// <summary>
		/// 成功时返回内部值；失败时抛出异常。
		/// </summary>
		public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Result 处于失败状态，无法读取 Value。");

		/// <summary>
		/// 失败时返回内部异常；成功时抛出异常。
		/// </summary>
		public Exception Error => IsFailure ? _error! : throw new InvalidOperationException("Result 处于成功状态，无法读取 Error。");

		// ── Match（模式匹配）──────────────────────────────────────────────────────

		/// <summary>
		/// 对 Result 进行穷举式模式匹配：
		/// 成功时执行 <paramref name="onSuccess"/>，失败时执行 <paramref name="onFailure"/>。
		/// </summary>
		/// <typeparam name="R">返回结果类型。</typeparam>
		/// <param name="onSuccess">成功分支，接收成功值。</param>
		/// <param name="onFailure">失败分支，接收异常。</param>
		public R Match<R>(Func<T, R> onSuccess, Func<Exception, R> onFailure)
			=> IsSuccess ? onSuccess(_value!) : onFailure(_error!);

		/// <summary>
		/// 对 Result 进行副作用式模式匹配，返回 <see cref="Unit"/> 以保持函数式风格。
		/// </summary>
		public Unit Match(Action<T> onSuccess, Action<Exception> onFailure)
		{
			if (IsSuccess) onSuccess(_value!);
			else onFailure(_error!);
			return Unit.Default;
		}

		// ── Map（函子操作）────────────────────────────────────────────────────────

		/// <summary>
		/// <b>Map</b>：若结果为成功，则用 <paramref name="f"/> 变换成功值，返回新的 Result。
		/// 若结果为失败，则直接传播错误，<paramref name="f"/> 不会被执行。
		/// </summary>
		public Result<R> Map<R>(Func<T, R> f)
			=> IsSuccess
				? Result<R>.Success(f(_value))
				: Result<R>.Fail(_error);

		// ── Bind（单子操作）───────────────────────────────────────────────────────

		/// <summary>
		/// <b>Bind</b>（FlatMap）：若结果为成功，则将成功值传递给 <paramref name="f"/>，
		/// 返回 <paramref name="f"/> 产生的 Result（可能再次成功或失败）。
		/// 若结果为失败，则直接传播错误，<paramref name="f"/> 不会被执行。
		/// </summary>
		/// <remarks>
		/// 使用 Bind 可将多个可能失败的操作串联为一条"铁路"（Railway），
		/// 任意一步失败即短路，将错误沿管道传递到末端统一处理。
		/// </remarks>
		public Result<R> Bind<R>(Func<T, Result<R>> f)
			=> IsSuccess ? f(_value) : Result<R>.Fail(_error);

		// ── 工厂方法与隐式转换 ────────────────────────────────────────────────────

		/// <summary>创建包含 <paramref name="value"/> 的成功结果。</summary>
		public static Result<T> Success(T value) => new(value);

		/// <summary>创建包含 <paramref name="error"/> 的失败结果。</summary>
		public static Result<T> Fail(Exception error) => new(error);

		/// <summary>
		/// 允许将 <typeparamref name="T"/> 值直接赋值给 <see cref="Result{T}"/>，
		/// 自动包装为成功结果。
		/// </summary>
		public static implicit operator Result<T>(T value) => Success(value);
	}
}
