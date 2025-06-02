using System.Runtime.CompilerServices;
using RustyOptions;

namespace MCServerLauncher.Daemon.Utils;

public static class ResultExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T2, TErr> Map<T1, T2, TState, TErr>(this Result<T1, TErr> self, Func<TState, T1, T2> mapper,
        TState state)
        where T1 : notnull
        where T2 : notnull
    {
        return self.Match(
            x => new Result<T2, TErr>(mapper(state, x)),
            Result.Err<T2, TErr>
        );
    }

    public static async ValueTask<Result<T2, TErr>> MapAsTaskAsync<T1, T2, TErr>(
        this Result<T1, TErr> self,
        Func<T1, Task<T2>> mapper)
        where T1 : notnull
        where T2 : notnull
    {
        ArgumentNullException.ThrowIfNull(mapper, nameof(mapper));
        return self.IsOk(out var obj)
            ? new Result<T2, TErr>(await mapper(obj).ConfigureAwait(false))
            : Result.Err<T2, TErr>(self.UnwrapErr());
    }

    public static Error ExceptionErrorMapper<T>(Exception exception)
        where T : notnull
    {
        return new Error().CauseBy(exception);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Unit, Error> Ok()
    {
        return Result.Ok<Unit, Error>(Unit.Default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TResult, Error> Ok<TResult>(TResult ok)
        where TResult : notnull
    {
        return Result.Ok<TResult, Error>(ok);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Unit, Error> Err(Error err)
    {
        return Result.Err<Unit, Error>(err);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TResult, Error> Err<TResult>(Error err)
        where TResult : notnull
    {
        return Result.Err<TResult, Error>(err);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TResult, Error> Err<TResult>(Error err, Error? innerError)
        where TResult : notnull
    {
        return Result.Err<TResult, Error>(err.WithInner(innerError));
    }

    /// <summary>
    ///     ResultExt.Try避免捕获外部变量以提高性能(通过state传)
    /// </summary>
    /// <param name="func">可能抛出异常的lambda函数</param>
    /// <param name="state">函数所需的外部变量</param>
    /// <typeparam name="TState">函数所需的外部变量类型</typeparam>
    /// <returns></returns>
    public static Result<Unit, Exception> Try<TState>(Action<TState> func, TState state)
    {
        try
        {
            func.Invoke(state);
            return Result.OkExn(Unit.Default);
        }
        catch (Exception ex)
        {
            return Result.Err<Unit>(ex);
        }
    }

    /// <summary>
    ///  ResultExt.Try避免捕获外部变量以提高性能(不需要捕获变量的版本)
    /// </summary>
    /// <param name="func">可能抛出异常的lambda函数</param>
    /// <returns></returns>
    public static Result<Unit, Exception> Try(Action func)
    {
        try
        {
            func.Invoke();
            return Result.OkExn(Unit.Default);
        }
        catch (Exception ex)
        {
            return Result.Err<Unit>(ex);
        }
    }

    /// <summary>
    ///     ResultExt.Try避免捕获外部变量以提高性能(通过state传)
    /// </summary>
    /// <param name="func">可能抛出异常的lambda函数</param>
    /// <param name="state">函数所需的外部变量</param>
    /// <typeparam name="TResult">lambda函数返回值</typeparam>
    /// <typeparam name="TState">函数所需的外部变量类型</typeparam>
    /// <returns></returns>
    public static Result<TResult, Exception> Try<TResult, TState>(Func<TState, TResult> func, TState state)
        where TResult : notnull
    {
        try
        {
            return Result.OkExn(func.Invoke(state));
        }
        catch (Exception ex)
        {
            return Result.Err<TResult>(ex);
        }
    }

    /// <summary>
    ///     ResultExt.TryAsync避免捕获外部变量以提高性能(不需要捕获变量的版本)
    /// </summary>
    /// <param name="func">可能抛出异常的lambda函数</param>
    /// <returns></returns>
    public static async Task<Result<Unit, Exception>> TryAsync(Func<Task> func)
    {
        try
        {
            await func.Invoke();
            return Result.OkExn(Unit.Default);
        }
        catch (Exception ex)
        {
            return Result.Err<Unit>(ex);
        }
    }

    /// <summary>
    ///     ResultExt.TryAsync避免捕获外部变量以提高性能(通过state传)
    /// </summary>
    /// <param name="func">可能抛出异常的异步lambda函数</param>
    /// <param name="state">函数所需的外部变量</param>
    /// <typeparam name="TState">函数所需的外部变量类型</typeparam>
    /// <returns></returns>
    public static async Task<Result<Unit, Exception>> TryAsync<TState>(Func<TState, Task> func, TState state)
    {
        try
        {
            await func.Invoke(state);
            return Result.OkExn(Unit.Default);
        }
        catch (Exception ex)
        {
            return Result.Err<Unit>(ex);
        }
    }

    /// <summary>
    ///     ResultExt.TryAsync避免捕获外部变量以提高性能(通过state传), 异步版本
    /// </summary>
    /// <param name="func">可能抛出异常的异步lambda函数</param>
    /// <param name="state">函数所需的外部变量</param>
    /// <typeparam name="TResult">lambda函数返回值</typeparam>
    /// <typeparam name="TState">函数所需的外部变量类型</typeparam>
    /// <returns></returns>
    public static async Task<Result<TResult, Exception>> TryAsync<TResult, TState>(Func<TState, Task<TResult>> func,
        TState state)
        where TResult : notnull
    {
        try
        {
            return Result.OkExn(await func.Invoke(state));
        }
        catch (Exception ex)
        {
            return Result.Err<TResult>(ex);
        }
    }
}