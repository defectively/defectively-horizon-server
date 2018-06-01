using System.ComponentModel;
using System.Windows;

namespace Defectively.HorizonServer
{
    public partial class MainWindow : Window
    {
        private ServerWrapper wrapper;

        public MainWindow() {
            InitializeComponent();
        }

        private async void OnButtonClick(object sender, RoutedEventArgs e) {
            wrapper = new ServerWrapper();
            await wrapper.Initialize();
        }

        private void OnClosing(object sender, CancelEventArgs e) {
            //e.Cancel = true;
            wrapper.Server.Stop();
            wrapper.Server.Dispose();

        }
    }
}
