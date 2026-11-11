using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace TP7.Windows;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
        DispatcherUnhandledException += (_, args) =>
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TP7-Wav-Editor");
            try
            {
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "errors.log"), $"{DateTimeOffset.Now:u}\n{args.Exception}\n");
            }
            catch (IOException) { }
            MessageBox.Show($"An error occurred: {args.Exception.Message}\n\nYour source recording remains available. Error details: {folder}", "TP-7 WAV Editor", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        base.OnStartup(e);
    }
}
