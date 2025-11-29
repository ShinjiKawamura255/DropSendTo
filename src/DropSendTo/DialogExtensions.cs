using System;
using System.Threading.Tasks;
using System.Windows;

namespace DropSendTo;

internal interface IConfirmableDialog
{
    bool IsConfirmed { get; }
}

internal static class DialogExtensions
{
    public static Task<bool> ShowForResultAsync(this Window dialog)
    {
        if (dialog == null) throw new ArgumentNullException(nameof(dialog));

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnClosed(object? sender, EventArgs args)
        {
            dialog.Closed -= OnClosed;
            bool confirmed = dialog is IConfirmableDialog confirmable && confirmable.IsConfirmed;
            tcs.TrySetResult(confirmed);
        }

        dialog.Closed += OnClosed;
        dialog.Show();

        return tcs.Task;
    }
}
