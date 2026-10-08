using System.Windows;
using ArcGIS.Desktop.Framework.Controls;

namespace SateAIs.AddIn.Settings;

public partial class ApiKeyWindow : ProWindow
{
    private readonly ApiKeyViewModel _model = new();

    public ApiKeyWindow()
    {
        InitializeComponent();
        DataContext = _model;
        Secret.Password = _model.ApiKey;
        Secret.PasswordChanged += (_, _) => _model.ApiKey = Secret.Password;
        ShowKey.Checked += (_, _) => Plain.Text = Secret.Password;
        ShowKey.Unchecked += (_, _) => Secret.Password = Plain.Text;
        _model.CloseRequested += saved =>
        {
            Saved = saved;
            DialogResult = saved;
            Close();
        };
    }

    public bool Saved { get; private set; }

    /// <summary>Open as a modal dialog; true when a key was saved.</summary>
    public static bool Open(Window? owner)
    {
        var window = new ApiKeyWindow { Owner = owner };
        window.ShowDialog();
        return window.Saved;
    }
}
