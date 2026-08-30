// Services/Core/Execution/GlobalPauseService.cs
using System;

namespace VManager.Services.Core.Execution;

public sealed class GlobalPauseService
{
    private static readonly Lazy<GlobalPauseService> _instance = new(() => new GlobalPauseService());
    public static GlobalPauseService Instance => _instance.Value;

    private readonly PauseTokenSource _source = new();

    public PauseToken Token => _source.Token;
    public bool IsPaused => _source.IsPaused;

    public void Pause() => _source.Pause();
    public void Resume() => _source.Resume();

    private GlobalPauseService() { }
}