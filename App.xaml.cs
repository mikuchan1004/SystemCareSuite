using System.Text;
using System.Windows;

namespace SystemCareSuite
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            base.OnStartup(e);
        }
    }
}
