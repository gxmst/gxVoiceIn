using System.Windows;

namespace VoiceInputApp.Services.Injection;

public class ClipboardService : IClipboardService
{
    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        return RunStaAsync(() => Clipboard.ContainsText() ? Clipboard.GetText() : null, cancellationToken);
    }

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        return RunStaAsync(() => Clipboard.SetText(text), cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return RunStaAsync(Clipboard.Clear, cancellationToken);
    }

    public Task<bool> RestoreTextIfUnchangedAsync(string expectedCurrentText, string? restoreText, CancellationToken cancellationToken = default)
    {
        return RunStaAsync(() =>
        {
            var currentText = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            if (!string.Equals(currentText, expectedCurrentText, StringComparison.Ordinal))
            {
                return false;
            }

            if (restoreText is null)
            {
                Clipboard.Clear();
            }
            else
            {
                Clipboard.SetText(restoreText);
            }

            return true;
        }, cancellationToken);
    }

    private static Task RunStaAsync(Action action, CancellationToken cancellationToken)
    {
        return RunStaAsync(() =>
        {
            action();
            return true;
        }, cancellationToken);
    }

    private static Task<T> RunStaAsync<T>(Func<T> func, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                tcs.TrySetResult(func());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        return tcs.Task;
    }
}
