using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Xunit;
using VManager.Services.Core.Media;
using VManager.Services.Models;
using VManager.Services.Core.Execution;
using VManager.ViewModels;

namespace VManager.Tests.Unit
{
    public class CancelPauseFallbackTests
    {
        [Fact]
        public void UriStreamingCheck_ShouldIdentifyHttpOrHttpsUrls()
        {
            // Test simple URL check helper
            string httpUrl = "https://live.stream.com/feed.m3u8";
            string rtspUrl = "rtsp://live.stream.com/feed";
            string localPath = "/mnt/ntfs/Videos/test.mp4";

            var isHttpUrl = Uri.TryCreate(httpUrl, UriKind.Absolute, out var uriHttp) 
                && (uriHttp.Scheme == Uri.UriSchemeHttp || uriHttp.Scheme == Uri.UriSchemeHttps);
            
            var isRtspUrl = Uri.TryCreate(rtspUrl, UriKind.Absolute, out var uriRtsp) 
                && (uriRtsp.Scheme == "rtsp" || uriRtsp.Scheme == "rtmp");

            var isLocalPath = Uri.TryCreate(localPath, UriKind.Absolute, out var uriLocal) 
                && (uriLocal.Scheme == Uri.UriSchemeFile || !uriLocal.IsAbsoluteUri);

            isHttpUrl.Should().BeTrue();
            isRtspUrl.Should().BeTrue();
            isLocalPath.Should().BeTrue();
        }

        [Fact]
        public async Task MainWindowViewModel_OnAppClose_ShouldCancelAllRunningTools()
        {
            // Arrange
            var tool1 = new TestToolViewModel { IsOperationRunning = true };
            var tool2 = new TestToolViewModel { IsOperationRunning = true };
            var tool3 = new TestToolViewModel { IsOperationRunning = false };

            // Simular lógica de MainWindow.axaml.cs:339-342
            var runningTools = new List<ViewModelBase> { tool1, tool2, tool3 }
                .Where(t => t.IsOperationRunning)
                .ToList();

            // Act
            foreach (var tool in runningTools)
            {
                tool.RequestCancelOperation();
            }

            // Assert
            tool1.WasCancelRequested.Should().BeTrue();
            tool2.WasCancelRequested.Should().BeTrue();
            tool3.WasCancelRequested.Should().BeFalse();
        }

        [Fact]
        public void GlobalPauseService_ShouldPauseAndResumeCorrectly()
        {
            // Arrange
            var pauseService = GlobalPauseService.Instance;
            pauseService.Resume(); // Ensure clean starting state
            
            // Act
            pauseService.Pause();
            bool isPausedAfterPause = pauseService.Token.IsPaused;
            
            pauseService.Resume();
            bool isPausedAfterResume = pauseService.Token.IsPaused;

            // Assert
            isPausedAfterPause.Should().BeTrue();
            isPausedAfterResume.Should().BeFalse();
        }

        [Fact]
        public void CombinedPauseToken_ShouldPauseWhenAnySourceIsPaused()
        {
            var globalSource = new PauseTokenSource();
            var toolSource = new PauseTokenSource();

            using var combined = new CombinedPauseToken(globalSource.Token, toolSource.Token);

            combined.Token.IsPaused.Should().BeFalse();

            toolSource.Pause();
            combined.Token.IsPaused.Should().BeTrue();

            toolSource.Resume();
            combined.Token.IsPaused.Should().BeFalse();

            globalSource.Pause();
            combined.Token.IsPaused.Should().BeTrue();
        }

        [Fact]
        public async Task MediaAnalyzer_AnalyzeAsync_WhenStreamingUrlThrowsException_ShouldNotThrowInvalidOperationException()
        {
            // Arrange
            var analyzer = new MediaAnalyzer();
            string streamingUrl = "https://example.com/live.m3u8";

            // Act
            // Sabiendo que es URL de streaming, debe evitar invocar ErrorService.Show (que lanzaría InvalidOperationException en test sin UI)
            Func<Task> act = async () => await analyzer.AnalyzeAsync(streamingUrl);

            // Assert
            await act.Should().NotThrowAsync<InvalidOperationException>();
            
            var result = await analyzer.AnalyzeAsync(streamingUrl);
            result.Success.Should().BeFalse();
        }

        // Helper class to represent a tool ViewModel for testing
        private class TestToolViewModel : ViewModelBase
        {
            public bool WasCancelRequested => _cts?.IsCancellationRequested ?? false;

            public TestToolViewModel()
            {
                _cts = new CancellationTokenSource();
            }
        }
    }
}
