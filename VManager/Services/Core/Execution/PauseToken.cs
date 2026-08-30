// Services/Core/Execution/PauseToken.cs
using System;

namespace VManager.Services.Core.Execution;

public readonly struct PauseToken
{
    private readonly PauseTokenSource? _source;

    internal PauseToken(PauseTokenSource source) => _source = source;

    public bool IsPaused => _source?.IsPaused ?? false;

    public event Action<bool>? PauseChanged
    {
        add { if (_source != null) _source.PauseChanged += value; }
        remove { if (_source != null) _source.PauseChanged -= value; }
    }
}

public class PauseTokenSource
{
    private volatile bool _isPaused;

    public bool IsPaused => _isPaused;
    public PauseToken Token => new(this);

    public event Action<bool>? PauseChanged;

    public void Pause()
    {
        if (_isPaused) return;
        _isPaused = true;
        PauseChanged?.Invoke(true);
    }

    public void Resume()
    {
        if (!_isPaused) return;
        _isPaused = false;
        PauseChanged?.Invoke(false);
    }
}