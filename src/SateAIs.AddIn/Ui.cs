using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using ArcGIS.Desktop.Framework;

namespace SateAIs.AddIn;

/// <summary>Small helpers shared by the view models: toasts, thread marshalling, fire-and-forget.</summary>
internal static class Ui
{
    /// <summary>A toast in Pro's notification area.</summary>
    public static void Notify(string message)
    {
        FrameworkApplication.AddNotification(new Notification
        {
            Title = "SateAIs",
            Message = message,
            ImageSource = Application.Current.Resources["ToastLicensing32"] as ImageSource,
        });
    }

    public static void OnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    /// <summary>Run after the current UI work finishes, so several changes in one tick produce one notification.</summary>
    public static void Post(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    /// <summary>Await a background task whose failure is not the user's problem.</summary>
    public static async Task Guard(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"SateAIs: {e}");
        }
    }
}
