using System.Diagnostics;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Settings;

namespace JiMes.Api.Features.Printing;

/// <summary>
/// xlsx → PDF (§15.2 P6). 서버에 설치한 LibreOffice 한 곳(print.pdf_converter_path)만 쓰고, 클라이언트에는 설치하지 않는다.
/// LibreOffice 는 같은 사용자 프로필을 동시에 쓰면 실패하므로 변환을 하나씩 직렬로 처리한다 (대기열).
/// </summary>
public sealed class PdfConverter(SettingsCache settings, ILogger<PdfConverter> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<byte[]> ConvertXlsxAsync(byte[] xlsx, CancellationToken ct)
    {
        var soffice = settings.GetString(SettingKeys.PrintPdfConverterPath);
        if (!File.Exists(soffice))
            throw new BusinessRuleException("PDF_CONVERTER_MISSING",
                $"PDF 변환기를 찾을 수 없습니다: {soffice} — 관리자 설정 'print.pdf_converter_path' 를 확인하세요.");

        var timeout = TimeSpan.FromSeconds(settings.GetInt(SettingKeys.PrintPdfConvertTimeoutSec));
        var workDir = Path.Combine(Path.GetTempPath(), "jimes-print", Guid.NewGuid().ToString("N"));
        var profileDir = Path.Combine(Path.GetTempPath(), "jimes-print", "lo-profile");
        Directory.CreateDirectory(workDir);

        await Gate.WaitAsync(ct);
        try
        {
            var input = Path.Combine(workDir, "document.xlsx");
            await File.WriteAllBytesAsync(input, xlsx, ct);

            var psi = new ProcessStartInfo(soffice)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in new[]
                     {
                         $"-env:UserInstallation={new Uri(profileDir).AbsoluteUri}",
                         "--headless", "--norestore", "--convert-to", "pdf", "--outdir", workDir, input,
                     })
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("LibreOffice 를 시작하지 못했습니다.");
            var stderr = process.StandardError.ReadToEndAsync(ct);
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw new BusinessRuleException("PDF_CONVERT_TIMEOUT",
                    $"PDF 변환이 제한시간({timeout.TotalSeconds:0}초)을 넘었습니다. 관리자 설정 'print.pdf_convert_timeout_sec' 를 확인하세요.");
            }

            var output = Path.ChangeExtension(input, ".pdf");
            if (!File.Exists(output))
            {
                logger.LogError("LibreOffice 변환 실패 exit={Exit}: {Err} {Out}", process.ExitCode, await stderr, await stdout);
                throw new BusinessRuleException("PDF_CONVERT_FAILED", "PDF 변환에 실패했습니다. 양식 파일을 확인하세요.");
            }
            return await File.ReadAllBytesAsync(output, ct);
        }
        finally
        {
            Gate.Release();
            try { Directory.Delete(workDir, recursive: true); }
            catch (IOException ex) { logger.LogWarning(ex, "임시 폴더 삭제 실패 {Dir}", workDir); }
        }
    }
}
