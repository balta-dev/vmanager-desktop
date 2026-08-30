using System;
using System.IO;
using System.Threading.Tasks;
using FFMpegCore;
using VManager.Services.Models;
using VManager.Services.Core;

namespace VManager.Services.Core.Media
{
    internal class MediaAnalyzer : IMediaAnalyzer
    {
        public virtual async Task<AnalysisResult<IMediaAnalysis>> AnalyzeAsync(string inputPath)
        {
            bool isUrl = Uri.TryCreate(inputPath, UriKind.Absolute, out var uriResult)
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps || uriResult.Scheme == "rtsp" || uriResult.Scheme == "rtmp");

            try
            {
                if (!isUrl && !File.Exists(inputPath))
                    return new AnalysisResult<IMediaAnalysis>(false, ErrorMessages.FileNotFound);
                
                var mediaInfo = await FFProbe.AnalyseAsync(inputPath);
                
                // Si es un stream, una duración de 0 o menor puede ser normal. Para archivos locales sí es inválida.
                if (!isUrl && mediaInfo.Duration.TotalSeconds <= 0)
                    return new AnalysisResult<IMediaAnalysis>(false, ErrorMessages.InvalidDuration);

                return new AnalysisResult<IMediaAnalysis>(true, "Análisis completado", mediaInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al analizar video {(isUrl ? "(Streaming)" : "(Local)")}. {ex.Message}");
                
                // No mostrar diálogo UI molesto si es un error de streaming/red
                if (!isUrl)
                {
                    ErrorService.Show(ex);
                }
                
                return new AnalysisResult<IMediaAnalysis>(false, string.Format(ErrorMessages.AnalysisError, ex.Message));
            }
        }
    }
}