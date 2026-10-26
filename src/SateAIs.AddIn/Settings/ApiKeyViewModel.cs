using System.Diagnostics;
using SateAIs.AddIn.Analysis;
using SateAIs.Core.Api;
using SateAIs.Core.Credentials;

namespace SateAIs.AddIn.Settings;

internal sealed class ApiKeyViewModel : ObservableObject
{
    /// <summary>Where someone without a key goes. The console root redirects to a login page, which is no use to them.</summary>
    public const string RegisterUrl = "https://console.spcsft.com/register?src=arcgis_addin";

    private string _apiKey = ApiKeys.Stored() ?? "";
    private string _status = "";
    private string _statusKind = "hint";
    private bool _busy;

    public ApiKeyViewModel()
    {
        TestCommand = new DelegateCommand(_ => _ = TestAsync(), _ => !Busy);
        SaveCommand = new DelegateCommand(_ => Save(), _ => !Busy);
        CancelCommand = new DelegateCommand(_ => CloseRequested?.Invoke(false));
        OpenRegisterCommand = new DelegateCommand(_ => Process.Start(new ProcessStartInfo(RegisterUrl) { UseShellExecute = true }));
    }

    /// <summary>true when the key was saved.</summary>
    public event Action<bool>? CloseRequested;

    public string ApiKey
    {
        get => _apiKey;
        set
        {
            if (Set(ref _apiKey, value))
            {
                SetStatus("", "hint");
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string StatusKind
    {
        get => _statusKind;
        private set => Set(ref _statusKind, value);
    }

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                TestCommand.RaiseCanExecuteChanged();
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public DelegateCommand TestCommand { get; }

    public DelegateCommand SaveCommand { get; }

    public DelegateCommand CancelCommand { get; }

    public DelegateCommand OpenRegisterCommand { get; }

    private async Task TestAsync()
    {
        var key = ApiKey.Trim();
        if (key.Length == 0)
        {
            SetStatus("Please enter an API key.", "error");
            return;
        }
        Busy = true;
        SetStatus("Testing connection…", "hint");
        try
        {
            using var client = new SateAIsApiClient(key);
            var account = await client.GetMeAsync();
            SetStatus("✓ " + Sentences.Connected(account), "ok");
        }
        catch (Exception e)
        {
            SetStatus("✕ " + Sentences.ForConnectionTest(e), "error");
        }
        finally
        {
            Busy = false;
        }
    }

    private void Save()
    {
        var key = ApiKey.Trim();
        if (key.Length == 0)
        {
            SetStatus("Please enter an API key.", "error");
            return;
        }
        try
        {
            ApiKeys.Store(key);
        }
        catch (Exception e)
        {
            SetStatus("✕ The key could not be stored: " + e.Message, "error");
            return;
        }
        CloseRequested?.Invoke(true);
    }

    private void SetStatus(string text, string kind)
    {
        Status = text;
        StatusKind = kind;
    }
}
