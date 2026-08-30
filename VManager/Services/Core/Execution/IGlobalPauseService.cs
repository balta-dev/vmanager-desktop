// Services/Core/Execution/IGlobalPauseService.cs
namespace VManager.Services.Core.Execution;

public interface IGlobalPauseService
{
    PauseToken Token { get; }
    void Pause();
    void Resume();
    bool IsPaused { get; }
}